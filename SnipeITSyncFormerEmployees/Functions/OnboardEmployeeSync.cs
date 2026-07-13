using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
namespace SnipeITSyncFormerEmployees;

public class OnboardEmployeeSync
{
    private readonly ILogger<OnboardEmployeeSync> _logger;
    private readonly GraphServiceClient _graphServiceClient;
    private readonly ISnipeItService _snipeItService;
    
    public OnboardEmployeeSync(ILogger<OnboardEmployeeSync> logger, ISnipeItService snipeItService,
        GraphServiceClient graphServiceClient)
    {
        _logger = logger;
        _snipeItService = snipeItService;
        _graphServiceClient = graphServiceClient;
    }
    
    [Function("OnboardEmployeeSync")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo myTimer)
    {
        _logger.LogInformation("Starting scheduled sync for new employees at: {Time}", DateTime.Now);

        var recentlyCreatedUsers = await GetRecentlyCreatedEntraUsersAsync();

        if (recentlyCreatedUsers.Count == 0)
        {
            _logger.LogInformation("No recently created Entra ID accounts found. Nothing to do.");
            return;
        }

        _logger.LogInformation("Found {Count} recently created Entra ID accounts.", recentlyCreatedUsers.Count);

        foreach (var user in recentlyCreatedUsers)
        {
            if (user.DisplayName is null || user.Mail is null || 
                user.GivenName is null || user.Surname is null || user.MailNickname is null)
            {
                _logger.LogWarning("Skipping user {Id}: missing required fields in Entra ID.", user.Id);
                continue;
            }

            var snipeUser = await _snipeItService.FindSnipeItUser(user.DisplayName, user.Mail);

            if (snipeUser is not null)
            {
                _logger.LogInformation("{DisplayName} already exists in Snipe-IT, skipping.", user.DisplayName);
                continue;
            }

            var jobTitle = user.JobTitle ?? "New Employee";

            var success = await _snipeItService.CreateSnipeItUser(
                user.GivenName, user.Surname, user.Mail, user.MailNickname, jobTitle);

            if (success)
            {
                _logger.LogInformation("{DisplayName} successfully added to Snipe-IT.", user.DisplayName);
            }
            else
            {
                _logger.LogWarning("Failed to add {DisplayName} to Snipe-IT.", user.DisplayName);
            }
        }

        _logger.LogInformation("Onboarding sync completed.");
    }

    private async Task<List<User>> GetRecentlyCreatedEntraUsersAsync()
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-7);
            var result = await _graphServiceClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"createdDateTime ge {cutoffDate:yyyy-MM-ddTHH:mm:ssZ}";
                requestConfiguration.QueryParameters.Select = 
                    ["id", "displayName", "mail", "givenName", "surname", "mailNickname", "jobTitle"];
                requestConfiguration.QueryParameters.Count = true;
                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
            });
            return result?.Value?.ToList() ?? [];
        }
        catch (Exception e)
        {
            _logger.LogWarning("Failed to query users from Entra: {Error}", e.Message);
            return [];
        }
    }
}