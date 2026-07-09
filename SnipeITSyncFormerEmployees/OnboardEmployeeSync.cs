using System.Net.Http.Headers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
namespace SnipeITSyncFormerEmployees;

public class OnboardEmployeeSync
{
    private readonly ILogger<OnboardEmployeeSync> _logger;
    private readonly HttpClient _httpClient;
    private readonly GraphServiceClient _graphServiceClient;

    public OnboardEmployeeSync(ILogger<OnboardEmployeeSync> logger, HttpClient httpClient,
        GraphServiceClient graphServiceClient)
    {
        var apiKey = Environment.GetEnvironmentVariable("SNIPEIT_API_KEY");

        _logger = logger;
        _httpClient = httpClient;
        _graphServiceClient = graphServiceClient;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }


    [Function("OnboardEmployeeSync")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo myTimer)
    {
        
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
    
    //Some function to get recently enabled Entra users
    
    //Some function to find said user
    
    //Some function to set and sync their information 
    //Post their info to Snipe-IT
}