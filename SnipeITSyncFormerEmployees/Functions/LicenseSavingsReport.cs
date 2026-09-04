using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Feature 11: weekly digest of license seats reclaimed on offboard (money the org stops paying),
/// read from the Cosmos audit trail. Groups the last 7 days of "LicenseReclaimed" decisions by
/// license name and, when LICENSE_COST_MAP is set, prices them into a "$/month freed" figure.
///
/// Requires the Cosmos audit backend (like GET /api/audit); with only Table Storage configured it
/// logs and returns. Runs Monday 08:00 UTC. Excludes dry-run rows so simulated reclaims don't inflate it.
/// </summary>
public class LicenseSavingsReport(
    ILogger<LicenseSavingsReport> logger,
    INotificationService notificationService,
    SyncOptions options,
    CosmosClient? cosmos = null)
{
    [Function("LicenseSavingsReport")]
    public async Task Run([TimerTrigger("0 0 8 * * MON")] TimerInfo timer)
    {
        if (cosmos is null || !options.UseCosmosAudit)
        {
            logger.LogWarning("LicenseSavingsReport needs the Cosmos audit backend (set COSMOS_CONNECTION_STRING).");
            return;
        }

        var since = DateTimeOffset.UtcNow.AddDays(-7);

        // Same parameterized-SQL + GROUP BY shape as AuditStatsFunction. Column names are fixed strings;
        // only values flow through parameters, so this stays injection-safe. dryRun=false keeps simulated
        // reclaims out of the savings figure.
        var sql =
            "SELECT c.detail AS key, COUNT(1) AS count FROM c " +
            "WHERE c.action = @action AND c.dryRun = false AND c.timestampUtc >= @since " +
            "GROUP BY c.detail";
        var query = new QueryDefinition(sql)
            .WithParameter("@action", "LicenseReclaimed")
            .WithParameter("@since", since.ToString("o"));

        List<CountBucket> buckets;
        try
        {
            var container = cosmos.GetContainer(options.CosmosDatabaseName, options.CosmosAuditContainer);
            buckets = [];
            using var iterator = container.GetItemQueryIterator<CountBucket>(query);
            while (iterator.HasMoreResults)
                buckets.AddRange(await iterator.ReadNextAsync());
        }
        catch (CosmosException e)
        {
            logger.LogWarning("License savings query failed: {Status} {Error}", e.StatusCode, e.Message);
            return;
        }

        if (buckets.Count == 0)
        {
            logger.LogInformation("No license seats reclaimed in the last 7 days; skipping savings digest.");
            return;
        }

        var totalSeats = buckets.Sum(b => b.Count);
        decimal totalCost = 0m;
        var facts = new List<(string Name, string Value)>();

        // The seat detail may carry a "(seat #N)" suffix; strip it to match LICENSE_COST_MAP keys.
        foreach (var bucket in buckets.OrderByDescending(b => b.Count))
        {
            var licenseName = StripSeatSuffix(bucket.Key);
            var value = $"{bucket.Count} seat(s)";

            if (options.LicenseCostMap.TryGetValue(licenseName, out var unitCost))
            {
                var lineCost = unitCost * bucket.Count;
                totalCost += lineCost;
                value += $" — {lineCost:C}/mo";
            }

            facts.Add((licenseName, value));
        }

        facts.Insert(0, ("Total seats reclaimed", totalSeats.ToString()));
        if (totalCost > 0m)
            facts.Insert(1, ("Estimated monthly savings", $"{totalCost:C}"));

        await notificationService.SendDigestAsync("License savings — last 7 days", facts);
        logger.LogInformation("License savings digest sent: {Seats} seat(s), {Cost:C}/mo across {Licenses} license(s).",
            totalSeats, totalCost, buckets.Count);
    }

    /// <summary>"Microsoft 365 E3 (seat #42)" → "Microsoft 365 E3"; leaves plain names untouched.</summary>
    private static string StripSeatSuffix(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "(unknown license)";
        var idx = detail.IndexOf(" (seat #", StringComparison.Ordinal);
        return (idx > 0 ? detail[..idx] : detail).Trim();
    }
}
