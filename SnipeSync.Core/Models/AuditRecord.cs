using System.Text.Json.Serialization;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// One audit document as stored in Cosmos DB (feature 5, Cosmos backend). Shared by
/// <see cref="CosmosAuditService"/> (writes) and <see cref="AuditQueryFunction"/> (reads) so the
/// stored shape and the query-deserialization shape can never drift apart.
///
/// The Table Storage backend uses an untyped <c>TableEntity</c>; Cosmos + JSON want a real type.
/// </summary>
public record AuditRecord
{
    /// <summary>Cosmos requires a lowercase <c>id</c>; a compact GUID keeps each decision unique.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Partition key: a synthetic month bucket ("yyyy-MM"). Bounds partition size for this
    /// time-series data and lets date-range queries target a small set of partitions.
    /// </summary>
    [JsonPropertyName("ym")]
    public required string Ym { get; init; }

    [JsonPropertyName("function")] public required string Function { get; init; }
    [JsonPropertyName("user")] public required string User { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
    [JsonPropertyName("oldValue")] public string? OldValue { get; init; }
    [JsonPropertyName("newValue")] public string? NewValue { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    [JsonPropertyName("dryRun")] public bool DryRun { get; init; }
    [JsonPropertyName("timestampUtc")] public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Builds a record for <paramref name="now"/>, deriving the month-bucket partition key.</summary>
    public static AuditRecord Create(
        string function, string user, string action,
        string? oldValue, string? newValue, string? detail, bool dryRun, DateTimeOffset now) =>
        new()
        {
            Ym = now.ToString("yyyy-MM"),
            Function = function,
            User = user,
            Action = action,
            OldValue = oldValue,
            NewValue = newValue,
            Detail = detail,
            DryRun = dryRun,
            TimestampUtc = now
        };
}
