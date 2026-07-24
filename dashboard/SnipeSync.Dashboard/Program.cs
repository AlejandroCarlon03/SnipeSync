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
using Microsoft.Extensions.Options;
using Photino.NET;
using SnipeSync.Dashboard;

// Fixed loopback port for headless/browser mode, so the Vite dev proxy has a stable
// target and a second launch can detect an instance that's already running.
const int DashboardPort = 5177;

// Headless mode (no native window): run the SPA + proxy on a plain HTTP server so it
// can be opened in any browser. Used for verification where no WebView2 host exists.
var headless = args.Contains("--headless")
    || string.Equals(Environment.GetEnvironmentVariable("DASHBOARD_HEADLESS"), "1", StringComparison.Ordinal);

// Browser mode: serve headless and open the UI in the user's browser instead of the
// Photino window. This is the shipping launch mode on machines where WebView2 renders
// the native window black (see SESSION_CONTEXT) -- the same build is perfect in a
// browser. It lives in the app rather than a launcher script on purpose: a desktop
// shortcut that invokes powershell.exe is silently blocked by endpoint protection on
// at least one machine here, whereas a shortcut straight to this exe runs fine.
var browserMode = args.Contains("--browser")
    || string.Equals(Environment.GetEnvironmentVariable("DASHBOARD_BROWSER"), "1", StringComparison.Ordinal);

if (browserMode)
{
    headless = true; // implies the fixed port, so the already-running check below works

    // Clicking the shortcut twice should reopen the window, not fail to bind the port.
    if (IsPortInUse(DashboardPort))
    {
        OpenInBrowser($"http://127.0.0.1:{DashboardPort}");
        return;
    }
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = "wwwroot"
});

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false);

// Local, untracked override for secrets (the function key). Gitignored; loaded last so
// its values win. Create dashboard/SnipeSync.Dashboard/appsettings.Local.json.
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"), optional: true, reloadOnChange: false);

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection(DashboardOptions.SectionName));
builder.Services.AddHttpClient("functions");

// Loopback only; the dashboard is a local single-user tool. Port 0 = OS-assigned
// (fixed 5177 in headless so the Vite dev proxy has a stable target).
builder.WebHost.UseUrls(headless ? $"http://127.0.0.1:{DashboardPort}" : "http://127.0.0.1:0");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// --- Server-side proxy to the Azure Functions audit endpoints -------------------
// The SPA calls these same-origin; we forward to the Functions app with the function
// key attached here, so the key never reaches the browser. Only these two fixed
// upstream paths are proxied — never an arbitrary caller-supplied path (no open proxy).
app.MapGet("/api/audit", (HttpContext ctx, IHttpClientFactory f, IOptions<DashboardOptions> o, CancellationToken ct) =>
    ProxyAsync(ctx, f, o.Value, "/api/audit", ct));

app.MapGet("/api/audit/stats", (HttpContext ctx, IHttpClientFactory f, IOptions<DashboardOptions> o, CancellationToken ct) =>
    ProxyAsync(ctx, f, o.Value, "/api/audit/stats", ct));

// SPA fallback for client-side routing.
app.MapFallbackToFile("index.html");

await app.StartAsync();

var boundUrl = app.Services.GetRequiredService<IServer>().Features
    .Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? $"http://127.0.0.1:{DashboardPort}";

if (browserMode)
{
    OpenInBrowser(boundUrl);
    await app.WaitForShutdownAsync();
    return;
}

if (headless)
{
    Console.WriteLine($"SnipeSync dashboard running headless at {boundUrl} (Ctrl+C to stop).");
    await app.WaitForShutdownAsync();
    return;
}

// Photino must own the main thread on Windows.
var title = app.Services.GetRequiredService<IOptions<DashboardOptions>>().Value.WindowTitle;
var window = new PhotinoWindow()
    .SetTitle(title)
    .SetUseOsDefaultSize(false)
    .SetSize(1280, 820)
    .Center()
    .SetContextMenuEnabled(false)
    .Load(new Uri(boundUrl));

window.WaitForClose();

await app.StopAsync();

// --- helpers --------------------------------------------------------------------

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

// Forwards the incoming GET (query string preserved) to opts.FunctionsBaseUrl + upstreamPath,
// appending the function key as ?code=, and relays the upstream status/body/content-type.
static async Task<IResult> ProxyAsync(
    HttpContext ctx, IHttpClientFactory factory, DashboardOptions opts, string upstreamPath, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(opts.FunctionsBaseUrl))
    {
        return Results.Problem(
            title: "Dashboard is not configured.",
            detail: "Set SnipeSync:FunctionsBaseUrl (and FunctionKey) in appsettings.json.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // Re-emit the caller's query (dropping any client-supplied 'code'), then attach our key.
    var pairs = new List<string>();
    foreach (var kv in ctx.Request.Query)
    {
        if (string.Equals(kv.Key, "code", StringComparison.OrdinalIgnoreCase)) continue;
        foreach (var v in kv.Value)
            pairs.Add($"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(v ?? string.Empty)}");
    }
    if (!string.IsNullOrWhiteSpace(opts.FunctionKey))
        pairs.Add($"code={Uri.EscapeDataString(opts.FunctionKey)}");

    var query = pairs.Count > 0 ? "?" + string.Join("&", pairs) : string.Empty;
    var target = $"{opts.FunctionsBaseUrl.TrimEnd('/')}{upstreamPath}{query}";

    var client = factory.CreateClient("functions");
    try
    {
        using var upstream = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);
        var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json";
        return Results.Content(body, contentType, statusCode: (int)upstream.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Upstream Functions request failed.",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}
