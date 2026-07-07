using System.Net.Http.Headers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Net.Http.Json;
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
public record SnipeItPatchStatus(string Status);

public class FormerEmployeeSync
{
    private readonly ILogger<FormerEmployeeSync> _logger;
    private readonly HttpClient _httpClient;
    private readonly GraphServiceClient _graphServiceClient;

    public FormerEmployeeSync(ILogger<FormerEmployeeSync> logger, HttpClient httpClient,
        GraphServiceClient graphServiceClient)
    {
        var apiKey = Environment.GetEnvironmentVariable("SNIPEIT_API_KEY");

        _logger = logger;
        _httpClient = httpClient;
        _graphServiceClient = graphServiceClient;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    //Set to run every day at 2:00 AM, can change easily in the future if another time is more convenient.
    [Function("FormerEmployeeSync")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer)
    {
        _logger.LogInformation("Starting scheduled Former Employee sync at: {Time}", DateTime.Now);

        var disabledUsers = await GetDisabledEntraUsersAsync();

        if (disabledUsers.Count == 0)
        {
            _logger.LogInformation("No disabled Entra ID accounts found. Nothing to do.");
            return;
        }

        _logger.LogInformation("Found {Count} disabled Entra ID accounts.", disabledUsers.Count);

        foreach (var user in disabledUsers)
        {
            if (user.DisplayName is null || user.Mail is null)
            {
                _logger.LogWarning("Skipping user {Id}: missing DisplayName or Mail in Entra ID.", user.Id);
                continue;
            }

            var snipeUser = await FindSnipeItUser(user.DisplayName, user.Mail);
            if (snipeUser is null)
            {
                _logger.LogWarning("No Snipe-IT match found for '{DisplayName}'.", user.DisplayName);
                continue;
            }

            if (snipeUser.JobTitle == "Former Employee")
            {
                _logger.LogInformation("{DisplayName} is already marked as Former Employee, skipping.", user.DisplayName);
                continue;
            }

            var success = await SetSnipeItUserTitle(snipeUser.Id, user.DisplayName, snipeUser.JobTitle);

            if (success)
            {
                _logger.LogInformation("{DisplayName} successfully marked as Former Employee in Snipe-IT.", user.DisplayName);
            }
            else
            {
                _logger.LogWarning("Failed to update {DisplayName} in Snipe-IT.", user.DisplayName);
            }
        }

        _logger.LogInformation("Former Employee sync completed.");
    }

    private async Task<List<User>> GetDisabledEntraUsersAsync()
    {
        try
        {
            var result = await _graphServiceClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = "accountEnabled eq false";
                requestConfiguration.QueryParameters.Select = ["id", "displayName", "accountEnabled", "mail"];
                requestConfiguration.QueryParameters.Count = true;
                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
            });

            return result?.Value?.ToList() ?? [];
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to query disabled users from Entra: {Error}", e.Message);
            return [];
        }
    }

    private async Task<SnipeItUser?> FindSnipeItUser(string fullName, string email)
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

    private async Task<bool> SetSnipeItUserTitle(int userId, string displayName, string currentTitle)
    {
        var baseUrl = Environment.GetEnvironmentVariable("SNIPEIT_URL");
        var uri = $"{baseUrl}/api/v1/users/{userId}";
        try
        {
            var response = await _httpClient.PatchAsJsonAsync(uri, new { jobtitle = "Former Employee" });
            var status = await response.Content.ReadFromJsonAsync<SnipeItPatchStatus>();
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
}