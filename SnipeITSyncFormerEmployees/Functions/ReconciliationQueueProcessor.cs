using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Drains the sync-unmatched queue (feature 8) — the users FormerEmployeeSync couldn't
/// match to Snipe-IT. Retries the match (including alternate Entra identifiers) and, on a
/// hit, performs the same offboarding action. Genuine misses are audited and escalated;
/// transient write failures bubble up so the runtime retries and eventually moves the
/// message to the sync-unmatched-poison queue — the real dead-letter.
/// </summary>
public class ReconciliationQueueProcessor(
    ILogger<ReconciliationQueueProcessor> logger,
    IOffboardingService offboardingService,
    EntraUserService entraUserService,
    INotificationService notificationService,
    IAuditService auditService,
    SyncOptions options)
{
    [Function("ReconciliationQueueProcessor")]
    public async Task Run(
        [QueueTrigger("%RECONCILIATION_QUEUE_NAME%", Connection = "AzureWebJobsStorage")] UnmatchedUser message)
    {
        logger.LogInformation("Reconciling unmatched user '{DisplayName}' (from {Source}){DryRun}",
            message.DisplayName, message.SourceFunction, options.DryRun ? " [DRY-RUN]" : "");

        var summary = new SyncRunSummary("ReconciliationQueueProcessor") { DryRun = options.DryRun };

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
                await auditService.RecordAsync("ReconciliationQueueProcessor", message.DisplayName,
                    "ReconciledMatch", newValue: outcome.ToString());
                logger.LogInformation("Reconciled {DisplayName}: {Outcome}.", message.DisplayName, outcome);
                break;

            case OffboardOutcome.NotMatched:
                // Definitive miss — a disabled user with genuinely no Snipe-IT record. Consume the
                // message (don't loop), record it, and surface it for a human to review.
                logger.LogWarning("Still no Snipe-IT match for '{DisplayName}' after reconciliation.",
                    message.DisplayName);
                await auditService.RecordAsync("ReconciliationQueueProcessor", message.DisplayName,
                    "UnmatchedAfterReconciliation", detail: message.Email);
                summary.Unmatched.Add(message.DisplayName);
                await notificationService.SendRunSummaryAsync(summary);
                break;

            case OffboardOutcome.Failed:
                // The user matched but a write failed — likely transient. Throw so the runtime
                // retries; after maxDequeueCount (host.json) it lands in sync-unmatched-poison.
                throw new InvalidOperationException(
                    $"Offboarding write failed for '{message.DisplayName}' during reconciliation; will retry.");
        }
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
