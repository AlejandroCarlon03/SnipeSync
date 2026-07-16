namespace SnipeITSyncFormerEmployees;

/// <summary>Persists a durable record of each sync decision (feature 5).</summary>
public interface IAuditService
{
    Task RecordAsync(
        string function,
        string user,
        string action,
        string? oldValue = null,
        string? newValue = null,
        string? detail = null);
}
