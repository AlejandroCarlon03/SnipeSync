using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Wraps the Entra ID (Graph) queries. Centralizes the select/expand list and, when
/// EMPLOYEE_SECURITY_GROUP_ID is configured (feature 9), scopes both queries to the
/// transitive members of that security group so contractors / service accounts /
/// shared mailboxes outside the group are never picked up.
/// </summary>
public class EntraUserService(
    ILogger<EntraUserService> logger,
    GraphServiceClient graphServiceClient,
    SyncOptions options)
{
    private static readonly string[] SelectFields =
    [
        "id", "displayName", "accountEnabled", "mail",
        "givenName", "surname", "mailNickname", "jobTitle",
        "department", "officeLocation"
    ];

    private static readonly string[] ExpandManager = ["manager($select=displayName,mail)"];

    public Task<List<User>> GetDisabledUsersAsync() =>
        QueryAsync("accountEnabled eq false");

    public Task<List<User>> GetRecentlyCreatedEnabledUsersAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-options.OnboardLookbackDays);
        return QueryAsync($"createdDateTime ge {cutoff:yyyy-MM-ddTHH:mm:ssZ} and accountEnabled eq true");
    }

    /// <summary>
    /// Recently re-enabled users are candidates for rehire handling (feature 3). Entra doesn't
    /// expose a "re-enabled" timestamp, so we scan currently-enabled accounts and let the caller
    /// reconcile against Snipe-IT's former-employee flag.
    /// </summary>
    public Task<List<User>> GetEnabledUsersAsync() =>
        QueryAsync("accountEnabled eq true");

    private async Task<List<User>> QueryAsync(string filter)
    {
        try
        {
            if (options.EmployeeSecurityGroupId is { } groupId)
            {
                var scoped = await graphServiceClient
                    .Groups[groupId]
                    .TransitiveMembers
                    .GraphUser
                    .GetAsync(rc =>
                    {
                        rc.QueryParameters.Filter = filter;
                        rc.QueryParameters.Select = SelectFields;
                        rc.QueryParameters.Expand = ExpandManager;
                        rc.QueryParameters.Count = true;
                        rc.Headers.Add("ConsistencyLevel", "eventual");
                    });
                return scoped?.Value?.ToList() ?? [];
            }

            var result = await graphServiceClient.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = filter;
                rc.QueryParameters.Select = SelectFields;
                rc.QueryParameters.Expand = ExpandManager;
                rc.QueryParameters.Count = true;
                rc.Headers.Add("ConsistencyLevel", "eventual");
            });
            return result?.Value?.ToList() ?? [];
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to query Entra users (filter: {Filter}): {Error}", filter, e.Message);
            return [];
        }
    }

    /// <summary>Best-effort display name of the user's manager from an expanded query.</summary>
    public static string? GetManagerName(User user)
    {
        if (user.Manager is User manager)
            return manager.DisplayName;
        if (user.Manager?.AdditionalData is { } data && data.TryGetValue("displayName", out var value))
            return value?.ToString();
        return null;
    }

    /// <summary>Builds the Snipe-IT custom-field payload for department/manager/office (feature 2).</summary>
    public IReadOnlyDictionary<string, object?> BuildUserFields(User user)
    {
        var fields = new Dictionary<string, object?>();
        if (options.DepartmentFieldColumn is { } dept && !string.IsNullOrWhiteSpace(user.Department))
            fields[dept] = user.Department;
        if (options.OfficeFieldColumn is { } office && !string.IsNullOrWhiteSpace(user.OfficeLocation))
            fields[office] = user.OfficeLocation;
        if (options.ManagerFieldColumn is { } mgr && GetManagerName(user) is { } managerName)
            fields[mgr] = managerName;
        return fields;
    }
}
