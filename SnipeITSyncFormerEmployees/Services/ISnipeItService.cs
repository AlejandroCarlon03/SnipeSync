namespace SnipeITSyncFormerEmployees;

public interface ISnipeItService
{
    Task<SnipeItUser?> FindSnipeItUser(string fullName, string email);

    /// <summary>Sets the user's job title to <paramref name="newTitle"/> (used for both offboard and rehire revert).</summary>
    Task<bool> SetSnipeItUserTitle(int userId, string displayName, string currentTitle, string newTitle);

    Task<bool> CreateSnipeItUser(
        string firstName, string lastName, string email, string username, string jobTitle,
        IReadOnlyDictionary<string, object?>? extraFields = null);

    /// <summary>Returns assets currently checked out to the given Snipe-IT user.</summary>
    Task<List<SnipeItAsset>> GetUserAssets(int userId);

    /// <summary>
    /// Checks an asset back in. When <paramref name="statusId"/> is set, the asset's status
    /// label is also updated (e.g. to a deprovisioned/suspended label).
    /// </summary>
    Task<bool> CheckinAsset(int assetId, string assetLabel, int? statusId, string? note);

    /// <summary>PATCHes arbitrary fields (e.g. department/manager/office custom fields) onto a user.</summary>
    Task<bool> SetSnipeItUserFields(int userId, string displayName, IReadOnlyDictionary<string, object?> fields);
}
