using System.Net;
using System.Web;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Read side of the Cosmos audit trail (feature 5, Cosmos backend): GET /api/audit runs a
/// parameterized Cosmos SQL query over the audit history and returns JSON. Function-key protected
/// because the response contains employee PII (names, emails).
///
/// Query params (all optional): user, action, function, from (ISO date), to (ISO date),
/// limit (default 100, max 1000). Example: /api/audit?user=Jane%20Doe&from=2026-07-01&limit=50
/// </summary>
public class AuditQueryFunction(ILogger<AuditQueryFunction> logger, SyncOptions options, CosmosClient? cosmos = null)
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 1000;

    [Function("AuditQuery")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "audit")] HttpRequestData req)
    {
        if (cosmos is null || !options.UseCosmosAudit)
        {
            logger.LogWarning("GET /api/audit called but no Cosmos backend is configured.");
            return await Text(req, HttpStatusCode.ServiceUnavailable,
                "Cosmos audit backend is not configured (set COSMOS_CONNECTION_STRING).");
        }

        var q = HttpUtility.ParseQueryString(req.Url.Query);
        var user = Trimmed(q["user"]);
        var action = Trimmed(q["action"]);
        var function = Trimmed(q["function"]);
        var limit = ParseLimit(q["limit"]);

        if (!TryParseDate(q["from"], out var from))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'from' date; use ISO 8601 (e.g. 2026-07-01).");
        if (!TryParseDate(q["to"], out var to))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'to' date; use ISO 8601 (e.g. 2026-07-31).");

        // Build the WHERE clause dynamically — only filters that were actually supplied become
        // conditions. Cosmos NoSQL has no "@param IS NULL" postfix operator (that's a T-SQL-ism),
        // so the optional-filter trick from SQL Server doesn't apply here. The condition fragments
        // are fixed strings; only values flow through parameters, so this stays injection-safe.
        var conditions = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        if (user is not null) { conditions.Add("c.user = @user"); parameters.Add(("@user", user)); }
        if (action is not null) { conditions.Add("c.action = @action"); parameters.Add(("@action", action)); }
        if (function is not null) { conditions.Add("c.function = @function"); parameters.Add(("@function", function)); }
        if (from is not null) { conditions.Add("c.timestampUtc >= @from"); parameters.Add(("@from", from.Value.ToString("o"))); }
        if (to is not null) { conditions.Add("c.timestampUtc <= @to"); parameters.Add(("@to", to.Value.ToString("o"))); }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        // limit is a validated, clamped int (not user text), so inlining it avoids any question of
        // parameterized-LIMIT support across Cosmos versions.
        var sql = $"SELECT * FROM c {where} ORDER BY c.timestampUtc DESC OFFSET 0 LIMIT {limit}";

        var query = new QueryDefinition(sql);
        foreach (var (name, value) in parameters)
            query = query.WithParameter(name, value);

        try
        {
            var container = cosmos.GetContainer(options.CosmosDatabaseName, options.CosmosAuditContainer);
            var results = new List<AuditRecord>(limit);
            double ruCharge = 0;

            // No PartitionKey on the query options → cross-partition (queries can span months).
            using var iterator = container.GetItemQueryIterator<AuditRecord>(query);
            while (iterator.HasMoreResults && results.Count < limit)
            {
                var page = await iterator.ReadNextAsync();
                ruCharge += page.RequestCharge;
                results.AddRange(page);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("x-cosmos-ru", ruCharge.ToString("0.##"));
            await response.WriteAsJsonAsync(results);
            logger.LogInformation("GET /api/audit returned {Count} row(s), {RU} RU.", results.Count, ruCharge);
            return response;
        }
        catch (CosmosException e)
        {
            logger.LogWarning("Audit query failed: {Status} {Error}", e.StatusCode, e.Message);
            return await Text(req, HttpStatusCode.InternalServerError, "Audit query failed.");
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseLimit(string? raw) =>
        int.TryParse(raw, out var value) ? Math.Clamp(value, 1, MaxLimit) : DefaultLimit;

    private static bool TryParseDate(string? raw, out DateTimeOffset? value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = null;
            return true;
        }

        if (DateTimeOffset.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

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
