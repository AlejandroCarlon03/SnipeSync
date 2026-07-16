using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Pushes unmatched users to an Azure Storage Queue so they aren't silently dropped.
/// A later reconciliation pass (or a manual review of the queue) can retry the match.
/// No-op when no storage connection is configured.
/// </summary>
public class StorageReconciliationQueue : IReconciliationQueue
{
    private readonly ILogger<StorageReconciliationQueue> _logger;
    private readonly QueueClient? _queue;
    private bool _ensured;

    public StorageReconciliationQueue(ILogger<StorageReconciliationQueue> logger, SyncOptions options)
    {
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(options.ReconciliationQueueConnectionString))
        {
            try
            {
                _queue = new QueueClient(
                    options.ReconciliationQueueConnectionString,
                    options.ReconciliationQueueName,
                    new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
            }
            catch (Exception e)
            {
                _logger.LogWarning("Reconciliation queue unavailable, disabled: {Error}", e.Message);
            }
        }
    }

    public async Task EnqueueAsync(UnmatchedUser user)
    {
        if (_queue is null) return;

        try
        {
            if (!_ensured)
            {
                await _queue.CreateIfNotExistsAsync();
                _ensured = true;
            }

            var payload = JsonSerializer.Serialize(user);
            await _queue.SendMessageAsync(payload);
            _logger.LogInformation("Queued unmatched user {DisplayName} for reconciliation.", user.DisplayName);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to queue unmatched user {DisplayName}: {Error}", user.DisplayName, e.Message);
        }
    }
}
