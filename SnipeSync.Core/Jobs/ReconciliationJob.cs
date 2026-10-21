using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Second-pass match for a user <see cref="FormerEmployeeSyncJob"/> couldn't find in Snipe-IT (feature 8).
/// Retries with the stored email, then with alternate Entra identifiers (mail, UPN, proxy addresses), and
/// on a hit performs the same offboarding action. A genuine miss is audited as UnmatchedAfterReconciliation.
/// Retry policy is the host's: the Azure queue processor throws on LookupFailed/Failed so the runtime
/// redelivers, while the dashboard reports them and leaves the user for the next sync.
/// </summary>
public class ReconciliationJob(
    ILogger<ReconciliationJob> logger,
    IOffboardingService offboardingService,
    EntraUserService entraUserService,
    IAuditService auditService,
    SyncOptions options)
{
    /// <summary>Audit rows keep the original Azure function's name so history from both hosts filters together.</summary>
    public const string Name = "ReconciliationQueueProcessor";

    public async Task<OffboardOutcome> ReconcileAsync(UnmatchedUser message, SyncRunSummary summary)
    {
        logger.LogInformation("Reconciling unmatched user '{DisplayName}' (from {Source}){DryRun}",
            message.DisplayName, message.SourceFunction, options.DryRun ? " [DRY-RUN]" : "");

        // First pass: retry with the identifier we originally stored.
        var outcome = await offboardingService.OffboardUserAsync(message.DisplayName, message.Email, summary);

        // Second pass: re-query Entra for fresh/alternate emails and try those.
        if (outcome == OffboardOutcome.NotMatched && message.EntraId is not null)
        {
            var fresh = await entraUserService.GetUserByIdAsync(message.EntraId);
            if (fresh is not null)
            {
                foreach (var candidate in CandidateEmails(fresh, message.Email))
                {
                    outcome = await offboardingService.OffboardUserAsync(message.DisplayName, candidate, summary);
                    if (outcome != OffboardOutcome.NotMatched)
                    {
                        logger.LogInformation("Matched {DisplayName} on alternate identifier {Email}.",
                            message.DisplayName, candidate);
                        break;
                    }
                }
            }
        }

        switch (outcome)
        {
            case OffboardOutcome.Offboarded:
            case OffboardOutcome.AlreadyFormer:
                await auditService.RecordAsync(Name, message.DisplayName,
                    "ReconciledMatch", newValue: outcome.ToString());
                logger.LogInformation("Reconciled {DisplayName}: {Outcome}.", message.DisplayName, outcome);
                break;

            case OffboardOutcome.NotMatched:
                // Definitive miss — a disabled user with genuinely no Snipe-IT record. Consume the
                // message (don't loop) and record it for a human to review.
                //
                // No Teams post here: this runs once per user, so a digest card would
                // fire per unmatched user. FormerEmployeeSync already named them in its run summary
                // when it enqueued them; the audit row below is the durable record of the retry.
                logger.LogWarning("Still no Snipe-IT match for '{DisplayName}' after reconciliation.",
                    message.DisplayName);
                await auditService.RecordAsync(Name, message.DisplayName,
                    "UnmatchedAfterReconciliation", detail: message.Email);
                break;

            case OffboardOutcome.LookupFailed:
                // Couldn't complete the Snipe-IT lookup (throttling that outlasted the retry policy).
                // Not a miss, so it isn't audited as one; the host decides how to retry.
                logger.LogWarning("Snipe-IT lookup failed for '{DisplayName}' during reconciliation.",
                    message.DisplayName);
                summary.Failed++;
                break;

            case OffboardOutcome.Failed:
                // The user matched but a write failed (likely transient); the host decides how to retry.
                logger.LogWarning("Offboarding write failed for '{DisplayName}' during reconciliation.",
                    message.DisplayName);
                break;
        }

        return outcome;
    }

    /// <summary>Distinct alternate emails from a re-queried Entra user, excluding the one already tried.</summary>
    private static IEnumerable<string> CandidateEmails(User user, string? alreadyTried)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (alreadyTried is not null) seen.Add(alreadyTried);

        var candidates = new List<string?> { user.Mail, user.UserPrincipalName };
        if (user.ProxyAddresses is not null)
        {
            // proxyAddresses look like "SMTP:primary@x.com" / "smtp:alias@x.com".
            candidates.AddRange(user.ProxyAddresses.Select(p =>
            {
                var idx = p.IndexOf(':');
                return idx >= 0 ? p[(idx + 1)..] : p;
            }));
        }

        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c) && seen.Add(c))
                yield return c;
        }
    }
}
