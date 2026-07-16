using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Writes one row per sync decision to Azure Table Storage. Cheap, durable history that
/// outlives log retention. No-op when no storage connection is configured.
/// </summary>
public class TableAuditService : IAuditService
{
    private readonly ILogger<TableAuditService> _logger;
    private readonly SyncOptions _options;
    private readonly TableClient? _table;
    private bool _ensured;

    public TableAuditService(ILogger<TableAuditService> logger, SyncOptions options)
    {
        _logger = logger;
        _options = options;

        if (!string.IsNullOrWhiteSpace(options.AuditTableConnectionString))
        {
            try
            {
                _table = new TableClient(options.AuditTableConnectionString, options.AuditTableName);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Audit table unavailable, auditing disabled: {Error}", e.Message);
            }
        }
    }

    public async Task RecordAsync(
        string function, string user, string action,
        string? oldValue = null, string? newValue = null, string? detail = null)
    {
        if (_table is null) return;

        try
        {
            if (!_ensured)
            {
                await _table.CreateIfNotExistsAsync();
                _ensured = true;
            }

            var entity = new TableEntity(
                partitionKey: DateTime.UtcNow.ToString("yyyy-MM-dd"),
                rowKey: $"{DateTime.UtcNow:HHmmssfff}-{Guid.NewGuid():N}")
            {
                ["Function"] = function,
                ["User"] = user,
                ["Action"] = action,
                ["OldValue"] = oldValue,
                ["NewValue"] = newValue,
                ["Detail"] = detail,
                ["DryRun"] = _options.DryRun,
                ["TimestampUtc"] = DateTime.UtcNow
            };

            await _table.AddEntityAsync(entity);
        }
        catch (RequestFailedException e)
        {
            _logger.LogWarning("Failed to write audit entry for {User}/{Action}: {Error}", user, action, e.Message);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Unexpected error writing audit entry for {User}/{Action}: {Error}", user, action, e.Message);
        }
    }
}
