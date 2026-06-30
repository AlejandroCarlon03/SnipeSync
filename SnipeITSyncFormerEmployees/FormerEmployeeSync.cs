using System.Net.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;

namespace SnipeITSyncFormerEmployees;

public class FormerEmployeeSync
{
    private readonly ILogger<FormerEmployeeSync> _logger;
    private readonly HttpClient _httpClient;
    private readonly GraphServiceClient _graphServiceClient;

    public FormerEmployeeSync(ILogger<FormerEmployeeSync> logger, HttpClient httpClient,
        GraphServiceClient graphServiceClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _graphServiceClient = graphServiceClient;
    }
    
    [Function("FormerEmployeeSync")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to the new Azure Functions!");
    }
    

}