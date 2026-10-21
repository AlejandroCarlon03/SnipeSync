namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Query-string parsing shared by every host that serves GET /api/audit and /api/audit/stats, so the
/// Functions endpoints and the local dashboard accept exactly the same parameters.
/// </summary>
public static class AuditQueryParsing
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 1000;

    public const string BackendUnavailableMessage =
        "Audit backend is not configured. Set AUDIT_BACKEND=table (local Azurite) or COSMOS_CONNECTION_STRING (Azure).";

    public static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static int ParseLimit(string? raw) =>
        int.TryParse(raw, out var value) ? Math.Clamp(value, 1, MaxLimit) : DefaultLimit;

    public static bool TryParseBool(string? raw, out bool? value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = null; return true; }
        if (bool.TryParse(raw, out var parsed)) { value = parsed; return true; }
        value = null;
        return false;
    }

    public static bool TryParseDate(string? raw, bool endOfDayIfDateOnly, out DateTimeOffset? value)
    {
        if (string.IsNullOrWhiteSpace(raw)) { value = null; return true; }
        if (!DateTimeOffset.TryParse(raw, out var parsed)) { value = null; return false; }
        // A date-only "to" bound must include the whole day: "to=2026-09-04" parses to midnight, which
        // would exclude every row stamped later that day. Extend it to the last tick of the day so the
        // range stays inclusive. "from" keeps start-of-day, so the day-only case covers the full span.
        if (endOfDayIfDateOnly && !raw.Contains('T') && !raw.Contains(':'))
            parsed = new DateTimeOffset(parsed.Date, parsed.Offset).AddDays(1).AddTicks(-1);
        value = parsed;
        return true;
    }
}
