using System.Text.Json;
using System.Text.Json.Serialization;
using SnipeITSyncFormerEmployees;

namespace SnipeSync.Dashboard;

/// <summary>
/// Past sync runs, each with only what it changed. A run's own record (steps, outcome) comes from
/// the <c>runs</c> table, written as each run ends; its rows come from the audit trail by runId.
///
/// Rows written before runs were tracked have no runId. Those are grouped into inferred runs by
/// time: rows less than <see cref="LegacyGap"/> apart belong to the same run, which matches how the
/// nightly job writes a burst of rows and then goes quiet until the next night.
/// </summary>
public sealed class RunHistory(SqliteAuditDb db)
{
    private static readonly TimeSpan LegacyGap = TimeSpan.FromMinutes(20);

    /// <summary>Upper bound on audit rows read per request; a run writes tens of rows, not thousands.</summary>
    private const int RowCap = 5000;

    /// <summary>The two actions that mean the sync declined to act on someone.</summary>
    public static readonly string[] DeclinedActions = ["SkippedNoMatch", "UnmatchedAfterReconciliation"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Records a finished run. Called by <see cref="SyncRunner"/> for every run it starts.</summary>
    public static async Task SaveAsync(SqliteAuditDb db, SyncRunState run)
    {
        await using var conn = db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO runs (runId, startedAt, finishedAt, dryRun, outcome, error, steps)
            VALUES ($id, $started, $finished, $dry, $outcome, $error, $steps)
            """;
        cmd.Parameters.AddWithValue("$id", run.RunId);
        cmd.Parameters.AddWithValue("$started", SqliteAuditDb.FormatTimestamp(run.StartedAt));
        cmd.Parameters.AddWithValue("$finished",
            run.FinishedAt is { } f ? SqliteAuditDb.FormatTimestamp(f) : DBNull.Value);
        cmd.Parameters.AddWithValue("$dry", run.DryRun ? 1 : 0);
        cmd.Parameters.AddWithValue("$outcome", run.Outcome);
        cmd.Parameters.AddWithValue("$error", (object?)run.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$steps", JsonSerializer.Serialize(run.Steps, Json));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<RunEntry>> ListAsync(
        DateTimeOffset? from, DateTimeOffset? to, bool? dryRun, int limit, ApprovalMatcher approvals,
        CancellationToken ct = default)
    {
        await using var conn = db.Open();

        var rows = new List<AuditRecord>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT id, ym, function, user, action, oldValue, newValue, detail, dryRun, timestampUtc, runId
                FROM audit {Range(cmd, "timestampUtc", from, to)}
                ORDER BY timestampUtc DESC LIMIT {RowCap}
                """;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                rows.Add(new AuditRecord
                {
                    Id = r.GetString(0),
                    Ym = r.GetString(1),
                    Function = r.GetString(2),
                    User = r.GetString(3),
                    Action = r.GetString(4),
                    OldValue = r.IsDBNull(5) ? null : r.GetString(5),
                    NewValue = r.IsDBNull(6) ? null : r.GetString(6),
                    Detail = r.IsDBNull(7) ? null : r.GetString(7),
                    DryRun = r.GetInt64(8) != 0,
                    TimestampUtc = SqliteAuditDb.ParseTimestamp(r.GetString(9)),
                    RunId = r.IsDBNull(10) ? null : r.GetString(10)
                });
            }
        }

        var recorded = new Dictionary<string, RecordedRun>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT runId, startedAt, finishedAt, dryRun, outcome, error, steps
                FROM runs {Range(cmd, "startedAt", from, to)}
                """;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetString(0);
                recorded[id] = new RecordedRun(
                    SqliteAuditDb.ParseTimestamp(r.GetString(1)),
                    r.IsDBNull(2) ? null : SqliteAuditDb.ParseTimestamp(r.GetString(2)),
                    r.GetInt64(3) != 0,
                    r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.IsDBNull(6) ? null : ParseSteps(r.GetString(6)));
            }
        }

        var groups = new List<(string Id, List<AuditRecord> Rows)>();
        groups.AddRange(rows.Where(x => x.RunId is not null)
            .GroupBy(x => x.RunId!)
            .Select(g => (g.Key, g.ToList())));
        groups.AddRange(InferLegacyRuns(rows.Where(x => x.RunId is null)));

        // A recorded run that wrote no rows (a quiet night, or one that never got going) still counts.
        foreach (var id in recorded.Keys.Where(id => groups.All(g => g.Id != id)))
            groups.Add((id, []));

        var entries = groups
            .Select(g => Build(g.Id, g.Rows, recorded.GetValueOrDefault(g.Id), approvals))
            .Where(e => dryRun is null || e.DryRun == dryRun)
            .OrderByDescending(e => e.StartedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToList();
        return entries;
    }

    private static IEnumerable<(string, List<AuditRecord>)> InferLegacyRuns(IEnumerable<AuditRecord> rows)
    {
        List<AuditRecord>? current = null;
        foreach (var row in rows.OrderBy(x => x.TimestampUtc))
        {
            var last = current?[^1];
            if (current is null || last is null
                || row.TimestampUtc - last.TimestampUtc > LegacyGap
                || row.DryRun != last.DryRun)
            {
                if (current is not null) yield return (LegacyId(current), current);
                current = [];
            }
            current.Add(row);
        }
        if (current is { Count: > 0 }) yield return (LegacyId(current), current);
    }

    private static string LegacyId(List<AuditRecord> rows) => $"inferred-{rows[0].TimestampUtc.UtcTicks}";

    private static RunEntry Build(string id, List<AuditRecord> rows, RecordedRun? run, ApprovalMatcher approvals)
    {
        var ordered = rows.OrderBy(x => x.TimestampUtc).ToList();

        var changes = ordered.Where(x => !DeclinedActions.Contains(x.Action)).ToList();

        // One line per person the sync declined, however many declining rows they have in the run.
        var declined = ordered.Where(x => DeclinedActions.Contains(x.Action))
            .GroupBy(x => x.User)
            .Select(g =>
            {
                var email = g.Select(x => x.Detail).FirstOrDefault(LooksLikeEmail);
                return new DeclinedPerson(
                    g.Key, email, g.Select(x => x.Action).Distinct().ToList(),
                    g.Select(x => x.Function).Distinct().ToList(), g.Last(),
                    approvals.IsApproved(g.Key, email));
            })
            .OrderBy(p => p.Approved)
            .ThenBy(p => p.User, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RunEntry(
            id,
            run is not null,
            run?.StartedAt ?? ordered.FirstOrDefault()?.TimestampUtc ?? DateTimeOffset.MinValue,
            run?.FinishedAt ?? ordered.LastOrDefault()?.TimestampUtc,
            run?.DryRun ?? ordered.FirstOrDefault()?.DryRun ?? false,
            run?.Outcome,
            run?.Error,
            run?.Steps,
            ordered.Count,
            changes,
            declined);
    }

    private static JsonElement? ParseSteps(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikeEmail(string? s) =>
        s is not null && s.Contains('@') && !s.Contains(' ') && s.IndexOf('.', s.IndexOf('@')) > 0;

    private static string Range(Microsoft.Data.Sqlite.SqliteCommand cmd, string column, DateTimeOffset? from, DateTimeOffset? to)
    {
        var parts = new List<string>();
        if (from is not null)
        {
            parts.Add($"{column} >= $from");
            cmd.Parameters.AddWithValue("$from", SqliteAuditDb.FormatTimestamp(from.Value));
        }
        if (to is not null)
        {
            parts.Add($"{column} <= $to");
            cmd.Parameters.AddWithValue("$to", SqliteAuditDb.FormatTimestamp(to.Value));
        }
        return parts.Count > 0 ? "WHERE " + string.Join(" AND ", parts) : "";
    }

    private sealed record RecordedRun(
        DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, bool DryRun, string Outcome, string? Error,
        JsonElement? Steps);
}

/// <summary>One run as GET /api/runs returns it.</summary>
public sealed record RunEntry(
    [property: JsonPropertyName("runId")] string RunId,
    // False when the run has no row in the runs table (it ran before run history was kept), so
    // its steps and outcome are unknown.
    [property: JsonPropertyName("recorded")] bool Recorded,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("finishedAt")] DateTimeOffset? FinishedAt,
    [property: JsonPropertyName("dryRun")] bool DryRun,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("steps")] JsonElement? Steps,
    [property: JsonPropertyName("rowCount")] int RowCount,
    // Only what the run changed, oldest first.
    [property: JsonPropertyName("changes")] IReadOnlyList<AuditRecord> Changes,
    [property: JsonPropertyName("declined")] IReadOnlyList<DeclinedPerson> Declined);

public sealed record DeclinedPerson(
    [property: JsonPropertyName("user")] string User,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("actions")] IReadOnlyList<string> Actions,
    [property: JsonPropertyName("functions")] IReadOnlyList<string> Functions,
    // This person's last row in the run, so the dashboard can open its details.
    [property: JsonPropertyName("record")] AuditRecord Record,
    [property: JsonPropertyName("approved")] bool Approved);
