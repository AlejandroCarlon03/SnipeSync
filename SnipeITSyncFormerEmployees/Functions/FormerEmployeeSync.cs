using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
namespace SnipeITSyncFormerEmployees;

public class FormerEmployeeSync(
    ILogger<FormerEmployeeSync> logger,
    ISnipeItService snipeItService,
    EntraUserService entraUserService,
    INotificationService notificationService,
    IAuditService auditService,
    IReconciliationQueue reconciliationQueue,
    SyncOptions options)
{
    [Function("FormerEmployeeSync")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer)
    {
        logger.LogInformation("Starting scheduled Former Employee sync at: {Time}{DryRun}",
            DateTime.Now, options.DryRun ? " [DRY-RUN]" : "");

        var summary = new SyncRunSummary("FormerEmployeeSync") { DryRun = options.DryRun };

        var disabledUsers = await entraUserService.GetDisabledUsersAsync();

        if (disabledUsers.Count == 0)
        {
            logger.LogInformation("No disabled Entra ID accounts found. Nothing to do.");
            await notificationService.SendRunSummaryAsync(summary);
            return;
        }

        logger.LogInformation("Found {Count} disabled Entra ID accounts.", disabledUsers.Count);

        foreach (var user in disabledUsers)
        {
            if (user.DisplayName is null || user.Mail is null)
            {
                logger.LogWarning("Skipping user {Id}: missing DisplayName or Mail in Entra ID.", user.Id);
                continue;
            }

            summary.Processed++;

            var snipeUser = await snipeItService.FindSnipeItUser(user.DisplayName, user.Mail);
            if (snipeUser is null)
            {
                logger.LogWarning("No Snipe-IT match found for '{DisplayName}'.", user.DisplayName);
                summary.Unmatched.Add(user.DisplayName);
                await reconciliationQueue.EnqueueAsync(new UnmatchedUser(
                    user.Id, user.DisplayName, user.Mail, "No Snipe-IT match", "FormerEmployeeSync", DateTimeOffset.UtcNow));
                await auditService.RecordAsync("FormerEmployeeSync", user.DisplayName, "SkippedNoMatch", detail: user.Mail);
                continue;
            }

            if (snipeUser.JobTitle == options.FormerEmployeeTitle)
            {
                logger.LogInformation("{DisplayName} is already marked as {Title}, skipping title update.",
                    user.DisplayName, options.FormerEmployeeTitle);
                summary.AlreadyCurrent++;
            }
            else
            {
                var titleUpdated = await snipeItService.SetSnipeItUserTitle(
                    snipeUser.Id, user.DisplayName, snipeUser.JobTitle, options.FormerEmployeeTitle);

                if (titleUpdated)
                {
                    summary.Offboarded++;
                    await auditService.RecordAsync("FormerEmployeeSync", user.DisplayName, "MarkedFormerEmployee",
                        oldValue: snipeUser.JobTitle, newValue: options.FormerEmployeeTitle);
                }
                else
                {
                    logger.LogWarning("Failed to update {DisplayName} in Snipe-IT.", user.DisplayName);
                    summary.Failed++;
                }
            }

            // Feature 1: reclaim the assets that are the real signal of a departure.
            await HandleAssetsAsync(snipeUser.Id, user.DisplayName, summary);
        }

        logger.LogInformation("Former Employee sync completed.");
        await notificationService.SendRunSummaryAsync(summary);
    }

    private async Task HandleAssetsAsync(int snipeUserId, string displayName, SyncRunSummary summary)
    {
        var assets = await snipeItService.GetUserAssets(snipeUserId);
        if (assets.Count == 0)
            return;

        logger.LogInformation("{DisplayName} still has {Count} asset(s) checked out in Snipe-IT.",
            displayName, assets.Count);

        foreach (var asset in assets)
        {
            logger.LogInformation("  - {Asset} ({Model}), status '{Status}'",
                asset.DisplayLabel, asset.Model?.Name ?? "unknown model", asset.StatusLabel?.Name ?? "unknown");

            if (!options.AutoCheckinAssets)
            {
                // Log-only mode: tell IT what to physically reclaim.
                summary.AssetsNeedingAttention.Add($"{displayName}: {asset.DisplayLabel}");
                continue;
            }

            var note = $"Auto check-in: {displayName} deactivated in Entra ID ({DateTime.UtcNow:yyyy-MM-dd}).";
            var checkedIn = await snipeItService.CheckinAsset(
                asset.Id, asset.DisplayLabel, options.DeprovisionedStatusId, note);

            if (checkedIn)
            {
                summary.AssetsCheckedIn++;
                await auditService.RecordAsync("FormerEmployeeSync", displayName, "AssetCheckedIn",
                    oldValue: asset.StatusLabel?.Name, newValue: options.DeprovisionedStatusId?.ToString(),
                    detail: asset.DisplayLabel);
            }
            else
            {
                summary.AssetsNeedingAttention.Add($"{displayName}: {asset.DisplayLabel}");
            }
        }
    }
}
