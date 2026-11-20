using System.Text.Json.Serialization;
using SnipeITSyncFormerEmployees;

namespace SnipeSync.Dashboard;

/// <summary>
/// Accounts the maintainer has marked as fine without a Snipe-IT record: shared mailboxes, service
/// accounts, people who were never meant to be in Snipe-IT. The sync keeps auditing them exactly as
/// before (nothing here changes what it does); the dashboard just stops listing them under
/// "Needs a look" and shows their declined rows as approved.
///
/// An approval is keyed by the account's email when one is known, else by its display name, and
/// matches a person on either.
/// </summary>
public sealed class ApprovalStore
{
    /// <summary>
    /// The accounts approved when this feature shipped (Alex, 2026-11-20): three shared mailboxes and
    /// one account confirmed as not needing a Snipe-IT record. Seeded once, so removing one in the app
    /// sticks across restarts.
    /// </summary>
    private static readonly (string User, string Email)[] Seed =
    [
        ("Sales Ask", "salesask@diamondkitchenandbath.com"),
        ("Backsplash", "backsplash@diamondkitchenandbath.com"),
        ("Gilbert Store (Not the Sales Staff)", "gilbertstore@diamondkitchenandbath.com"),
        ("Noah Bright", "NoahB@diamondkitchenandbath.com"),
    ];

    private const string SeedMarker = "seed:approvals:2026-11-20";
    private const string SeedNote = "Approved when approvals were added; no Snipe-IT record needed.";

    private readonly SqliteAuditDb _db;

    public ApprovalStore(SqliteAuditDb db)
    {
        _db = db;
        SeedOnce();
    }

    public static string KeyFor(string user, string? email) =>
        (string.IsNullOrWhiteSpace(email) ? user : email).Trim().ToLowerInvariant();

    public async Task<IReadOnlyList<Approval>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, user, email, note, approvedAt FROM approvals ORDER BY user COLLATE NOCASE";
        var list = new List<Approval>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new Approval(
                r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), SqliteAuditDb.ParseTimestamp(r.GetString(4))));
        }
        return list;
    }

    public async Task<Approval> AddAsync(string user, string? email, string? note, CancellationToken ct = default)
    {
        var approval = new Approval(KeyFor(user, email), user.Trim(), Blank(email), Blank(note), DateTimeOffset.UtcNow);
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        // Approving again just refreshes the note and date.
        cmd.CommandText = """
            INSERT INTO approvals (key, user, email, note, approvedAt) VALUES ($key, $user, $email, $note, $at)
            ON CONFLICT(key) DO UPDATE SET user = $user, email = $email, note = $note, approvedAt = $at
            """;
        cmd.Parameters.AddWithValue("$key", approval.Key);
        cmd.Parameters.AddWithValue("$user", approval.User);
        cmd.Parameters.AddWithValue("$email", (object?)approval.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", (object?)approval.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", SqliteAuditDb.FormatTimestamp(approval.ApprovedAt));
        await cmd.ExecuteNonQueryAsync(ct);
        return approval;
    }

    /// <summary>Returns false when there was nothing to remove.</summary>
    public async Task<bool> RemoveAsync(string key, CancellationToken ct = default)
    {
        await using var conn = _db.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM approvals WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key.Trim().ToLowerInvariant());
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private void SeedOnce()
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        using var mark = conn.CreateCommand();
        mark.Transaction = tx;
        mark.CommandText = "INSERT OR IGNORE INTO meta (key, value) VALUES ($k, $v)";
        mark.Parameters.AddWithValue("$k", SeedMarker);
        mark.Parameters.AddWithValue("$v", SqliteAuditDb.FormatTimestamp(DateTimeOffset.UtcNow));
        if (mark.ExecuteNonQuery() == 0)
            return; // Seeded before; the transaction rolls back on dispose with nothing in it.

        var at = SqliteAuditDb.FormatTimestamp(DateTimeOffset.UtcNow);
        foreach (var (user, email) in Seed)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR IGNORE INTO approvals (key, user, email, note, approvedAt)
                VALUES ($key, $user, $email, $note, $at)
                """;
            cmd.Parameters.AddWithValue("$key", KeyFor(user, email));
            cmd.Parameters.AddWithValue("$user", user);
            cmd.Parameters.AddWithValue("$email", email);
            cmd.Parameters.AddWithValue("$note", SeedNote);
            cmd.Parameters.AddWithValue("$at", at);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Matches a person against the approval list on email or display name.</summary>
public sealed class ApprovalMatcher(IEnumerable<Approval> approvals)
{
    private readonly HashSet<string> _keys = approvals
        .SelectMany(a => new[] { a.Key, a.User.Trim().ToLowerInvariant(), a.Email?.Trim().ToLowerInvariant() })
        .OfType<string>()
        .ToHashSet();

    public bool IsApproved(string user, string? email) =>
        _keys.Contains(user.Trim().ToLowerInvariant())
        || (email is not null && _keys.Contains(email.Trim().ToLowerInvariant()));
}

public sealed record Approval(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("user")] string User,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("approvedAt")] DateTimeOffset ApprovedAt);

/// <summary>Body of POST /api/approvals.</summary>
public sealed record ApprovalRequest(string? User, string? Email, string? Note);
