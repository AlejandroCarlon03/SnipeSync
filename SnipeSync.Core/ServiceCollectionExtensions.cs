using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace SnipeITSyncFormerEmployees;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the sync engine: options, the Snipe-IT / Graph / Teams clients, offboarding, and the four
    /// jobs. The host still registers <see cref="IAuditService"/> and <see cref="IReconciliationQueue"/>,
    /// because where audit rows and unmatched users go is the one thing Azure and local differ on.
    /// </summary>
    /// <param name="options">A pre-built options instance (e.g. with a dry-run override); null reads the environment.</param>
    public static IServiceCollection AddSnipeSyncCore(this IServiceCollection services, SyncOptions? options = null)
    {
        // Config-driven options (feature 7) — read once from environment / app settings.
        if (options is null) services.AddSingleton<SyncOptions>();
        else services.AddSingleton(options);

        // Snipe-IT rate-limits hard (429); retry with backoff so throttling isn't misread as "not found".
        services.AddHttpClient<ISnipeItService, SnipeItService>()
            .AddPolicyHandler((sp, _) =>
                SnipeItRetryPolicy.Create(sp.GetRequiredService<ILoggerFactory>().CreateLogger("SnipeItRetry")));
        services.AddHttpClient<INotificationService, TeamsNotificationService>();

        services.AddSingleton<EntraUserService>();
        services.AddSingleton<IOffboardingService, OffboardingService>();

        services.AddSingleton(_ =>
        {
            var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
            var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");

            if (tenantId is null || clientId is null || clientSecret is null)
                throw new InvalidOperationException("Missing one or more Azure AD environment variables.");

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            return new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
        });

        services.AddTransient<FormerEmployeeSyncJob>();
        services.AddTransient<OnboardEmployeeSyncJob>();
        services.AddTransient<ReconciliationJob>();
        services.AddTransient<LicenseSyncJob>();

        return services;
    }
}
