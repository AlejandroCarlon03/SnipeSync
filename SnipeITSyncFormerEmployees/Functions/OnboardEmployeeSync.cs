using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
namespace SnipeITSyncFormerEmployees;

public class OnboardEmployeeSync(
    ILogger<OnboardEmployeeSync> logger,
    ISnipeItService snipeItService,
    EntraUserService entraUserService,
    INotificationService notificationService,
    IAuditService auditService,
    SyncOptions options)
{
    [Function("OnboardEmployeeSync")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo myTimer)
    {
        logger.LogInformation("Starting scheduled sync for new employees at: {Time}{DryRun}",
            DateTime.Now, options.DryRun ? " [DRY-RUN]" : "");

        var summary = new SyncRunSummary("OnboardEmployeeSync") { DryRun = options.DryRun };

        await OnboardRecentUsersAsync(summary);

        // Feature 3: also catch re-enabled long-standing accounts still flagged as former employees.
        if (options.RehireFullScan)
            await RehireScanAsync(summary);

        logger.LogInformation("Onboarding sync completed.");
        await notificationService.SendRunSummaryAsync(summary);
    }

    private async Task OnboardRecentUsersAsync(SyncRunSummary summary)
    {
        var recentlyCreatedUsers = await entraUserService.GetRecentlyCreatedEnabledUsersAsync();

        if (recentlyCreatedUsers.Count == 0)
        {
            logger.LogInformation("No recently created Entra ID accounts found.");
            return;
        }

        logger.LogInformation("Found {Count} recently created Entra ID accounts.", recentlyCreatedUsers.Count);

        foreach (var user in recentlyCreatedUsers)
        {
            if (user.DisplayName is null || user.Mail is null ||
                user.GivenName is null || user.Surname is null || user.MailNickname is null)
            {
                logger.LogWarning("Skipping user {Id}: missing required fields in Entra ID.", user.Id);
                continue;
            }

            summary.Processed++;

            var snipeUser = await snipeItService.FindSnipeItUser(user.DisplayName, user.Mail);

            if (snipeUser is not null)
            {
                // Existing Snipe-IT record: rehire revert if flagged former, otherwise keep fields fresh.
                if (snipeUser.JobTitle == options.FormerEmployeeTitle)
                    await RevertRehireAsync(user, snipeUser, summary);
                else
                {
                    logger.LogInformation("{DisplayName} already exists in Snipe-IT, refreshing fields.", user.DisplayName);
                    await SyncUserFieldsAsync(user, snipeUser.Id, summary);
                }
                continue;
            }

            var jobTitle = user.JobTitle ?? options.NewEmployeeTitle;
            var fields = entraUserService.BuildUserFields(user);

            var created = await snipeItService.CreateSnipeItUser(
                user.GivenName, user.Surname, user.Mail, user.MailNickname, jobTitle,
                fields.Count > 0 ? fields : null);

            if (created)
            {
                logger.LogInformation("{DisplayName} successfully added to Snipe-IT.", user.DisplayName);
                summary.Onboarded++;
                await auditService.RecordAsync("OnboardEmployeeSync", user.DisplayName, "Created",
                    newValue: jobTitle, detail: user.Mail);
            }
            else
            {
                logger.LogWarning("Failed to add {DisplayName} to Snipe-IT.", user.DisplayName);
                summary.Failed++;
            }
        }
    }

    private async Task RehireScanAsync(SyncRunSummary summary)
    {
        var enabledUsers = await entraUserService.GetEnabledUsersAsync();
        logger.LogInformation("Rehire scan: checking {Count} enabled Entra accounts.", enabledUsers.Count);

        foreach (var user in enabledUsers)
        {
            if (user.DisplayName is null || user.Mail is null)
                continue;

            var snipeUser = await snipeItService.FindSnipeItUser(user.DisplayName, user.Mail);
            if (snipeUser is not null && snipeUser.JobTitle == options.FormerEmployeeTitle)
                await RevertRehireAsync(user, snipeUser, summary);
        }
    }

    private async Task RevertRehireAsync(User user, SnipeItUser snipeUser, SyncRunSummary summary)
    {
        var restoredTitle = options.RehireTitle ?? user.JobTitle ?? options.NewEmployeeTitle;

        var reverted = await snipeItService.SetSnipeItUserTitle(
            snipeUser.Id, user.DisplayName!, snipeUser.JobTitle, restoredTitle);

        if (reverted)
        {
            logger.LogInformation("Rehire detected: reverted {DisplayName} from '{Former}' to '{Restored}'.",
                user.DisplayName, options.FormerEmployeeTitle, restoredTitle);
            summary.Reactivated++;
            await auditService.RecordAsync("OnboardEmployeeSync", user.DisplayName!, "Rehired",
                oldValue: options.FormerEmployeeTitle, newValue: restoredTitle);
            await SyncUserFieldsAsync(user, snipeUser.Id, summary);
        }
        else
        {
            logger.LogWarning("Failed to revert rehire for {DisplayName}.", user.DisplayName);
            summary.Failed++;
        }
    }

    private async Task SyncUserFieldsAsync(User user, int snipeUserId, SyncRunSummary summary)
    {
        if (!options.SyncUserFields)
            return;

        var fields = entraUserService.BuildUserFields(user);
        if (fields.Count == 0)
            return;

        await snipeItService.SetSnipeItUserFields(snipeUserId, user.DisplayName!, fields);
    }
}
