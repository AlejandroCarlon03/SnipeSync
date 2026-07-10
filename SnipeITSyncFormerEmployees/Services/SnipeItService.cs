using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

public class SnipeItService : ISnipeItService
{
    private readonly ILogger<SnipeItService> _logger;
    private readonly HttpClient _httpClient;

    public SnipeItService(HttpClient httpClient, ILogger<SnipeItService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        var apiKey = Environment.GetEnvironmentVariable("SNIPEIT_API_KEY");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<SnipeItUser?> FindSnipeItUser(string fullName, string email)
    {
        var baseUrl = Environment.GetEnvironmentVariable("SNIPEIT_URL");
        if (!string.IsNullOrEmpty(email))
        {
            var encodedEmail = Uri.EscapeDataString(email);
            var uri = $"{baseUrl}/api/v1/users?search={encodedEmail}&limit=5";
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
                _logger.LogWarning("Failed to find Snipe-IT user by email {Email}: {Error}", email, e.Message);
                return null;
            }
        }
        else
        {
            var encodedName = Uri.EscapeDataString(fullName);
            var uri = $"{baseUrl}/api/v1/users?search={encodedName}&limit=10";
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
                _logger.LogWarning("Failed to find Snipe-IT user by name {Name}: {Error}", fullName, e.Message);
                return null;
            }
        }
    }
    
    public async Task<bool> SetSnipeItUserTitle(int userId, string displayName, string currentTitle)
    {
        var baseUrl = Environment.GetEnvironmentVariable("SNIPEIT_URL");
        var uri = $"{baseUrl}/api/v1/users/{userId}";
        try
        {
            var response = await _httpClient.PatchAsJsonAsync(uri, new { jobtitle = "Former Employee" });
            var status = await response.Content.ReadFromJsonAsync<SnipeItStatus>();
            if (status is null)
            {
                _logger.LogWarning("Failed to find Snipe-IT Status.");
                return false;
            }
            if (response.IsSuccessStatusCode && status.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[OK] Updated {DisplayName} (ID: {UserId}) -> 'Former Employee'", displayName, userId);
                _logger.LogInformation("Current title: {CurrentTitle}", currentTitle);
                return true;
            }
            else
            {
                _logger.LogWarning("[WARNING] Unexpected response for {DisplayName}", displayName);
                return false;
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to patch user {DisplayName}: {Error}", displayName, e.Message);
        }

        return false;
    }

    public async Task<bool> CreateSnipeItUser(string firstName, string lastName, string email, string username, string jobTitle)
    {
        var tempPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var baseUrl = Environment.GetEnvironmentVariable("SNIPEIT_URL");
        var uri = $"{baseUrl}/api/v1/users";
        var newUserBody = new
        {
            first_name = firstName,
            last_name = lastName,
            email,
            username,
            jobtitle = jobTitle,
            password = tempPassword,
            password_confirmation = tempPassword,
            ldap_import = 1
        };
        try
        {
            var response = await _httpClient.PostAsJsonAsync(uri, newUserBody);
            var status = await response.Content.ReadFromJsonAsync<SnipeItStatus>();
            if (status is null)
            {
                _logger.LogWarning("Failed to find Snipe-IT Status.");
                return false;
            }

            if (response.IsSuccessStatusCode && status.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[OK], {FirstName} {LastName} has been added to Snipe-IT.", firstName, lastName);
                _logger.LogInformation("Current title: {CurrentTitle}", jobTitle);
                return true;
            }
            else
            {
                _logger.LogWarning("[WARNING] Unexpected response for {FirstName}", firstName);
                return false;
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to post : {Error}", e.Message);
        }

        return false;
    }
}