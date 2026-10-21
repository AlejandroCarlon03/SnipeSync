using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photino.NET;
using SnipeITSyncFormerEmployees;
using SnipeSync.Dashboard;

// Fixed loopback port, so the Vite dev proxy has a stable target and a second launch can detect an
// instance that's already running.
const int DashboardPort = 5177;

// Header the SPA sends on POST /api/sync. A cross-origin page can't add a custom header without a CORS
// preflight, which this host never approves — so only the dashboard itself can start a sync.
const string SyncHeader = "X-SnipeSync";

LocalSettings.EnsureExists();

// --sync: run the whole sync once without any UI and exit. This is what the installer's optional nightly
// scheduled task runs, now that the Azure timers are gone. Exit code 0 = clean, 1 = errors.
if (args.Contains("--sync"))
{
    using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
    var result = await new SyncRunner(loggerFactory.CreateLogger<SyncRunner>())
        .RunToCompletionAsync(args.Contains("--dry-run"));

    // A scheduled task has no console to read afterwards, so the run's log also goes to a file.
    var report = result.Log.Select(l => $"{l.At:u} {l.Level,-11} {l.Message}")
        .Concat(result.Steps.Select(s => $"{s.Step,-10} {s.State,-8} {s.Error ?? DescribeCounts(s.Counts)}"))
        .Append($"Sync {result.RunId}: {result.Outcome}{(result.DryRun ? " (dry run)" : "")}")
        .ToList();
    report.ForEach(Console.WriteLine);
    await File.WriteAllLinesAsync(Path.Combine(LocalSettings.DataDirectory, "last-sync.log"), report);
    return result.Outcome == "succeeded" ? 0 : 1;
}

// --window: the original Photino desktop window. Not the default, because WebView2 renders it solid
// black on the machines this runs on (see SESSION_CONTEXT); the browser mode below is what ships.
// --headless: serve only, for development (the Vite dev server proxies to it).
var windowMode = args.Contains("--window");
var headless = args.Contains("--headless")
    || string.Equals(Environment.GetEnvironmentVariable("DASHBOARD_HEADLESS"), "1", StringComparison.Ordinal);
var browserMode = !windowMode && !headless;

// Launching from the shortcut again should reopen the window, not fail to bind the port.
if (browserMode && IsPortInUse(DashboardPort))
{
    OpenInBrowser($"http://127.0.0.1:{DashboardPort}");
    return 0;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = "wwwroot"
});

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false);

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection(DashboardOptions.SectionName));
builder.Services.AddSingleton(new SqliteAuditDb(LocalSettings.AuditDbPath));
builder.Services.AddSingleton<IAuditReader, SqliteAuditReader>();
builder.Services.AddSingleton<SyncRunner>();

// Loopback only; the dashboard is a local single-user tool. The Photino window gets an OS-assigned port.
builder.WebHost.UseUrls(windowMode ? "http://127.0.0.1:0" : $"http://127.0.0.1:{DashboardPort}");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// --- Audit trail: same contract the Functions endpoints served, now from the local SQLite file -------
app.MapGet("/api/audit", async (HttpRequest req, IAuditReader reader, CancellationToken ct) =>
{
    var q = req.Query;
    if (!AuditQueryParsing.TryParseDate(q["from"], endOfDayIfDateOnly: false, out var from))
        return Results.BadRequest("Invalid 'from' date; use ISO 8601 (e.g. 2026-07-01).");
    if (!AuditQueryParsing.TryParseDate(q["to"], endOfDayIfDateOnly: true, out var to))
        return Results.BadRequest("Invalid 'to' date; use ISO 8601 (e.g. 2026-07-31).");

    var filter = new AuditQueryFilter(
        AuditQueryParsing.Trimmed(q["user"]), AuditQueryParsing.Trimmed(q["action"]),
        AuditQueryParsing.Trimmed(q["function"]), from, to, AuditQueryParsing.ParseLimit(q["limit"]));
    try
    {
        return Results.Ok(await reader.QueryAsync(filter, ct));
    }
    catch (AuditReaderException e)
    {
        return Results.Problem(title: "Audit query failed.", detail: e.InnerException?.Message);
    }
});

app.MapGet("/api/audit/stats", async (HttpRequest req, IAuditReader reader, CancellationToken ct) =>
{
    var q = req.Query;
    var function = AuditQueryParsing.Trimmed(q["function"]);
    if (!AuditQueryParsing.TryParseBool(q["dryRun"], out var dryRun))
        return Results.BadRequest("Invalid 'dryRun'; use true or false.");
    if (!AuditQueryParsing.TryParseDate(q["from"], endOfDayIfDateOnly: false, out var from))
        return Results.BadRequest("Invalid 'from' date; use ISO 8601 (e.g. 2026-07-01).");
    if (!AuditQueryParsing.TryParseDate(q["to"], endOfDayIfDateOnly: true, out var to))
        return Results.BadRequest("Invalid 'to' date; use ISO 8601 (e.g. 2026-07-31).");

    try
    {
        var stats = await reader.StatsAsync(new AuditStatsFilter(function, dryRun, from, to), ct);
        return Results.Ok(new AuditStatsResponse
        {
            From = from?.ToString("o"),
            To = to?.ToString("o"),
            Function = function,
            DryRun = dryRun,
            Total = stats.Total,
            ByAction = stats.ByAction.ToList(),
            ByFunction = stats.ByFunction.ToList(),
            ByMonth = stats.ByMonth.ToList()
        });
    }
    catch (AuditReaderException e)
    {
        return Results.Problem(title: "Audit stats query failed.", detail: e.InnerException?.Message);
    }
});

// --- Manual sync --------------------------------------------------------------------------------------
app.MapPost("/api/sync", (HttpRequest req, SyncRequest? body, SyncRunner runner) =>
{
    if (!req.Headers.ContainsKey(SyncHeader))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var run = runner.TryStart(body?.DryRun ?? false);
    return run is null
        ? Results.Conflict(new { error = "A sync is already running." })
        : Results.Accepted("/api/sync/status", run);
});

app.MapGet("/api/sync/status", (SyncRunner runner) =>
{
    IReadOnlyList<string> missing;
    string? settingsError = null;
    try
    {
        missing = LocalSettings.Missing();
    }
    catch (InvalidOperationException e)
    {
        missing = LocalSettings.Required;
        settingsError = e.Message;
    }

    return Results.Ok(new
    {
        settingsPath = LocalSettings.SettingsPath,
        missingSettings = missing,
        settingsError,
        running = runner.IsRunning,
        run = runner.Snapshot
    });
});

// One-time setup: the SPA uploads the old Functions local.settings.json and we keep its sync settings.
app.MapPost("/api/settings/import", async (HttpRequest req) =>
{
    if (!req.Headers.ContainsKey(SyncHeader))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    using var reader = new StreamReader(req.Body);
    try
    {
        var keys = LocalSettings.Import(await reader.ReadToEndAsync());
        return Results.Ok(new { imported = keys, missingSettings = LocalSettings.Missing() });
    }
    catch (InvalidOperationException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

// SPA fallback for client-side routing.
app.MapFallbackToFile("index.html");

await app.StartAsync();

var boundUrl = app.Services.GetRequiredService<IServer>().Features
    .Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? $"http://127.0.0.1:{DashboardPort}";

if (browserMode)
{
    OpenInBrowser(boundUrl);
    await app.WaitForShutdownAsync();
    return 0;
}

if (headless)
{
    Console.WriteLine($"SnipeSync dashboard running headless at {boundUrl} (Ctrl+C to stop).");
    await app.WaitForShutdownAsync();
    return 0;
}

// Photino must own the main thread on Windows.
var title = app.Services.GetRequiredService<IOptions<DashboardOptions>>().Value.WindowTitle;
var window = new PhotinoWindow()
    .SetTitle(title)
    .SetIconFile(Path.Combine(AppContext.BaseDirectory, "snipesync.ico"))
    .SetUseOsDefaultSize(false)
    .SetSize(1280, 820)
    .Center()
    .SetContextMenuEnabled(false)
    .Load(new Uri(boundUrl));

window.WaitForClose();

await app.StopAsync();
return 0;

// --- helpers --------------------------------------------------------------------

static string DescribeCounts(SyncStepCounts? c) => c is null
    ? ""
    : $"processed {c.Processed}, offboarded {c.Offboarded}, onboarded {c.Onboarded}, rehired {c.Rehired}, " +
      $"seats out {c.LicensesCheckedOut}, unmatched {c.Unmatched.Count}, failed {c.Failed}";

/// <summary>Is something already serving on the loopback dashboard port?</summary>
static bool IsPortInUse(int port)
{
    try
    {
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
}

/// <summary>
/// Opens the dashboard for the user. Edge's --app mode gives a window with no tabs or
/// address bar, which is the closest thing to the intended desktop app while WebView2
/// is unusable here; anything unexpected falls back to the default browser.
/// </summary>
static void OpenInBrowser(string url)
{
    string[] edgeCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft", "Edge", "Application", "msedge.exe")
    ];

    var edge = edgeCandidates.FirstOrDefault(File.Exists);

    try
    {
        if (edge is not null)
        {
            Process.Start(new ProcessStartInfo(edge, $"--app={url}") { UseShellExecute = false });
            return;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Could not open Edge in app mode ({e.Message}); using the default browser.");
    }

    // ShellExecute hands the URL to whatever the user's default browser is.
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

/// <summary>Body of POST /api/sync.</summary>
internal sealed record SyncRequest(bool DryRun);
