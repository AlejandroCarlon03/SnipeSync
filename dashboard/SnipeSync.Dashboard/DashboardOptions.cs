namespace SnipeSync.Dashboard;

/// <summary>Host settings, bound from the "SnipeSync" section of appsettings.json.</summary>
public sealed class DashboardOptions
{
    public const string SectionName = "SnipeSync";

    /// <summary>Native window title (only used by --window mode).</summary>
    public string WindowTitle { get; set; } = "SnipeSync";
}
