using System.Text.Json;

namespace SnipeSync.Dashboard;

/// <summary>
/// The local app's secrets and sync settings: %LOCALAPPDATA%\SnipeSync\settings.json, with a
/// <c>Values</c> block shaped exactly like the Functions app's local.settings.json so one can be copied
/// into the other. It lives outside the install folder so upgrading or reinstalling never wipes it.
///
/// The sync engine reads its settings from environment variables (that's how Azure supplied them), so
/// <see cref="Apply"/> copies the values into this process's environment before each run. Re-reading
/// per run means an edit to the file takes effect on the next Sync click without a restart.
/// </summary>
public static class LocalSettings
{
    /// <summary>Settings without which nothing can sync: Graph credentials and the Snipe-IT endpoint.</summary>
    public static readonly string[] Required =
        ["AZURE_TENANT_ID", "AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET", "SNIPEIT_URL", "SNIPEIT_API_KEY"];

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipeSync");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string AuditDbPath => Path.Combine(DataDirectory, "audit.db");

    /// <summary>
    /// Creates settings.json from the shipped template on first run, so there is a file to fill in
    /// rather than a path to guess. Never overwrites an existing file.
    /// </summary>
    public static void EnsureExists()
    {
        if (File.Exists(SettingsPath)) return;
        Directory.CreateDirectory(DataDirectory);

        var template = Path.Combine(AppContext.BaseDirectory, "settings.example.json");
        if (File.Exists(template))
            File.Copy(template, SettingsPath);
    }

    /// <summary>Loads the Values block into the process environment. Returns the required keys still missing.</summary>
    public static IReadOnlyList<string> Apply()
    {
        foreach (var (key, value) in Read())
            Environment.SetEnvironmentVariable(key, value);

        return Required
            .Where(k => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(k)))
            .ToList();
    }

    /// <summary>The required keys settings.json doesn't fill in, without touching the environment.</summary>
    public static IReadOnlyList<string> Missing()
    {
        var values = Read();
        return Required.Where(k => !values.ContainsKey(k)).ToList();
    }

    /// <summary>
    /// Settings that only meant something to the Azure Function App (its storage, Cosmos, the queue,
    /// telemetry). Dropped on import so the local file only holds what the sync actually reads.
    /// </summary>
    private static readonly HashSet<string> AzureOnlyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "AzureWebJobsStorage", "AzureWebJobsDisableHomepage", "FUNCTIONS_WORKER_RUNTIME",
        "APPLICATIONINSIGHTS_CONNECTION_STRING", "REQUIRE_AUTH", "AUDIT_BACKEND", "RECONCILIATION_QUEUE_NAME",
        "COSMOS_CONNECTION_STRING", "COSMOS_DATABASE_NAME", "COSMOS_AUDIT_CONTAINER",
        "AUDIT_TABLE_CONNECTION_STRING", "AUDIT_TABLE_NAME"
    };

    /// <summary>
    /// Replaces settings.json with the Values from a pasted/uploaded local.settings.json (the old Functions
    /// settings file), minus the Azure-only keys. Returns the keys imported.
    /// </summary>
    public static IReadOnlyList<string> Import(string localSettingsJson)
    {
        var values = Parse(localSettingsJson, "The selected file")
            .Where(kv => !AzureOnlyKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (values.Count == 0)
            throw new InvalidOperationException("The selected file has no \"Values\" settings to import.");

        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["//"] = "SnipeSync settings, imported from a Functions local.settings.json. See the README for every key.",
            ["Values"] = values
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
        return values.Keys.Order().ToList();
    }

    private static Dictionary<string, string> Read() =>
        File.Exists(SettingsPath) ? Parse(File.ReadAllText(SettingsPath), SettingsPath) : [];

    private static Dictionary<string, string> Parse(string json, string source)
    {
        JsonDocument doc;
        try
        {
            // Lenient on purpose: func's local.settings.json tolerates trailing commas, so files written
            // for it often have them.
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException e)
        {
            // Surfaces on the dashboard as the reason a sync can't start, rather than as a crash.
            throw new InvalidOperationException($"{source} is not valid JSON: {e.Message}", e);
        }
        using var _ = doc;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("Values", out var block)
            || block.ValueKind != JsonValueKind.Object)
            return values;

        foreach (var prop in block.EnumerateObject())
        {
            // Placeholders from the template ("<...>") count as unset, so the missing-settings check catches them.
            var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
            if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith('<'))
                values[prop.Name] = value;
        }
        return values;
    }
}
