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

        // Parameterized SQL — never interpolate user input into the query text. Each "@p IS NULL OR
        // c.x = @p" clause makes the filter optional without branching the query string.
        var sql = """
            SELECT * FROM c
            WHERE (@user IS NULL OR c.user = @user)
              AND (@action IS NULL OR c.action = @action)
              AND (@function IS NULL OR c.function = @function)
              AND (@from IS NULL OR c.timestampUtc >= @from)
              AND (@to IS NULL OR c.timestampUtc <= @to)
            ORDER BY c.timestampUtc DESC
            OFFSET 0 LIMIT @limit
            """;

        var query = new QueryDefinition(sql)
            .WithParameter("@user", user)
            .WithParameter("@action", action)
            .WithParameter("@function", function)
            .WithParameter("@from", from?.ToString("o"))
            .WithParameter("@to", to?.ToString("o"))
            .WithParameter("@limit", limit);

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
