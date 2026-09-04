using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Read side of the audit trail: GET /api/audit returns filtered audit history as JSON. Backend-agnostic
/// via <see cref="IAuditReader"/> — Cosmos DB in Azure, Table Storage (Azurite) when running locally.
/// Function-key protected because the response contains employee PII (names, emails).
///
/// Query params (all optional): user, action, function, from (ISO date), to (ISO date),
/// limit (default 100, max 1000). Example: /api/audit?user=Jane%20Doe&from=2026-07-01&limit=50
/// </summary>
public class AuditQueryFunction(ILogger<AuditQueryFunction> logger, IAuditReader reader)
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 1000;

    [Function("AuditQuery")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "audit")] HttpRequestData req)
    {
        if (!reader.IsAvailable)
        {
            logger.LogWarning("GET /api/audit called but no audit backend is available.");
            return await Text(req, HttpStatusCode.ServiceUnavailable,
                "Audit backend is not configured. Set AUDIT_BACKEND=table (local Azurite) or COSMOS_CONNECTION_STRING (Azure).");
        }

        var q = HttpUtility.ParseQueryString(req.Url.Query);
        var user = Trimmed(q["user"]);
        var action = Trimmed(q["action"]);
        var function = Trimmed(q["function"]);
        var limit = ParseLimit(q["limit"]);

        if (!TryParseDate(q["from"], endOfDayIfDateOnly: false, out var from))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'from' date; use ISO 8601 (e.g. 2026-07-01).");
        if (!TryParseDate(q["to"], endOfDayIfDateOnly: true, out var to))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'to' date; use ISO 8601 (e.g. 2026-07-31).");

        try
        {
            var results = await reader.QueryAsync(
                new AuditQueryFilter(user, action, function, from, to, limit), req.FunctionContext.CancellationToken);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(results);
            logger.LogInformation("GET /api/audit returned {Count} row(s).", results.Count);
            return response;
        }
        catch (AuditReaderException e)
        {
            logger.LogWarning("Audit query failed: {Error}", e.Message);
            return await Text(req, HttpStatusCode.InternalServerError, "Audit query failed.");
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseLimit(string? raw) =>
        int.TryParse(raw, out var value) ? Math.Clamp(value, 1, MaxLimit) : DefaultLimit;

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
