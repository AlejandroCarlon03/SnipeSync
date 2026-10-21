using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnipeITSyncFormerEmployees;

var builder = FunctionsApplication.CreateBuilder(args);

// The sync engine (options, Snipe-IT/Graph/Teams clients, offboarding, jobs) is shared with the dashboard.
builder.Services.AddSnipeSyncCore();
builder.Services.AddSingleton<IReconciliationQueue, StorageReconciliationQueue>();

// Audit trail (feature 5). Cosmos DB when configured (queryable via GET /api/audit), else the
// Table Storage backend. The CosmosClient is registered only when a connection is present —
// it's a singleton by design (it pools connections) and is resolved as optional everywhere else.
if (Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING") is { Length: > 0 } cosmosConn)
{
    builder.Services.AddSingleton(_ => new CosmosClient(cosmosConn, new CosmosClientOptions
    {
        // Match AuditRecord's camelCase [JsonPropertyName]s so writes and queries agree on shape.
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    }));
}
builder.Services.AddSingleton<IAuditService>(sp =>
{
    var options = sp.GetRequiredService<SyncOptions>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return options.UseCosmosAudit
        ? new CosmosAuditService(loggerFactory.CreateLogger<CosmosAuditService>(), options,
            sp.GetService<CosmosClient>())
        : new TableAuditService(loggerFactory.CreateLogger<TableAuditService>(), options);
});

// Read side of the audit trail (GET /api/audit + /api/audit/stats), selected by the same flag as the
// writer above so local (Table/Azurite) and Azure (Cosmos) stay symmetrical — flip AUDIT_BACKEND, no code change.
builder.Services.AddSingleton<IAuditReader>(sp =>
{
    var options = sp.GetRequiredService<SyncOptions>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return options.UseCosmosAudit
        ? new CosmosAuditReader(loggerFactory.CreateLogger<CosmosAuditReader>(), options, sp.GetService<CosmosClient>())
        : new TableAuditReader(loggerFactory.CreateLogger<TableAuditReader>(), options);
});

builder.ConfigureFunctionsWebApplication();
builder.Build().Run();
