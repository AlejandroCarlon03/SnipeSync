using Microsoft.Extensions.Logging;
namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Offboards every disabled Entra account: flags the matching Snipe-IT user as a former employee and
/// reclaims what they hold. Users with no Snipe-IT match go to the reconciliation queue. Host-agnostic:
/// the Azure timer and the dashboard's Sync button both call <see cref="RunAsync"/>.
/// </summary>
public class FormerEmployeeSyncJob(
    ILogger<FormerEmployeeSyncJob> logger,
    IOffboardingService offboardingService,
    EntraUserService entraUserService,
    INotificationService notificationService,
    IAuditService auditService,
    IReconciliationQueue reconciliationQueue,
    SyncOptions options)
{
    public const string Name = "FormerEmployeeSync";

    public async Task<SyncRunSummary> RunAsync()
    {
        logger.LogInformation("Starting Former Employee sync at: {Time}{DryRun}",
            DateTime.Now, options.DryRun ? " [DRY-RUN]" : "");

        var summary = new SyncRunSummary(Name) { DryRun = options.DryRun };

        var disabledUsers = await entraUserService.GetDisabledUsersAsync();

        if (disabledUsers.Count == 0)
        {
            logger.LogInformation("No disabled Entra ID accounts found. Nothing to do.");
            await notificationService.SendRunSummaryAsync(summary);
            return summary;
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

            var outcome = await offboardingService.OffboardUserAsync(user.DisplayName, user.Mail, summary);

            if (outcome == OffboardOutcome.NotMatched)
            {
                summary.Unmatched.Add(user.DisplayName);
                await reconciliationQueue.EnqueueAsync(new UnmatchedUser(
                    user.Id, user.DisplayName, user.Mail, "No Snipe-IT match", Name, DateTimeOffset.UtcNow));
                await auditService.RecordAsync(Name, user.DisplayName, "SkippedNoMatch", detail: user.Mail);
            }
            else if (outcome == OffboardOutcome.LookupFailed)
            {
                // Transient lookup failure — do NOT enqueue (that's what caused the 429 storm). Leave the
                // user untouched; they're still disabled in Entra, so the next run retries them.
                logger.LogWarning("Skipping '{DisplayName}' this run — Snipe-IT lookup failed; will retry next run.",
                    user.DisplayName);
                summary.Failed++;
            }
        }

        logger.LogInformation("Former Employee sync completed.");
        await notificationService.SendRunSummaryAsync(summary);
        return summary;
    }
}
