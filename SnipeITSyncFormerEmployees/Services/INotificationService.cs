namespace SnipeITSyncFormerEmployees;

/// <summary>Sends a post-run summary digest so IT gets visibility without checking App Insights.</summary>
public interface INotificationService
{
    Task SendRunSummaryAsync(SyncRunSummary summary);
}
