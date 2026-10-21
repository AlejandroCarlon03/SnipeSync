using Microsoft.Azure.Functions.Worker;
namespace SnipeITSyncFormerEmployees;

/// <summary>Azure timer wrapper for <see cref="OnboardEmployeeSyncJob"/>.</summary>
public class OnboardEmployeeSync(OnboardEmployeeSyncJob job)
{
    [Function(OnboardEmployeeSyncJob.Name)]
    // Runs at 03:00, an hour after FormerEmployeeSync (02:00), so the two don't hammer Snipe-IT at once.
    public Task Run([TimerTrigger("0 0 3 * * *")] TimerInfo myTimer) => job.RunAsync();
}
