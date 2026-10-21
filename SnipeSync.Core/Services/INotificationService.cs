namespace SnipeITSyncFormerEmployees;

/// <summary>Sends a post-run summary digest so IT gets visibility without checking App Insights.</summary>
public interface INotificationService
{
    Task SendRunSummaryAsync(SyncRunSummary summary);

    /// <summary>
    /// Posts a free-form fact digest (title + name/value rows) — used by reports that don't map onto
    /// a <see cref="SyncRunSummary"/>, such as the weekly license-savings report.
    /// </summary>
    Task SendDigestAsync(string title, IReadOnlyList<(string Name, string Value)> facts);
}
