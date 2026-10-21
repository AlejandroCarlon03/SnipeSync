using System.Net;
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
                AuditQueryParsing.BackendUnavailableMessage);
        }

        var q = HttpUtility.ParseQueryString(req.Url.Query);
        var function = AuditQueryParsing.Trimmed(q["function"]);

        if (!AuditQueryParsing.TryParseBool(q["dryRun"], out var dryRun))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'dryRun'; use true or false.");
        if (!AuditQueryParsing.TryParseDate(q["from"], endOfDayIfDateOnly: false, out var from))
            return await Text(req, HttpStatusCode.BadRequest, "Invalid 'from' date; use ISO 8601 (e.g. 2026-07-01).");
        if (!AuditQueryParsing.TryParseDate(q["to"], endOfDayIfDateOnly: true, out var to))
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

    private static async Task<HttpResponseData> Text(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteStringAsync(message);
        return response;
    }
}

