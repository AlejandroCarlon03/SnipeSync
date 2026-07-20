namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Accumulates the outcome of a single sync run so it can be surfaced in a
/// notification digest (feature 4) instead of being scattered across logs.
/// </summary>
public class SyncRunSummary(string functionName)
{
    public string FunctionName { get; } = functionName;
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public bool DryRun { get; set; }

    public int Processed { get; set; }
    public int Offboarded { get; set; }
    public int Onboarded { get; set; }
    public int Reactivated { get; set; }
    public int AssetsCheckedIn { get; set; }
    public int LicensesReclaimed { get; set; }
    public int AccessoriesReclaimed { get; set; }
    public int AlreadyCurrent { get; set; }
    public int Failed { get; set; }

    /// <summary>Users seen in Entra with no Snipe-IT match (feature 8 candidates).</summary>
    public List<string> Unmatched { get; } = [];

    /// <summary>Assets that could not be auto-checked-in and need manual reclaim.</summary>
    public List<string> AssetsNeedingAttention { get; } = [];

    public int Skipped => Unmatched.Count;
    public bool HasActivity =>
        Offboarded + Onboarded + Reactivated + AssetsCheckedIn
        + LicensesReclaimed + AccessoriesReclaimed + Failed + Skipped > 0;
}

/// <summary>An Entra user we could not match to Snipe-IT, queued for a second-pass reconciliation.</summary>
public record UnmatchedUser(
    string? EntraId,
    string DisplayName,
    string? Email,
    string Reason,
    string SourceFunction,
    DateTimeOffset SeenAt
);
