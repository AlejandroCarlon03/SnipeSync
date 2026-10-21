using Microsoft.Azure.Functions.Worker;
namespace SnipeITSyncFormerEmployees;

/// <summary>Azure timer wrapper for <see cref="FormerEmployeeSyncJob"/>.</summary>
public class FormerEmployeeSync(FormerEmployeeSyncJob job)
{
    [Function(FormerEmployeeSyncJob.Name)]
    public Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer) => job.RunAsync();
}
