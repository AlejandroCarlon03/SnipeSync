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

// --- License models (Part B) ----------------------------------------------------
// GET /api/v1/users/{id}/licenses returns the licenses assigned to a user, but a
// check-in is per *seat*, so we resolve each license's seats and match the user.
public record SnipeItUserLicense(int Id, string? Name);
public record SnipeItUserLicensesResponse(int Total, List<SnipeItUserLicense> Rows);

// GET /api/v1/licenses/{id}/seats — the assignee comes back under "assigned_user" (NOT "assigned_to",
// which is what the write/checkout side uses). Reading the wrong key makes every seat look unassigned,
// so GetFirstFreeSeatId hands out the same seat repeatedly and GetUserLicenseSeats finds nothing.
public record SnipeItLicenseSeatRaw(
    int Id,
    [property: JsonPropertyName("assigned_user")] SnipeItNamedRef? AssignedTo
);
public record SnipeItLicenseSeatsResponse(int Total, List<SnipeItLicenseSeatRaw> Rows);

// GET /api/v1/licenses?search=... — used to resolve an Entra SKU's target license (checkout side).
public record SnipeItLicenseRef(
    int Id,
    string? Name,
    [property: JsonPropertyName("free_seats_count")] int FreeSeatsCount
);
public record SnipeItLicensesResponse(int Total, List<SnipeItLicenseRef> Rows);

// POST /api/v1/licenses returns { status, messages, payload:{ the created license } }.
public record SnipeItCreateLicenseResponse(
    string Status,
    SnipeItLicenseRef? Payload,
    [property: JsonPropertyName("messages")] object? Messages = null
);

/// <summary>A resolved seat assigned to a specific user, ready to check in.</summary>
public record SnipeItLicenseSeat(int SeatId, int LicenseId, string? LicenseName)
{
    public string DisplayLabel => $"{LicenseName ?? $"license #{LicenseId}"} (seat #{SeatId})";
}

// --- Accessory models (Part B) --------------------------------------------------
// GET /api/v1/users/{id}/accessories. Check-in uses the pivot id of the assignment.
public record SnipeItAccessory(
    int Id,
    string? Name,
    [property: JsonPropertyName("pivot_id")] int? PivotId
)
{
    public string DisplayLabel => !string.IsNullOrWhiteSpace(Name) ? Name! : $"accessory #{Id}";
}
public record SnipeItAccessoriesResponse(int Total, List<SnipeItAccessory> Rows);
