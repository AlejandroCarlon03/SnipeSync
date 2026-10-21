namespace SnipeITSyncFormerEmployees;

/// <summary>Queues Entra users that couldn't be matched to Snipe-IT for a second-pass reconciliation (feature 8).</summary>
public interface IReconciliationQueue
{
    Task EnqueueAsync(UnmatchedUser user);
}
