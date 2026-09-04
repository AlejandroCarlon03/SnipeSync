using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Azure Table Storage implementation of <see cref="IAuditReader"/> — the local/free path, reading the
/// same rows <see cref="TableAuditService"/> writes (works against Azurite, no Azure subscription).
/// Table Storage has no GROUP BY, so the supplied filters are pushed down as an OData query and the
/// ordering/limit/aggregation are done in memory. Fine for a single-user local tool's audit volume;
/// the Cosmos reader remains the server-side-aggregating path for the Azure deployment.
/// </summary>
public class TableAuditReader : IAuditReader
{
    private readonly ILogger<TableAuditReader> _logger;
    private readonly TableClient? _table;

    public TableAuditReader(ILogger<TableAuditReader> logger, SyncOptions options)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(options.AuditTableConnectionString))
        {
            try
            {
                _table = new TableClient(options.AuditTableConnectionString, options.AuditTableName);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Audit table unavailable for reads: {Error}", e.Message);
            }
        }
    }

    public bool IsAvailable => _table is not null;

    public async Task<IReadOnlyList<AuditRecord>> QueryAsync(AuditQueryFilter filter, CancellationToken ct)
    {
        var records = await FetchAsync(
            BuildFilter(filter.User, filter.Action, filter.Function, null, filter.From, filter.To), ct);

        return records
            .OrderByDescending(r => r.TimestampUtc)
            .Take(filter.Limit)
            .ToList();
    }

    public async Task<AuditStatsResult> StatsAsync(AuditStatsFilter filter, CancellationToken ct)
    {
        var records = await FetchAsync(
            BuildFilter(null, null, filter.Function, filter.DryRun, filter.From, filter.To), ct);

        return new AuditStatsResult(
            records.Count,
            Group(records, r => r.Action),
            Group(records, r => r.Function),
            Group(records, r => r.Ym));
    }

    private async Task<List<AuditRecord>> FetchAsync(string? filter, CancellationToken ct)
    {
        var list = new List<AuditRecord>();
        try
        {
            await foreach (var e in _table!.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct))
                list.Add(Map(e));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table hasn't been created yet (nothing has been audited). That's an empty history,
            // not an error — the dashboard should render "no records", not fail.
            _logger.LogInformation("Audit table does not exist yet; returning empty history.");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning("Table audit query failed: {Status} {Error}", ex.Status, ex.Message);
            throw new AuditReaderException("Audit query failed.", ex);
        }
        return list;
    }

    /// <summary>Maps a stored audit row to the shared <see cref="AuditRecord"/> shape the API returns.</summary>
    private static AuditRecord Map(TableEntity e)
    {
        var ts = e.Timestamp ?? DateTimeOffset.UtcNow;
        if (e.TryGetValue("TimestampUtc", out var raw))
        {
            if (raw is DateTime dt) ts = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            else if (raw is DateTimeOffset dto) ts = dto;
        }

        return new AuditRecord
        {
            Id = e.RowKey,
            Ym = ts.ToString("yyyy-MM"),
            Function = e.GetString("Function") ?? string.Empty,
            User = e.GetString("User") ?? string.Empty,
            Action = e.GetString("Action") ?? string.Empty,
            OldValue = e.GetString("OldValue"),
            NewValue = e.GetString("NewValue"),
            Detail = e.GetString("Detail"),
            DryRun = e.TryGetValue("DryRun", out var d) && d is bool b && b,
            TimestampUtc = ts
        };
    }

    private static List<CountBucket> Group(IEnumerable<AuditRecord> records, Func<AuditRecord, string?> selector) =>
        records.GroupBy(selector)
            .Select(g => new CountBucket { Key = g.Key, Count = g.Count() })
            .OrderByDescending(b => b.Count)
            .ToList();

    /// <summary>
    /// Builds an OData filter for Table Storage from the supplied constraints. Field names are fixed
    /// literals; string values are single-quote-escaped, so this is injection-safe. Null → no filter.
    /// </summary>
    private static string? BuildFilter(
        string? user, string? action, string? function, bool? dryRun, DateTimeOffset? from, DateTimeOffset? to)
    {
        var parts = new List<string>();
        if (user is not null) parts.Add($"User eq '{Escape(user)}'");
        if (action is not null) parts.Add($"Action eq '{Escape(action)}'");
        if (function is not null) parts.Add($"Function eq '{Escape(function)}'");
        if (dryRun is not null) parts.Add($"DryRun eq {(dryRun.Value ? "true" : "false")}");
        if (from is not null) parts.Add($"TimestampUtc ge datetime'{from.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}'");
        if (to is not null) parts.Add($"TimestampUtc le datetime'{to.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}'");
        return parts.Count > 0 ? string.Join(" and ", parts) : null;
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
