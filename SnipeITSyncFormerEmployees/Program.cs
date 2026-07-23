using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using SnipeITSyncFormerEmployees;

var builder = FunctionsApplication.CreateBuilder(args);

// Config-driven options (feature 7) — read once from environment / app settings.
builder.Services.AddSingleton<SyncOptions>();

// Snipe-IT rate-limits hard (429); retry with backoff so throttling isn't misread as "not found".
builder.Services.AddHttpClient<ISnipeItService, SnipeItService>()
    .AddPolicyHandler((sp, _) =>
        SnipeItRetryPolicy.Create(sp.GetRequiredService<ILoggerFactory>().CreateLogger("SnipeItRetry")));
builder.Services.AddHttpClient<INotificationService, TeamsNotificationService>();

builder.Services.AddSingleton<EntraUserService>();
builder.Services.AddSingleton<IOffboardingService, OffboardingService>();
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

builder.Services.AddSingleton(sp =>
{
    var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
    var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
    var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");

    if (tenantId is null || clientId is null || clientSecret is null)
        throw new InvalidOperationException("Missing one or more Azure AD environment variables.");

    var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
    return new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
});

builder.ConfigureFunctionsWebApplication();
builder.Build().Run();
