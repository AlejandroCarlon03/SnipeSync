using System.Text.Json.Serialization;
namespace SnipeITSyncFormerEmployees;

public record SnipeItUser(
    int Id,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("jobtitle")] string JobTitle,
    string Email
);
public record SnipeItSearchResponse(int Total, List<SnipeItUser> Rows);

// Snipe-IT write endpoints (create/update/checkin) return { status, messages, payload }.
// We only need the status string; messages is captured for logging on failure.
public record SnipeItStatus(
    string Status,
    [property: JsonPropertyName("messages")] object? Messages = null
);

// --- Asset models (feature 1) ---------------------------------------------------
public record SnipeItAsset(
    int Id,
    string? Name,
    [property: JsonPropertyName("asset_tag")] string? AssetTag,
    string? Serial,
    [property: JsonPropertyName("status_label")] SnipeItStatusLabel? StatusLabel,
    SnipeItNamedRef? Model
)
{
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(AssetTag) ? AssetTag!
        : !string.IsNullOrWhiteSpace(Name) ? Name!
        : $"asset #{Id}";
}

public record SnipeItStatusLabel(
    int Id,
    string? Name,
    [property: JsonPropertyName("status_type")] string? StatusType
);

public record SnipeItNamedRef(int Id, string? Name);

public record SnipeItAssetsResponse(int Total, List<SnipeItAsset> Rows);
