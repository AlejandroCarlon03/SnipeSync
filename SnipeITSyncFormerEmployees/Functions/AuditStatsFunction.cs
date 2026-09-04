using System.Net;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Aggregate side of the audit trail: GET /api/audit/stats returns totals grouped by action, function
/// and month, so the dashboard can show rollups without pulling every row. Backend-agnostic via
/// <see cref="IAuditReader"/> — Cosmos server-side GROUP BY in Azure, in-memory grouping over Table
/// Storage when local. Function-key protected like <see cref="AuditQueryFunction"/>.
///
/// Query params (all optional): function, dryRun (true/false), from (ISO date), to (ISO date).
/// Example: /api/audit/stats?from=2026-07-01&to=2026-07-31&dryRun=false
/// </summary>
public class AuditStatsFunction(ILogger<AuditStatsFunction> logger, IAuditReader reader)
{
    [Function("AuditStats")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "audit/stats")] HttpRequestData req)
    {
        if (!reader.IsAvailable)
        {
            logger.LogWarning("GET /api/audit/stats called but no audit backend is available.");
            return await Text(req, HttpStatusCode.ServiceUnavailable,
                "Audit backend is not configured. Set AUDIT_BACKEND=table (local Azurite) or COSMOS_CONNECTION_STRING (Azure).");
        }

        var q = HttpUtility.ParseQueryString(req.Url.Query);
        var function = Trimmed(q["function"]);

        if (!TryParseBool(q["dryRun"], out var dryRun))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'dryRun'; use true or false.");
        if (!TryParseDate(q["from"], endOfDayIfDateOnly: false, out var from))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'from' date; use ISO 8601 (e.g. 2026-07-01).");
        if (!TryParseDate(q["to"], endOfDayIfDateOnly: true, out var to))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'to' date; use ISO 8601 (e.g. 2026-07-31).");

        try
        {
            var stats = await reader.StatsAsync(
                new AuditStatsFilter(function, dryRun, from, to), req.FunctionContext.CancellationToken);

            var payload = new AuditStatsResponse
            {
                From = from?.ToString("o"),
                To = to?.ToString("o"),
                Function = function,
                DryRun = dryRun,
                Total = stats.Total,
                ByAction = stats.ByAction.ToList(),
                ByFunction = stats.ByFunction.ToList(),
                ByMonth = stats.ByMonth.ToList()
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(payload);
            logger.LogInformation("GET /api/audit/stats: total {Total} across {Actions} action(s).",
                stats.Total, stats.ByAction.Count);
            return response;
        }
        catch (AuditReaderException e)
        {
            logger.LogWarning("Audit stats query failed: {Error}", e.Message);
            return await Text(req, HttpStatusCode.InternalServerError, "Audit stats query failed.");
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseBool(string? raw, out bool? value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = null; return true; }
        if (bool.TryParse(raw, out var parsed)) { value = parsed; return true; }
        value = null;
        return false;
    }

    private static bool TryParseDate(string? raw, bool endOfDayIfDateOnly, out DateTimeOffset? value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = null; return true; }
        if (!DateTimeOffset.TryParse(raw, out var parsed)) { value = null; return false; }
        // A date-only "to" bound must include the whole day: "to=2026-09-04" parses to midnight, which
        // would exclude every row stamped later that day. Extend it to the last tick of the day so the
        // range stays inclusive. "from" keeps start-of-day, so the day-only case covers the full span.
        if (endOfDayIfDateOnly && !raw.Contains('T') && !raw.Contains(':'))
            parsed = new DateTimeOffset(parsed.Date, parsed.Offset).AddDays(1).AddTicks(-1);
        value = parsed;
        return true;
    }

    private static async Task<HttpResponseData> Text(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteStringAsync(message);
        return response;
    }
}

/// <summary>One <c>GROUP BY</c> bucket: the grouped value and its row count.</summary>
public record CountBucket
{
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("count")] public int Count { get; init; }
}

/// <summary>Aggregate response for GET /api/audit/stats.</summary>
public record AuditStatsResponse
{
    [JsonPropertyName("from")] public string? From { get; init; }
    [JsonPropertyName("to")] public string? To { get; init; }
    [JsonPropertyName("function")] public string? Function { get; init; }
    [JsonPropertyName("dryRun")] public bool? DryRun { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("byAction")] public required List<CountBucket> ByAction { get; init; }
    [JsonPropertyName("byFunction")] public required List<CountBucket> ByFunction { get; init; }
    [JsonPropertyName("byMonth")] public required List<CountBucket> ByMonth { get; init; }
}
