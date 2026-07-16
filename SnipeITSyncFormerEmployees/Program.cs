using Azure.Identity;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using SnipeITSyncFormerEmployees;

var builder = FunctionsApplication.CreateBuilder(args);

// Config-driven options (feature 7) — read once from environment / app settings.
builder.Services.AddSingleton<SyncOptions>();

builder.Services.AddHttpClient<ISnipeItService, SnipeItService>();
builder.Services.AddHttpClient<INotificationService, TeamsNotificationService>();

builder.Services.AddSingleton<EntraUserService>();
builder.Services.AddSingleton<IAuditService, TableAuditService>();
builder.Services.AddSingleton<IReconciliationQueue, StorageReconciliationQueue>();

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
