using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Writes one document per sync decision to Azure Cosmos DB (feature 5, Cosmos backend). Unlike the
/// Table Storage backend this history is genuinely queryable — see <see cref="AuditQueryFunction"/>.
/// Selected over <see cref="TableAuditService"/> in DI when COSMOS_CONNECTION_STRING is set; no-op
/// when the client isn't available, mirroring the Table service's graceful-degradation contract.
/// </summary>
public class CosmosAuditService : IAuditService
{
    private readonly ILogger<CosmosAuditService> _logger;
    private readonly SyncOptions _options;
    private readonly CosmosClient? _client;
    private Container? _container;
    private bool _ensured;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    public CosmosAuditService(ILogger<CosmosAuditService> logger, SyncOptions options, CosmosClient? client = null)
    {
        _logger = logger;
        _options = options;
        _client = client;
    }

    public async Task RecordAsync(
        string function, string user, string action,
        string? oldValue = null, string? newValue = null, string? detail = null)
    {
        if (_client is null) return;

        try
        {
            var container = await EnsureContainerAsync();
            var record = AuditRecord.Create(
                function, user, action, oldValue, newValue, detail, _options.DryRun, DateTimeOffset.UtcNow);

            await container.CreateItemAsync(record, new PartitionKey(record.Ym));
        }
        catch (CosmosException e)
        {
            _logger.LogWarning("Failed to write Cosmos audit entry for {User}/{Action}: {Status} {Error}",
                user, action, e.StatusCode, e.Message);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Unexpected error writing Cosmos audit entry for {User}/{Action}: {Error}",
                user, action, e.Message);
        }
    }

    /// <summary>
    /// Lazily creates the database + container (partition key <c>/ym</c>) on first write, guarded so
    /// the create-if-not-exists round-trips happen exactly once per instance.
    /// </summary>
    private async Task<Container> EnsureContainerAsync()
    {
        if (_ensured && _container is not null) return _container;

        await _ensureLock.WaitAsync();
        try
        {
            if (_ensured && _container is not null) return _container;

            var database = await _client!.CreateDatabaseIfNotExistsAsync(_options.CosmosDatabaseName);
            var container = await database.Database.CreateContainerIfNotExistsAsync(
                _options.CosmosAuditContainer, partitionKeyPath: "/ym");

            _container = container.Container;
            _ensured = true;
            return _container;
        }
        finally
        {
            _ensureLock.Release();
        }
    }
}
