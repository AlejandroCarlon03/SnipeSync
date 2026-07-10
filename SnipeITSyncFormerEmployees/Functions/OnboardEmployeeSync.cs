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
        _logger.LogInformation("Starting scheduled Former Employee sync at: {Time}", DateTime.Now);
        
    }

    private async Task<List<User>> GetRecentlyCreatedEntraUsersAsync()
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-7);
            var result = await _graphServiceClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"createdDateTime ge {cutoffDate:yyyy-MM-ddTHH:mm:ssZ}";
                requestConfiguration.QueryParameters.Select = ["id", "displayName", "mail"];
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
    
    

    //Some function to get recently enabled Entra users

    //Some function to find said user

    //Some function to set and sync their information 
    //Post their info to Snipe-IT
}
