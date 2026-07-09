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
    private readonly ISnipeItService _snipeItService;
    private readonly GraphServiceClient _graphServiceClient;

    public FormerEmployeeSync(ILogger<FormerEmployeeSync> logger, ISnipeItService snipeItService,
        GraphServiceClient graphServiceClient)
    {
        _logger = logger;
        _graphServiceClient = graphServiceClient;
        _snipeItService = snipeItService;

    }

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

            var snipeUser = await _snipeItService.FindSnipeItUser(user.DisplayName, user.Mail);            
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

            var success = await _snipeItService.SetSnipeItUserTitle(snipeUser.Id, user.DisplayName, snipeUser.JobTitle);

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
    
}