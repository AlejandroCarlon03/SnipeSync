namespace SnipeSync.Dashboard;

/// <summary>Host settings, bound from the "SnipeSync" config section (appsettings.json + overrides).</summary>
public sealed class DashboardOptions
{
    public const string SectionName = "SnipeSync";

    /// <summary>
    /// Base URL of the deployed Functions app, e.g. https://dkb-snipeit-sync.azurewebsites.net.
    /// Empty until configured — the proxy returns 503 so the UI shows a clear "not configured" message.
    /// </summary>
    public string FunctionsBaseUrl { get; set; } = "";

    /// <summary>
    /// Azure Functions key, attached to upstream calls server-side (?code=…). Kept out of the browser.
    /// Put the real value in a local, untracked override (appsettings.Development.json or user-secrets),
    /// not in the committed appsettings.json.
    /// </summary>
    public string FunctionKey { get; set; } = "";

    /// <summary>Native window title.</summary>
    public string WindowTitle { get; set; } = "SnipeSync Audit Dashboard";
}
