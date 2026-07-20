using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

public enum OffboardOutcome
{
    /// <summary>User matched and their title was flipped to the former-employee title.</summary>
    Offboarded,
    /// <summary>User matched but was already flagged former (assignments still reclaimed).</summary>
    AlreadyFormer,
    /// <summary>No Snipe-IT user matched — caller decides whether to enqueue / escalate.</summary>
    NotMatched,
    /// <summary>
    /// The Snipe-IT lookup itself failed (e.g. throttling that outlasted the retry policy), so we
    /// genuinely don't know whether the user exists. Distinct from NotMatched: the caller must NOT
    /// treat this as a definitive miss (don't enqueue for reconciliation, don't consume the message).
    /// </summary>
    LookupFailed,
    /// <summary>User matched but the title update failed.</summary>
    Failed
}

/// <summary>
/// Offboards a single departed employee in Snipe-IT: flips their title and reclaims
/// everything assigned to them (hardware assets, license seats, accessories). Shared
/// by the scheduled FormerEmployeeSync and the ReconciliationQueueProcessor so both
/// take the exact same action.
/// </summary>
public interface IOffboardingService
{
    Task<OffboardOutcome> OffboardUserAsync(string displayName, string? email, SyncRunSummary summary);
}

public class OffboardingService(
    ILogger<OffboardingService> logger,
    ISnipeItService snipeItService,
    IAuditService auditService,
    SyncOptions options) : IOffboardingService
{
    public async Task<OffboardOutcome> OffboardUserAsync(string displayName, string? email, SyncRunSummary summary)
    {
        SnipeItUser? snipeUser;
        try
        {
            snipeUser = await snipeItService.FindSnipeItUser(displayName, email ?? string.Empty);
        }
        catch (Exception e)
        {
            // Couldn't complete the lookup — don't guess. Signal LookupFailed so the caller retries
            // later rather than mislabeling a real employee as unmatched.
            logger.LogWarning("Snipe-IT lookup failed for '{DisplayName}': {Error}", displayName, e.Message);
            return OffboardOutcome.LookupFailed;
        }

        if (snipeUser is null)
        {
            logger.LogWarning("No Snipe-IT match found for '{DisplayName}'.", displayName);
            return OffboardOutcome.NotMatched;
        }

        var outcome = OffboardOutcome.AlreadyFormer;
        if (snipeUser.JobTitle == options.FormerEmployeeTitle)
        {
            logger.LogInformation("{DisplayName} is already marked as {Title}, skipping title update.",
                displayName, options.FormerEmployeeTitle);
            summary.AlreadyCurrent++;
        }
        else
        {
            var titleUpdated = await snipeItService.SetSnipeItUserTitle(
                snipeUser.Id, displayName, snipeUser.JobTitle, options.FormerEmployeeTitle);

            if (titleUpdated)
            {
                summary.Offboarded++;
                outcome = OffboardOutcome.Offboarded;
                await auditService.RecordAsync(summary.FunctionName, displayName, "MarkedFormerEmployee",
                    oldValue: snipeUser.JobTitle, newValue: options.FormerEmployeeTitle);
            }
            else
            {
                logger.LogWarning("Failed to update {DisplayName} in Snipe-IT.", displayName);
                summary.Failed++;
                outcome = OffboardOutcome.Failed;
            }
        }

        // Reclaim everything assigned to the departed user — the real offboarding signal.
        await HandleAssetsAsync(snipeUser.Id, displayName, summary);
        await HandleLicensesAsync(snipeUser.Id, displayName, summary);
        await HandleAccessoriesAsync(snipeUser.Id, displayName, summary);

        return outcome;
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
                summary.AssetsNeedingAttention.Add($"{displayName}: {asset.DisplayLabel}");
                continue;
            }

            var note = $"Auto check-in: {displayName} deactivated in Entra ID ({DateTime.UtcNow:yyyy-MM-dd}).";
            var checkedIn = await snipeItService.CheckinAsset(
                asset.Id, asset.DisplayLabel, options.DeprovisionedStatusId, note);

            if (checkedIn)
            {
                summary.AssetsCheckedIn++;
                await auditService.RecordAsync(summary.FunctionName, displayName, "AssetCheckedIn",
                    oldValue: asset.StatusLabel?.Name, newValue: options.DeprovisionedStatusId?.ToString(),
                    detail: asset.DisplayLabel);
            }
            else
            {
                summary.AssetsNeedingAttention.Add($"{displayName}: {asset.DisplayLabel}");
            }
        }
    }

    private async Task HandleLicensesAsync(int snipeUserId, string displayName, SyncRunSummary summary)
    {
        var seats = await snipeItService.GetUserLicenseSeats(snipeUserId);
        if (seats.Count == 0)
            return;

        logger.LogInformation("{DisplayName} still holds {Count} license seat(s) in Snipe-IT.",
            displayName, seats.Count);

        foreach (var seat in seats)
        {
            logger.LogInformation("  - {Seat}", seat.DisplayLabel);

            if (!options.AutoCheckinLicenses)
            {
                summary.AssetsNeedingAttention.Add($"{displayName}: {seat.DisplayLabel}");
                continue;
            }

            var note = $"Auto reclaim: {displayName} deactivated in Entra ID ({DateTime.UtcNow:yyyy-MM-dd}).";
            var reclaimed = await snipeItService.CheckinLicenseSeat(seat, note);

            if (reclaimed)
            {
                summary.LicensesReclaimed++;
                await auditService.RecordAsync(summary.FunctionName, displayName, "LicenseReclaimed",
                    detail: seat.DisplayLabel);
            }
            else
            {
                summary.AssetsNeedingAttention.Add($"{displayName}: {seat.DisplayLabel}");
            }
        }
    }

    private async Task HandleAccessoriesAsync(int snipeUserId, string displayName, SyncRunSummary summary)
    {
        var accessories = await snipeItService.GetUserAccessories(snipeUserId);
        if (accessories.Count == 0)
            return;

        logger.LogInformation("{DisplayName} still has {Count} accessory(ies) checked out in Snipe-IT.",
            displayName, accessories.Count);

        foreach (var accessory in accessories)
        {
            logger.LogInformation("  - {Accessory}", accessory.DisplayLabel);

            if (!options.AutoCheckinAccessories)
            {
                summary.AssetsNeedingAttention.Add($"{displayName}: {accessory.DisplayLabel}");
                continue;
            }

            var note = $"Auto reclaim: {displayName} deactivated in Entra ID ({DateTime.UtcNow:yyyy-MM-dd}).";
            var reclaimed = await snipeItService.CheckinAccessory(accessory, note);

            if (reclaimed)
            {
                summary.AccessoriesReclaimed++;
                await auditService.RecordAsync(summary.FunctionName, displayName, "AccessoryReclaimed",
                    detail: accessory.DisplayLabel);
            }
            else
            {
                summary.AssetsNeedingAttention.Add($"{displayName}: {accessory.DisplayLabel}");
            }
        }
    }
}
