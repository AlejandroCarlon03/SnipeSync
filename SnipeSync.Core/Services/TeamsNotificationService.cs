using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Posts an Adaptive-friendly Teams MessageCard to an Incoming Webhook.
/// When no TEAMS_WEBHOOK_URL is configured this is a no-op (logs only), so the
/// sync keeps working out of the box.
/// </summary>
public class TeamsNotificationService(HttpClient httpClient, ILogger<TeamsNotificationService> logger, SyncOptions options)
    : INotificationService
{
    public async Task SendRunSummaryAsync(SyncRunSummary summary)
    {
        var line =
            $"{summary.FunctionName}: {summary.Offboarded} offboarded, {summary.Onboarded} onboarded, " +
            $"{summary.Reactivated} rehired, {summary.AssetsCheckedIn} assets checked in, " +
            $"{summary.Skipped} skipped (no match), {summary.Failed} failed";
        logger.LogInformation("Run summary — {Summary}", line);

        if (string.IsNullOrWhiteSpace(options.TeamsWebhookUrl))
            return;

        var facts = new List<object>
        {
            Fact("Offboarded", summary.Offboarded),
            Fact("Onboarded", summary.Onboarded),
            Fact("Rehired / reactivated", summary.Reactivated),
            Fact("Assets checked in", summary.AssetsCheckedIn),
            Fact("License seats reclaimed", summary.LicensesReclaimed),
            Fact("Licenses created", summary.LicensesCreated),
            Fact("License seats checked out", summary.LicensesCheckedOut),
            Fact("Accessories reclaimed", summary.AccessoriesReclaimed),
            Fact("Skipped (no Snipe-IT match)", summary.Skipped),
            Fact("Failed", summary.Failed)
        };

        if (summary.Unmatched.Count > 0)
            facts.Add(Fact("Unmatched users", string.Join(", ", summary.Unmatched.Take(15))));
        if (summary.AssetsNeedingAttention.Count > 0)
            facts.Add(Fact("Assets needing manual reclaim", string.Join(", ", summary.AssetsNeedingAttention.Take(15))));
        if (summary.UnmappedSkus.Count > 0)
            facts.Add(Fact("Unmapped Entra SKUs", string.Join(", ", summary.UnmappedSkus.Take(15))));
        if (summary.SeatsExhausted.Count > 0)
            facts.Add(Fact("No free seat (add seats in Snipe-IT)", string.Join(", ", summary.SeatsExhausted.Take(15))));

        var title = summary.DryRun
            ? $"[DRY-RUN] {summary.FunctionName} summary"
            : $"{summary.FunctionName} summary";

        var card = new
        {
            @type = "MessageCard",
            @context = "http://schema.org/extensions",
            themeColor = summary.Failed > 0 ? "D9534F" : "0078D7",
            summary = line,
            title,
            sections = new[]
            {
                new
                {
                    activityTitle = $"Ran at {summary.StartedAt:u}",
                    facts = facts.ToArray()
                }
            }
        };

        try
        {
            var response = await httpClient.PostAsJsonAsync(options.TeamsWebhookUrl, card);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Teams webhook returned {Status} for {Function}.",
                    response.StatusCode, summary.FunctionName);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to post Teams notification: {Error}", e.Message);
        }
    }

    public async Task SendDigestAsync(string title, IReadOnlyList<(string Name, string Value)> facts)
    {
        var line = $"{title}: {string.Join("; ", facts.Select(f => $"{f.Name} {f.Value}"))}";
        logger.LogInformation("Digest — {Summary}", line);

        if (string.IsNullOrWhiteSpace(options.TeamsWebhookUrl))
            return;

        var card = new
        {
            @type = "MessageCard",
            @context = "http://schema.org/extensions",
            themeColor = "0078D7",
            summary = title,
            title,
            sections = new[]
            {
                new
                {
                    activityTitle = $"Generated at {DateTimeOffset.UtcNow:u}",
                    facts = facts.Select(f => Fact(f.Name, f.Value)).ToArray()
                }
            }
        };

        try
        {
            var response = await httpClient.PostAsJsonAsync(options.TeamsWebhookUrl, card);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Teams webhook returned {Status} for digest '{Title}'.", response.StatusCode, title);
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to post Teams digest '{Title}': {Error}", title, e.Message);
        }
    }

    private static object Fact(string name, object value) => new { name, value = value.ToString() };
}
