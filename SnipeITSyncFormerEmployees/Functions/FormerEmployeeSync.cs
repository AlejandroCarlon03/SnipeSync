using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
namespace SnipeITSyncFormerEmployees;

public class FormerEmployeeSync(
    ILogger<FormerEmployeeSync> logger,
    IOffboardingService offboardingService,
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

            var outcome = await offboardingService.OffboardUserAsync(user.DisplayName, user.Mail, summary);

            if (outcome == OffboardOutcome.NotMatched)
            {
                summary.Unmatched.Add(user.DisplayName);
                await reconciliationQueue.EnqueueAsync(new UnmatchedUser(
                    user.Id, user.DisplayName, user.Mail, "No Snipe-IT match", "FormerEmployeeSync", DateTimeOffset.UtcNow));
                await auditService.RecordAsync("FormerEmployeeSync", user.DisplayName, "SkippedNoMatch", detail: user.Mail);
            }
        }

        logger.LogInformation("Former Employee sync completed.");
        await notificationService.SendRunSummaryAsync(summary);
    }
}
