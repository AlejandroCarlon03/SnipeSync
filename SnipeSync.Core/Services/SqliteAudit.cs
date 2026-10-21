using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Ambient run id for audit rows. Set once at the start of a sync run so every row that run writes can
/// be grouped back together, without threading an id through <see cref="IAuditService.RecordAsync"/>
/// and every call site. AsyncLocal flows across the awaits inside the run and nowhere else.
/// </summary>
public static class AuditRunContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? RunId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}

/// <summary>
/// The local audit database: one SQLite file holding the same rows the Cosmos/Table backends store,
/// so the dashboard works with no Azure subscription. Owns the path and the schema; the reader and
/// writer below each open short-lived connections against it.
/// </summary>
public sealed class SqliteAuditDb
{
    // Fixed-width UTC text sorts chronologically, so range filters and ORDER BY work on the raw column.
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private readonly string _connectionString;

    public SqliteAuditDb(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        // WAL lets the dashboard keep reading while a sync run is writing.
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS audit (
                id           TEXT PRIMARY KEY,
                ym           TEXT NOT NULL,
                function     TEXT NOT NULL,
                user         TEXT NOT NULL,
                action       TEXT NOT NULL,
                oldValue     TEXT NULL,
                newValue     TEXT NULL,
                detail       TEXT NULL,
                dryRun       INTEGER NOT NULL,
                timestampUtc TEXT NOT NULL,
                runId        TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_audit_time ON audit (timestampUtc);
            CREATE INDEX IF NOT EXISTS ix_audit_run ON audit (runId);
            """;
        cmd.ExecuteNonQuery();
    }

    public string Path { get; }

    internal SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>Inserts records, skipping ids already present. Returns how many were new.</summary>
    public async Task<int> InsertAsync(IEnumerable<AuditRecord> records, string? runId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await using var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO audit (id, ym, function, user, action, oldValue, newValue, detail, dryRun, timestampUtc, runId)
            VALUES ($id, $ym, $function, $user, $action, $oldValue, $newValue, $detail, $dryRun, $ts, $runId)
            """;
        var p = new Dictionary<string, SqliteParameter>();
        foreach (var name in new[] { "$id", "$ym", "$function", "$user", "$action", "$oldValue", "$newValue", "$detail", "$dryRun", "$ts", "$runId" })
            p[name] = cmd.Parameters.Add(new SqliteParameter { ParameterName = name });

        var inserted = 0;
        foreach (var r in records)
        {
            p["$id"].Value = r.Id;
            p["$ym"].Value = r.Ym;
            p["$function"].Value = r.Function;
            p["$user"].Value = r.User;
            p["$action"].Value = r.Action;
            p["$oldValue"].Value = (object?)r.OldValue ?? DBNull.Value;
            p["$newValue"].Value = (object?)r.NewValue ?? DBNull.Value;
            p["$detail"].Value = (object?)r.Detail ?? DBNull.Value;
            p["$dryRun"].Value = r.DryRun ? 1 : 0;
            p["$ts"].Value = FormatTimestamp(r.TimestampUtc);
            p["$runId"].Value = (object?)runId ?? DBNull.Value;
            inserted += await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return inserted;
    }

    internal static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    internal static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}

/// <summary>Writes audit rows to the local SQLite file, tagged with the current <see cref="AuditRunContext.RunId"/>.</summary>
public class SqliteAuditService(ILogger<SqliteAuditService> logger, SyncOptions options, SqliteAuditDb db) : IAuditService
{
    public async Task RecordAsync(
        string function, string user, string action,
        string? oldValue = null, string? newValue = null, string? detail = null)
    {
        var record = AuditRecord.Create(function, user, action, oldValue, newValue, detail, options.DryRun,
            DateTimeOffset.UtcNow);
        try
        {
            await db.InsertAsync([record], AuditRunContext.RunId);
        }
        catch (SqliteException e)
        {
            // Same contract as the Cosmos/Table writers: a failed audit write never fails the sync itself.
            logger.LogWarning("Failed to write audit record for {User}: {Error}", user, e.Message);
        }
    }
}

/// <summary>
/// Reads the local SQLite audit file with the same filters, ordering and grouping as the Cosmos and
/// Table readers, so /api/audit and /api/audit/stats look identical to the dashboard.
/// </summary>
public class SqliteAuditReader(SqliteAuditDb db) : IAuditReader
{
    public bool IsAvailable => true;

    public async Task<IReadOnlyList<AuditRecord>> QueryAsync(AuditQueryFilter filter, CancellationToken ct)
    {
        try
        {
            await using var conn = db.Open();
            await using var cmd = conn.CreateCommand();
            var where = BuildWhere(cmd, filter.User, filter.Action, filter.Function, null, filter.From, filter.To);
            cmd.CommandText = $"""
                SELECT id, ym, function, user, action, oldValue, newValue, detail, dryRun, timestampUtc
                FROM audit {where} ORDER BY timestampUtc DESC LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", filter.Limit);

            var list = new List<AuditRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new AuditRecord
                {
                    Id = reader.GetString(0),
                    Ym = reader.GetString(1),
                    Function = reader.GetString(2),
                    User = reader.GetString(3),
                    Action = reader.GetString(4),
                    OldValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                    NewValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Detail = reader.IsDBNull(7) ? null : reader.GetString(7),
                    DryRun = reader.GetInt64(8) != 0,
                    TimestampUtc = SqliteAuditDb.ParseTimestamp(reader.GetString(9))
                });
            }
            return list;
        }
        catch (SqliteException e)
        {
            throw new AuditReaderException("Audit query failed.", e);
        }
    }

    public async Task<AuditStatsResult> StatsAsync(AuditStatsFilter filter, CancellationToken ct)
    {
        try
        {
            await using var conn = db.Open();
            return new AuditStatsResult(
                await CountAsync(conn, filter, ct),
                await GroupAsync(conn, "action", filter, ct),
                await GroupAsync(conn, "function", filter, ct),
                await GroupAsync(conn, "ym", filter, ct));
        }
        catch (SqliteException e)
        {
            throw new AuditReaderException("Audit stats query failed.", e);
        }
    }

    private static async Task<int> CountAsync(SqliteConnection conn, AuditStatsFilter f, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM audit {BuildWhere(cmd, null, null, f.Function, f.DryRun, f.From, f.To)}";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    // `column` is always one of three literals from StatsAsync, never caller input.
    private static async Task<List<CountBucket>> GroupAsync(
        SqliteConnection conn, string column, AuditStatsFilter f, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        var where = BuildWhere(cmd, null, null, f.Function, f.DryRun, f.From, f.To);
        cmd.CommandText = $"SELECT {column}, COUNT(*) AS n FROM audit {where} GROUP BY {column} ORDER BY n DESC";

        var buckets = new List<CountBucket>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            buckets.Add(new CountBucket { Key = reader.IsDBNull(0) ? null : reader.GetString(0), Count = reader.GetInt32(1) });
        return buckets;
    }

    /// <summary>Fixed condition strings with values bound as parameters — injection-safe, like the Cosmos reader.</summary>
    private static string BuildWhere(
        SqliteCommand cmd, string? user, string? action, string? function, bool? dryRun,
        DateTimeOffset? from, DateTimeOffset? to)
    {
        var parts = new List<string>();
        void Add(string condition, string name, object value)
        {
            parts.Add(condition);
            cmd.Parameters.AddWithValue(name, value);
        }

        if (user is not null) Add("user = $user", "$user", user);
        if (action is not null) Add("action = $action", "$action", action);
        if (function is not null) Add("function = $function", "$function", function);
        if (dryRun is not null) Add("dryRun = $dryRun", "$dryRun", dryRun.Value ? 1 : 0);
        if (from is not null) Add("timestampUtc >= $from", "$from", SqliteAuditDb.FormatTimestamp(from.Value));
        if (to is not null) Add("timestampUtc <= $to", "$to", SqliteAuditDb.FormatTimestamp(to.Value));
        return parts.Count > 0 ? "WHERE " + string.Join(" AND ", parts) : string.Empty;
    }
}
