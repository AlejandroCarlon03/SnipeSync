using System.Text.Json.Serialization;

namespace SnipeITSyncFormerEmployees;

/// <summary>One <c>GROUP BY</c> bucket: the grouped value and its row count.</summary>
public record CountBucket
{
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("count")] public int Count { get; init; }
}

/// <summary>Aggregate response for GET /api/audit/stats, served by both the Functions and dashboard hosts.</summary>
public record AuditStatsResponse
{
    [JsonPropertyName("from")] public string? From { get; init; }
    [JsonPropertyName("to")] public string? To { get; init; }
    [JsonPropertyName("function")] public string? Function { get; init; }
    [JsonPropertyName("dryRun")] public bool? DryRun { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("byAction")] public required List<CountBucket> ByAction { get; init; }
    [JsonPropertyName("byFunction")] public required List<CountBucket> ByFunction { get; init; }
    [JsonPropertyName("byMonth")] public required List<CountBucket> ByMonth { get; init; }
}
