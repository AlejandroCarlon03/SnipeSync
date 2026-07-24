using System.Net;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Aggregate side of the Cosmos audit trail (feature 5, Cosmos backend): GET /api/audit/stats runs
/// COUNT + GROUP BY queries over the audit history and returns totals, so the dashboard can show
/// "how many SkippedNoMatch this month" without pulling every row (GET /api/audit caps at 1000).
/// Function-key protected like <see cref="AuditQueryFunction"/> — the response is aggregate counts
/// (no PII), but it shares the same backend and access story.
///
/// Query params (all optional): function, dryRun (true/false), from (ISO date), to (ISO date).
/// Example: /api/audit/stats?from=2026-07-01&to=2026-07-31&dryRun=false
/// </summary>
public class AuditStatsFunction(ILogger<AuditStatsFunction> logger, SyncOptions options, CosmosClient? cosmos = null)
{
    [Function("AuditStats")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "audit/stats")] HttpRequestData req)
    {
        if (cosmos is null || !options.UseCosmosAudit)
        {
            logger.LogWarning("GET /api/audit/stats called but no Cosmos backend is configured.");
            return await Text(req, HttpStatusCode.ServiceUnavailable,
                "Cosmos audit backend is not configured (set COSMOS_CONNECTION_STRING).");
        }

        var q = HttpUtility.ParseQueryString(req.Url.Query);
        var function = Trimmed(q["function"]);

        if (!TryParseBool(q["dryRun"], out var dryRun))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'dryRun'; use true or false.");
        if (!TryParseDate(q["from"], out var from))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'from' date; use ISO 8601 (e.g. 2026-07-01).");
        if (!TryParseDate(q["to"], out var to))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'to' date; use ISO 8601 (e.g. 2026-07-31).");

        // Same dynamic-WHERE approach as AuditQueryFunction: only supplied filters become conditions,
        // and only values flow through parameters, so this stays injection-safe. The condition
        // fragments are fixed strings.
        var conditions = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        if (function is not null) { conditions.Add("c.function = @function"); parameters.Add(("@function", function)); }
        if (dryRun is not null) { conditions.Add("c.dryRun = @dryRun"); parameters.Add(("@dryRun", dryRun.Value)); }
        if (from is not null) { conditions.Add("c.timestampUtc >= @from"); parameters.Add(("@from", from.Value.ToString("o"))); }
        if (to is not null) { conditions.Add("c.timestampUtc <= @to"); parameters.Add(("@to", to.Value.ToString("o"))); }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;

        try
        {
            var container = cosmos.GetContainer(options.CosmosDatabaseName, options.CosmosAuditContainer);
            var ru = new RuTracker();

            // Three cross-partition GROUP BY aggregations. Grouping on stored properties (action,
            // function, ym) keeps this within Cosmos' well-supported GROUP BY surface. ym is the
            // partition key, so the monthly rollup is the cheapest of the three.
            var byAction = await GroupCountAsync(container, where, parameters, "action", ru);
            var byFunction = await GroupCountAsync(container, where, parameters, "function", ru);
            var byMonth = await GroupCountAsync(container, where, parameters, "ym", ru);

            // Total = sum of the per-action buckets; avoids a fourth round-trip.
            var total = byAction.Sum(b => b.Count);

            var payload = new AuditStatsResponse
            {
                From = from?.ToString("o"),
                To = to?.ToString("o"),
                Function = function,
                DryRun = dryRun,
                Total = total,
                ByAction = byAction,
                ByFunction = byFunction,
                ByMonth = byMonth
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("x-cosmos-ru", ru.Total.ToString("0.##"));
            await response.WriteAsJsonAsync(payload);
            logger.LogInformation("GET /api/audit/stats: total {Total} across {Actions} action(s), {RU} RU.",
                total, byAction.Count, ru.Total);
            return response;
        }
        catch (CosmosException e)
        {
            logger.LogWarning("Audit stats query failed: {Status} {Error}", e.StatusCode, e.Message);
            return await Text(req, HttpStatusCode.InternalServerError, "Audit stats query failed.");
        }
    }

    /// <summary>
    /// Runs <c>SELECT c.{field} AS key, COUNT(1) AS count FROM c {where} GROUP BY c.{field}</c> and
    /// returns the buckets. <paramref name="field"/> is a fixed, caller-supplied column name (never
    /// user input), so it is safe to inline; the filter values still flow through parameters.
    /// </summary>
    private static async Task<List<CountBucket>> GroupCountAsync(
        Container container, string where, List<(string Name, object Value)> parameters, string field, RuTracker ru)
    {
        var sql = $"SELECT c.{field} AS key, COUNT(1) AS count FROM c {where} GROUP BY c.{field}";
        var query = new QueryDefinition(sql);
        foreach (var (name, value) in parameters)
            query = query.WithParameter(name, value);

        var buckets = new List<CountBucket>();
        using var iterator = container.GetItemQueryIterator<CountBucket>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            ru.Add(page.RequestCharge);
            buckets.AddRange(page);
        }

        // Largest buckets first — matches how the dashboard wants to render them.
        buckets.Sort((a, b) => b.Count.CompareTo(a.Count));
        return buckets;
    }

    private sealed class RuTracker
    {
        public double Total { get; private set; }
        public void Add(double charge) => Total += charge;
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

    private static bool TryParseDate(string? raw, out DateTimeOffset? value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = null; return true; }
        if (DateTimeOffset.TryParse(raw, out var parsed)) { value = parsed; return true; }
        value = null;
        return false;
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
