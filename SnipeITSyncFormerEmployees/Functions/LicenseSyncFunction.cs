using Microsoft.Azure.Functions.Worker;

namespace SnipeITSyncFormerEmployees;

/// <summary>Azure timer wrapper for <see cref="LicenseSyncJob"/>.</summary>
public class LicenseSyncFunction(LicenseSyncJob job)
{
    [Function(LicenseSyncJob.Name)]
    // Runs at 04:00, after offboard (02:00) and onboard (03:00), so the three don't hit Snipe-IT at once.
    public Task Run([TimerTrigger("0 0 4 * * *")] TimerInfo timer) => job.RunAsync();
}
