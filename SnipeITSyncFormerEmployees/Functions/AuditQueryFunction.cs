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
    [Function("AuditQuery")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "audit")] HttpRequestData req)
    {
        if (!reader.IsAvailable)
        {
            logger.LogWarning("GET /api/audit called but no audit backend is available.");
            return await Text(req, HttpStatusCode.ServiceUnavailable,
                AuditQueryParsing.BackendUnavailableMessage);
        }

        var q = HttpUtility.ParseQueryString(req.Url.Query);
        var user = AuditQueryParsing.Trimmed(q["user"]);
        var action = AuditQueryParsing.Trimmed(q["action"]);
        var function = AuditQueryParsing.Trimmed(q["function"]);
        var limit = AuditQueryParsing.ParseLimit(q["limit"]);

        if (!AuditQueryParsing.TryParseDate(q["from"], endOfDayIfDateOnly: false, out var from))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'from' date; use ISO 8601 (e.g. 2026-07-01).");
        if (!AuditQueryParsing.TryParseDate(q["to"], endOfDayIfDateOnly: true, out var to))
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

    private static async Task<HttpResponseData> Text(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteStringAsync(message);
        return response;
    }
}
