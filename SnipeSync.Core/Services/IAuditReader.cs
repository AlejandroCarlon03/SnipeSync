namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Read side of the audit trail, backend-agnostic so the HTTP endpoints (and the dashboard that
/// consumes them) behave identically whether history lives in Cosmos DB (Azure) or Azure Table
/// Storage (local Azurite). The backend is chosen once in DI from <see cref="SyncOptions.UseCosmosAudit"/>,
/// mirroring how the write side (<see cref="IAuditService"/>) is already selected — so flipping
/// between local and Azure is a config change, never a code change.
/// </summary>
public interface IAuditReader
{
    /// <summary>Whether a usable backend is configured. When false the endpoints return 503.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns the most recent matching audit records, newest first, capped at the filter limit.</summary>
    Task<IReadOnlyList<AuditRecord>> QueryAsync(AuditQueryFilter filter, CancellationToken ct);

    /// <summary>Returns grouped counts (by action, function, month) plus the total, over the filter.</summary>
    Task<AuditStatsResult> StatsAsync(AuditStatsFilter filter, CancellationToken ct);
}

/// <summary>Optional filters for <see cref="IAuditReader.QueryAsync"/>; nulls mean "no constraint".</summary>
public record AuditQueryFilter(
    string? User, string? Action, string? Function,
    DateTimeOffset? From, DateTimeOffset? To, int Limit);

/// <summary>Optional filters for <see cref="IAuditReader.StatsAsync"/>; nulls mean "no constraint".</summary>
public record AuditStatsFilter(
    string? Function, bool? DryRun, DateTimeOffset? From, DateTimeOffset? To);

/// <summary>Grouped rollups returned by <see cref="IAuditReader.StatsAsync"/>.</summary>
public record AuditStatsResult(
    int Total,
    IReadOnlyList<CountBucket> ByAction,
    IReadOnlyList<CountBucket> ByFunction,
    IReadOnlyList<CountBucket> ByMonth);

/// <summary>Thrown by a reader when the backend query fails, so the HTTP layer can map it to a 500
/// without knowing whether the source was Cosmos or Table Storage.</summary>
public sealed class AuditReaderException(string message, Exception inner) : Exception(message, inner);
