using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

public class SnipeItService : ISnipeItService
{
    private readonly ILogger<SnipeItService> _logger;
    private readonly HttpClient _httpClient;
    private readonly SyncOptions _options;

    public SnipeItService(HttpClient httpClient, ILogger<SnipeItService> logger, SyncOptions options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options;
        var apiKey = Environment.GetEnvironmentVariable("SNIPEIT_API_KEY");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static string BaseUrl => Environment.GetEnvironmentVariable("SNIPEIT_URL") ?? string.Empty;

    public async Task<SnipeItUser?> FindSnipeItUser(string fullName, string email)
    {
        if (!string.IsNullOrEmpty(email))
        {
            var encodedEmail = Uri.EscapeDataString(email);
            var uri = $"{BaseUrl}/api/v1/users?search={encodedEmail}&limit=5";
            try
            {
                var response = await _httpClient.GetFromJsonAsync<SnipeItSearchResponse>(uri);
                var rows = response?.Rows ?? [];
                var emailMatch = rows.FirstOrDefault(x => x.Email.Equals(email));

                if (emailMatch is null)
                {
                    _logger.LogInformation("No Snipe-IT match found for email {Email}", email);
                }

                return emailMatch;
            }
            catch (Exception e)
            {
                // A failed lookup (e.g. throttling that survived the retry policy) is NOT a definitive
                // miss — returning null here would let a departed employee be treated as "not in Snipe-IT"
                // and, on the onboarding side, cause a duplicate user to be created. Surface it instead.
                _logger.LogWarning("Failed to look up Snipe-IT user by email {Email}: {Error}", email, e.Message);
                throw;
            }
        }
        else
        {
            var encodedName = Uri.EscapeDataString(fullName);
            var uri = $"{BaseUrl}/api/v1/users?search={encodedName}&limit=10";
            try
            {
                var response = await _httpClient.GetFromJsonAsync<SnipeItSearchResponse>(uri);
                var rows = response?.Rows ?? [];
                var nameMatch = rows.FirstOrDefault(x => $"{x.FirstName} {x.LastName}".Equals(fullName));
                if (nameMatch is null)
                {
                    _logger.LogInformation("No Snipe-IT match found for full name {Name}", fullName);
                }
                return nameMatch;
            }
            catch (Exception e)
            {
                // See the email branch above: a lookup failure is not a "not found" — surface it.
                _logger.LogWarning("Failed to look up Snipe-IT user by name {Name}: {Error}", fullName, e.Message);
                throw;
            }
        }
    }

    public async Task<bool> SetSnipeItUserTitle(int userId, string displayName, string currentTitle, string newTitle)
    {
        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would update {DisplayName} (ID: {UserId}) title '{Current}' -> '{New}'",
                displayName, userId, currentTitle, newTitle);
            return true;
        }

        var uri = $"{BaseUrl}/api/v1/users/{userId}";
        try
        {
            var response = await _httpClient.PatchAsJsonAsync(uri, new { jobtitle = newTitle });
            var status = await response.Content.ReadFromJsonAsync<SnipeItStatus>();
            if (status is null)
            {
                _logger.LogWarning("Failed to read Snipe-IT status when updating {DisplayName}.", displayName);
                return false;
            }
            if (response.IsSuccessStatusCode && status.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[OK] Updated {DisplayName} (ID: {UserId}) '{Current}' -> '{New}'",
                    displayName, userId, currentTitle, newTitle);
                return true;
            }

            _logger.LogWarning("[WARNING] Unexpected response updating {DisplayName}: {Messages}",
                displayName, status.Messages);
            return false;
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to patch user {DisplayName}: {Error}", displayName, e.Message);
        }

        return false;
    }

    public async Task<bool> CreateSnipeItUser(
        string firstName, string lastName, string email, string username, string jobTitle,
        IReadOnlyDictionary<string, object?>? extraFields = null)
    {
        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would create Snipe-IT user {First} {Last} ({Email}), title '{Title}'",
                firstName, lastName, email, jobTitle);
            return true;
        }

        var tempPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var uri = $"{BaseUrl}/api/v1/users";

        var body = new Dictionary<string, object?>
        {
            ["first_name"] = firstName,
            ["last_name"] = lastName,
            ["email"] = email,
            ["username"] = username,
            ["jobtitle"] = jobTitle,
            ["password"] = tempPassword,
            ["password_confirmation"] = tempPassword,
            ["ldap_import"] = 1
        };
        if (extraFields is not null)
        {
            foreach (var (key, value) in extraFields)
            {
                if (value is not null) body[key] = value;
            }
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(uri, body);
            var status = await response.Content.ReadFromJsonAsync<SnipeItStatus>();
            if (status is null)
            {
                _logger.LogWarning("Failed to read Snipe-IT status when creating {First} {Last}.", firstName, lastName);
                return false;
            }

            if (response.IsSuccessStatusCode && status.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[OK] {First} {Last} has been added to Snipe-IT (title '{Title}').",
                    firstName, lastName, jobTitle);
                return true;
            }

            _logger.LogWarning("[WARNING] Unexpected response creating {First} {Last}: {Messages}",
                firstName, lastName, status.Messages);
            return false;
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to create Snipe-IT user {First} {Last}: {Error}", firstName, lastName, e.Message);
        }

        return false;
    }

    public async Task<List<SnipeItAsset>> GetUserAssets(int userId)
    {
        var uri = $"{BaseUrl}/api/v1/users/{userId}/assets?limit=500";
        try
        {
            var response = await _httpClient.GetFromJsonAsync<SnipeItAssetsResponse>(uri);
            return response?.Rows ?? [];
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to fetch assets for Snipe-IT user {UserId}: {Error}", userId, e.Message);
            return [];
        }
    }

    public async Task<bool> CheckinAsset(int assetId, string assetLabel, int? statusId, string? note)
    {
        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would check in asset {Asset} (ID: {AssetId}){Status}",
                assetLabel, assetId, statusId is null ? "" : $" and set status_id={statusId}");
            return true;
        }

        var uri = $"{BaseUrl}/api/v1/hardware/{assetId}/checkin";
        var body = new Dictionary<string, object?> { ["note"] = note };
        if (statusId is not null) body["status_id"] = statusId;

        try
        {
            var response = await _httpClient.PostAsJsonAsync(uri, body);
            var status = await response.Content.ReadFromJsonAsync<SnipeItStatus>();
            if (status is not null
                && response.IsSuccessStatusCode
                && status.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[OK] Checked in asset {Asset} (ID: {AssetId}).", assetLabel, assetId);
                return true;
            }

            _logger.LogWarning("[WARNING] Failed to check in asset {Asset} (ID: {AssetId}): {Messages}",
                assetLabel, assetId, status?.Messages);
            return false;
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to check in asset {Asset} (ID: {AssetId}): {Error}", assetLabel, assetId, e.Message);
        }

        return false;
    }

    public async Task<bool> SetSnipeItUserFields(int userId, string displayName, IReadOnlyDictionary<string, object?> fields)
    {
        var payload = fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value);
        if (payload.Count == 0) return true;

        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would update fields for {DisplayName} (ID: {UserId}): {Fields}",
                displayName, userId, string.Join(", ", payload.Keys));
            return true;
        }

        var uri = $"{BaseUrl}/api/v1/users/{userId}";
        try
        {
            var response = await _httpClient.PatchAsJsonAsync(uri, payload);
            var status = await response.Content.ReadFromJsonAsync<SnipeItStatus>();
            if (status is not null
                && response.IsSuccessStatusCode
                && status.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[OK] Updated fields for {DisplayName} (ID: {UserId}): {Fields}",
                    displayName, userId, string.Join(", ", payload.Keys));
                return true;
            }

            _logger.LogWarning("[WARNING] Failed to update fields for {DisplayName}: {Messages}",
                displayName, status?.Messages);
            return false;
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to update fields for {DisplayName}: {Error}", displayName, e.Message);
        }

        return false;
    }

    public async Task<List<SnipeItLicenseSeat>> GetUserLicenseSeats(int userId)
    {
        // Step 1: which licenses is the user assigned to?
        var licensesUri = $"{BaseUrl}/api/v1/users/{userId}/licenses?limit=500";
        List<SnipeItUserLicense> licenses;
        try
        {
            var response = await _httpClient.GetFromJsonAsync<SnipeItUserLicensesResponse>(licensesUri);
            licenses = response?.Rows ?? [];
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to fetch licenses for Snipe-IT user {UserId}: {Error}", userId, e.Message);
            return [];
        }

        // Step 2: for each license, find the seat(s) assigned to this user (check-in is per seat).
        var seats = new List<SnipeItLicenseSeat>();
        foreach (var license in licenses)
        {
            var seatsUri = $"{BaseUrl}/api/v1/licenses/{license.Id}/seats?limit=500";
            try
            {
                var response = await _httpClient.GetFromJsonAsync<SnipeItLicenseSeatsResponse>(seatsUri);
                var userSeats = (response?.Rows ?? [])
                    .Where(s => s.AssignedTo?.Id == userId)
                    .Select(s => new SnipeItLicenseSeat(s.Id, license.Id, license.Name));
                seats.AddRange(userSeats);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Failed to fetch seats for license {LicenseId}: {Error}", license.Id, e.Message);
            }
        }

        return seats;
    }

    public async Task<bool> CheckinLicenseSeat(SnipeItLicenseSeat seat, string? note)
    {
        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would check in {Seat}", seat.DisplayLabel);
            return true;
        }

        // Snipe-IT checks in a license seat by clearing its assignment.
        // NOTE: verify against your Snipe-IT version — some expose PATCH .../seats/{id}.
        var uri = $"{BaseUrl}/api/v1/licenses/{seat.LicenseId}/seats/{seat.SeatId}";
        try
        {
            var response = await _httpClient.PatchAsJsonAsync(uri, new { assigned_to = (int?)null, notes = note });
            var status = await response.Content.ReadFromJsonAsync<SnipeItStatus>();
            if (status is not null
                && response.IsSuccessStatusCode
                && status.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[OK] Reclaimed {Seat}.", seat.DisplayLabel);
                return true;
            }

            _logger.LogWarning("[WARNING] Failed to reclaim {Seat}: {Messages}", seat.DisplayLabel, status?.Messages);
            return false;
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to reclaim {Seat}: {Error}", seat.DisplayLabel, e.Message);
        }

        return false;
    }

    public async Task<List<SnipeItAccessory>> GetUserAccessories(int userId)
    {
        var uri = $"{BaseUrl}/api/v1/users/{userId}/accessories?limit=500";
        try
        {
            var response = await _httpClient.GetFromJsonAsync<SnipeItAccessoriesResponse>(uri);
            return response?.Rows ?? [];
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to fetch accessories for Snipe-IT user {UserId}: {Error}", userId, e.Message);
            return [];
        }
    }

    public async Task<bool> CheckinAccessory(SnipeItAccessory accessory, string? note)
    {
        if (accessory.PivotId is null)
        {
            _logger.LogWarning("Cannot check in accessory {Accessory}: no assignment (pivot) id returned by Snipe-IT.",
                accessory.DisplayLabel);
            return false;
        }

        if (_options.DryRun)
        {
            _logger.LogInformation("[DRY-RUN] Would check in accessory {Accessory}", accessory.DisplayLabel);
            return true;
        }

        // Snipe-IT accessory check-in targets the assignment pivot id.
        // NOTE: verify against your Snipe-IT version's accessory check-in route.
        var uri = $"{BaseUrl}/api/v1/accessories/{accessory.Id}/checkin/{accessory.PivotId}";
        try
        {
            var response = await _httpClient.PostAsJsonAsync(uri, new { note });
            var status = await response.Content.ReadFromJsonAsync<SnipeItStatus>();
            if (status is not null
                && response.IsSuccessStatusCode
                && status.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[OK] Reclaimed accessory {Accessory}.", accessory.DisplayLabel);
                return true;
            }

            _logger.LogWarning("[WARNING] Failed to reclaim accessory {Accessory}: {Messages}",
                accessory.DisplayLabel, status?.Messages);
            return false;
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to reclaim accessory {Accessory}: {Error}", accessory.DisplayLabel, e.Message);
        }

        return false;
    }
}
