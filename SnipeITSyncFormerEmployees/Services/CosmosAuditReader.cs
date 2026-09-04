using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Cosmos DB implementation of <see cref="IAuditReader"/> — the Azure path. Runs the same
/// parameterized SQL / GROUP BY queries the audit HTTP functions used to run inline, now behind the
/// backend-agnostic interface. Server-side ORDER BY, LIMIT and GROUP BY keep it efficient at scale.
/// </summary>
public class CosmosAuditReader(ILogger<CosmosAuditReader> logger, SyncOptions options, CosmosClient? cosmos)
    : IAuditReader
{
    public bool IsAvailable => cosmos is not null && options.UseCosmosAudit;

    private Container Container => cosmos!.GetContainer(options.CosmosDatabaseName, options.CosmosAuditContainer);

    public async Task<IReadOnlyList<AuditRecord>> QueryAsync(AuditQueryFilter filter, CancellationToken ct)
    {
        // Only supplied filters become conditions; fragments are fixed strings and values flow through
        // parameters, so this stays injection-safe. The clamped int limit is inlined (parameterized
        // LIMIT support varies across Cosmos versions).
        var conditions = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        if (filter.User is not null) { conditions.Add("c.user = @user"); parameters.Add(("@user", filter.User)); }
        if (filter.Action is not null) { conditions.Add("c.action = @action"); parameters.Add(("@action", filter.Action)); }
        if (filter.Function is not null) { conditions.Add("c.function = @function"); parameters.Add(("@function", filter.Function)); }
        if (filter.From is not null) { conditions.Add("c.timestampUtc >= @from"); parameters.Add(("@from", filter.From.Value.ToString("o"))); }
        if (filter.To is not null) { conditions.Add("c.timestampUtc <= @to"); parameters.Add(("@to", filter.To.Value.ToString("o"))); }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var sql = $"SELECT * FROM c {where} ORDER BY c.timestampUtc DESC OFFSET 0 LIMIT {filter.Limit}";
        var query = new QueryDefinition(sql);
        foreach (var (name, value) in parameters) query = query.WithParameter(name, value);

        try
        {
            var results = new List<AuditRecord>(filter.Limit);
            using var iterator = Container.GetItemQueryIterator<AuditRecord>(query);
            while (iterator.HasMoreResults && results.Count < filter.Limit)
                results.AddRange(await iterator.ReadNextAsync(ct));
            return results;
        }
        catch (CosmosException e)
        {
            logger.LogWarning("Cosmos audit query failed: {Status} {Error}", e.StatusCode, e.Message);
            throw new AuditReaderException("Audit query failed.", e);
        }
    }

    public async Task<AuditStatsResult> StatsAsync(AuditStatsFilter filter, CancellationToken ct)
    {
        var conditions = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        if (filter.Function is not null) { conditions.Add("c.function = @function"); parameters.Add(("@function", filter.Function)); }
        if (filter.DryRun is not null) { conditions.Add("c.dryRun = @dryRun"); parameters.Add(("@dryRun", filter.DryRun.Value)); }
        if (filter.From is not null) { conditions.Add("c.timestampUtc >= @from"); parameters.Add(("@from", filter.From.Value.ToString("o"))); }
        if (filter.To is not null) { conditions.Add("c.timestampUtc <= @to"); parameters.Add(("@to", filter.To.Value.ToString("o"))); }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;

        try
        {
            var byAction = await GroupCountAsync(where, parameters, "action", ct);
            var byFunction = await GroupCountAsync(where, parameters, "function", ct);
            var byMonth = await GroupCountAsync(where, parameters, "ym", ct);
            return new AuditStatsResult(byAction.Sum(b => b.Count), byAction, byFunction, byMonth);
        }
        catch (CosmosException e)
        {
            logger.LogWarning("Cosmos audit stats failed: {Status} {Error}", e.StatusCode, e.Message);
            throw new AuditReaderException("Audit stats query failed.", e);
        }
    }

    private async Task<List<CountBucket>> GroupCountAsync(
        string where, List<(string Name, object Value)> parameters, string field, CancellationToken ct)
    {
        // field is a fixed, caller-supplied column name (never user input) so it is safe to inline.
        var sql = $"SELECT c.{field} AS key, COUNT(1) AS count FROM c {where} GROUP BY c.{field}";
        var query = new QueryDefinition(sql);
        foreach (var (name, value) in parameters) query = query.WithParameter(name, value);

        var buckets = new List<CountBucket>();
        using var iterator = Container.GetItemQueryIterator<CountBucket>(query);
        while (iterator.HasMoreResults) buckets.AddRange(await iterator.ReadNextAsync(ct));
        buckets.Sort((a, b) => b.Count.CompareTo(a.Count));
        return buckets;
    }
}
