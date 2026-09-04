using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Feature 10: reflects each active employee's current Entra M365 license assignments in Snipe-IT.
/// Additive only — it checks out the seats a user is entitled to but doesn't yet hold, and never
/// removes seats (offboard/FormerEmployeeSync already own reclaim). Entra's assignedLicenses carry
/// only skuId GUIDs, so a /subscribedSkus lookup turns those into part numbers (e.g. "SPE_E3"), and
/// LICENSE_SKU_MAP maps each part number to a Snipe-IT license name. When that license doesn't exist
/// yet and LICENSE_AUTO_CREATE is on (default), it's created — sized to the tenant's owned seats —
/// before checkout, so this works even against a Snipe-IT that starts with zero license records.
///
/// Gated behind LICENSE_SYNC_ENABLED so it stays dark until the map is validated in dry-run.
/// Runs at 04:00 — after offboard (02:00) and onboard (03:00) so the three don't hit Snipe-IT at once.
/// </summary>
public class LicenseSyncFunction(
    ILogger<LicenseSyncFunction> logger,
    ISnipeItService snipeItService,
    EntraUserService entraUserService,
    INotificationService notificationService,
    IAuditService auditService,
    SyncOptions options)
{
    [Function("LicenseSync")]
    public async Task Run([TimerTrigger("0 0 4 * * *")] TimerInfo timer)
    {
        if (!options.LicenseSyncEnabled)
        {
            logger.LogInformation("LicenseSync is disabled (set LICENSE_SYNC_ENABLED=true to enable).");
            return;
        }

        if (options.LicenseSkuMap.Count == 0)
        {
            logger.LogWarning("LicenseSync enabled but LICENSE_SKU_MAP is empty; nothing to sync.");
            return;
        }

        logger.LogInformation("Starting license sync at: {Time}{DryRun}",
            DateTime.Now, options.DryRun ? " [DRY-RUN]" : "");

        var summary = new SyncRunSummary("LicenseSync") { DryRun = options.DryRun };

        // Whole-tenant SKU data (one call per run): skuId GUID → part number, and part number → owned seats.
        var skus = await entraUserService.GetSubscribedSkusAsync();
        if (skus.Count == 0)
            logger.LogWarning("No subscribedSkus returned; every user's licenses will resolve to nothing.");

        var guidToPart = skus.ToDictionary(s => s.SkuId, s => s.PartNumber, StringComparer.OrdinalIgnoreCase);
        var partToSeats = skus
            .GroupBy(s => s.PartNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(x => x.EnabledUnits), StringComparer.OrdinalIgnoreCase);

        // Intended seat count per Snipe-IT license name = sum of owned seats across the SKUs mapped to it.
        // Used only when auto-creating a missing license so it's sized to what the tenant actually owns.
        var seatTargets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (part, name) in options.LicenseSkuMap)
            if (partToSeats.TryGetValue(part, out var owned))
                seatTargets[name] = seatTargets.GetValueOrDefault(name) + owned;

        // Resolve each license name at most once per run (find-or-create), so we never create duplicates
        // and repeat users reuse the same record. A cached null means "unresolvable — don't retry".
        var licenseCache = new Dictionary<string, SnipeItLicenseRef?>(StringComparer.OrdinalIgnoreCase);

        var users = await entraUserService.GetEnabledUsersAsync();
        logger.LogInformation("Evaluating licenses for {Count} enabled Entra account(s).", users.Count);

        foreach (var user in users)
        {
            if (user.DisplayName is null || user.Mail is null)
                continue;

            var desiredLicenses = ResolveDesiredLicenses(user, guidToPart, summary);
            if (desiredLicenses.Count == 0)
                continue;

            summary.Processed++;

            SnipeItUser? snipeUser;
            try
            {
                snipeUser = await snipeItService.FindSnipeItUser(user.DisplayName, user.Mail);
            }
            catch (Exception)
            {
                // Lookup failed (e.g. throttling past the retry policy) — don't guess. Tomorrow's run retries.
                logger.LogWarning("Snipe-IT lookup failed for '{DisplayName}'; skipping this run.", user.DisplayName);
                summary.Failed++;
                continue;
            }

            if (snipeUser is null)
            {
                // No Snipe-IT record — that's OnboardEmployeeSync's job to create, not ours.
                logger.LogInformation("No Snipe-IT match for '{DisplayName}'; skipping license checkout.", user.DisplayName);
                continue;
            }

            // Licenses the user already holds a seat on — makes the sync idempotent (re-runs are no-ops).
            var currentSeats = await snipeItService.GetUserLicenseSeats(snipeUser.Id);
            var heldLicenseNames = currentSeats
                .Where(s => s.LicenseName is not null)
                .Select(s => s.LicenseName!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var licenseName in desiredLicenses)
            {
                if (heldLicenseNames.Contains(licenseName))
                    continue;

                var license = await ResolveOrCreateLicenseAsync(licenseName, seatTargets, licenseCache, summary);
                if (license is null)
                {
                    // Couldn't find or create the license (ambiguous name, or create failed) — already logged.
                    summary.Failed++;
                    continue;
                }

                if (license.Id <= 0)
                {
                    // Dry-run synthetic record from CreateLicense — the real checkout can't run, so just
                    // record the intent so the digest reflects what a live run would do.
                    logger.LogInformation("[DRY-RUN] Would check out '{License}' to '{User}'.", licenseName, user.DisplayName);
                    summary.LicensesCheckedOut++;
                    continue;
                }

                var seatId = await snipeItService.GetFirstFreeSeatId(license.Id);
                if (seatId is null)
                {
                    logger.LogWarning("License '{License}' has no free seat for '{User}'.", licenseName, user.DisplayName);
                    summary.SeatsExhausted.Add($"{user.DisplayName}: {licenseName}");
                    continue;
                }

                var note = $"Auto checkout: '{user.DisplayName}' holds {licenseName} in Entra ID ({DateTime.UtcNow:yyyy-MM-dd}).";
                var checkedOut = await snipeItService.CheckoutLicenseSeat(
                    license.Id, seatId.Value, snipeUser.Id, licenseName, note);

                if (checkedOut)
                {
                    summary.LicensesCheckedOut++;
                    await auditService.RecordAsync("LicenseSync", user.DisplayName, "LicenseCheckedOut",
                        detail: licenseName);
                }
                else
                {
                    summary.Failed++;
                }
            }
        }

        logger.LogInformation("License sync completed. {Count} seat(s) checked out{DryRun}.",
            summary.LicensesCheckedOut, options.DryRun ? " (dry-run)" : "");
        await notificationService.SendRunSummaryAsync(summary);
    }

    /// <summary>
    /// Translates a user's assignedLicenses into the distinct Snipe-IT license names they should hold:
    /// skuId GUID → part number (<paramref name="skuMap"/>) → mapped name (LICENSE_SKU_MAP). Part numbers
    /// present in the tenant but absent from LICENSE_SKU_MAP are collected for the digest so gaps surface.
    /// </summary>
    private List<string> ResolveDesiredLicenses(
        User user, IReadOnlyDictionary<string, string> guidToPart, SyncRunSummary summary)
    {
        var result = new List<string>();
        foreach (var skuId in EntraUserService.GetAssignedSkuIds(user))
        {
            // Unknown skuId (no subscribedSkus entry) — nothing we can name; skip quietly.
            if (!guidToPart.TryGetValue(skuId, out var partNumber))
                continue;

            if (options.LicenseSkuMap.TryGetValue(partNumber, out var snipeName))
            {
                if (!result.Contains(snipeName, StringComparer.OrdinalIgnoreCase))
                    result.Add(snipeName);
            }
            else if (!summary.UnmappedSkus.Contains(partNumber, StringComparer.OrdinalIgnoreCase))
            {
                summary.UnmappedSkus.Add(partNumber);
            }
        }
        return result;
    }

    /// <summary>
    /// Finds the named Snipe-IT license, creating it (sized from <paramref name="seatTargets"/>) when it
    /// doesn't exist and auto-create is enabled. Results are memoized in <paramref name="cache"/> so a
    /// license is resolved — and created — at most once per run.
    /// </summary>
    private async Task<SnipeItLicenseRef?> ResolveOrCreateLicenseAsync(
        string licenseName,
        IReadOnlyDictionary<string, int> seatTargets,
        Dictionary<string, SnipeItLicenseRef?> cache,
        SyncRunSummary summary)
    {
        if (cache.TryGetValue(licenseName, out var cached))
            return cached;

        var license = await snipeItService.FindLicenseByName(licenseName);

        if (license is null && options.LicenseAutoCreate)
        {
            var seats = seatTargets.TryGetValue(licenseName, out var owned) && owned > 0 ? owned : 1;
            var note = $"Auto-created by LicenseSync to mirror Entra M365 assignments ({DateTime.UtcNow:yyyy-MM-dd}).";
            license = await snipeItService.CreateLicense(licenseName, seats, options.LicenseCategoryId, note);
            if (license is not null)
                summary.LicensesCreated++;
        }

        cache[licenseName] = license;
        return license;
    }
}
