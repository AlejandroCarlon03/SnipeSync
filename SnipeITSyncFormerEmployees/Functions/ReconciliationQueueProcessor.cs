using Microsoft.Azure.Functions.Worker;
namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Drains the sync-unmatched queue (feature 8) through <see cref="ReconciliationJob"/>. Transient
/// failures throw so the runtime retries and, after maxDequeueCount (host.json), moves the message to
/// sync-unmatched-poison — the real dead-letter.
/// </summary>
public class ReconciliationQueueProcessor(ReconciliationJob job, SyncOptions options)
{
    [Function(ReconciliationJob.Name)]
    public async Task Run(
        [QueueTrigger("%RECONCILIATION_QUEUE_NAME%", Connection = "AzureWebJobsStorage")] UnmatchedUser message)
    {
        var summary = new SyncRunSummary(ReconciliationJob.Name) { DryRun = options.DryRun };

        switch (await job.ReconcileAsync(message, summary))
        {
            case OffboardOutcome.LookupFailed:
                throw new InvalidOperationException(
                    $"Snipe-IT lookup failed for '{message.DisplayName}' during reconciliation; will retry.");
            case OffboardOutcome.Failed:
                throw new InvalidOperationException(
                    $"Offboarding write failed for '{message.DisplayName}' during reconciliation; will retry.");
        }
    }
}
