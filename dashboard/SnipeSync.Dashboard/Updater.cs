using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SnipeSync.Dashboard;

/// <summary>
/// Self-update from GitHub Releases. installer\release.cmd publishes each build as a release tagged
/// v&lt;version&gt; with SnipeSync-Setup-&lt;version&gt;.exe attached; this checks the latest release
/// against the running version, and on request downloads that installer and runs it silently.
///
/// The installer is per-user (no UAC), so an update needs no admin. It's launched with /RELAUNCH, which
/// the .iss turns into starting the new exe with --after-update once the files are replaced; this
/// process exits first so nothing holds the old exe open.
/// </summary>
public sealed class Updater(IOptions<DashboardOptions> options, ILogger<Updater> logger)
{
    /// <summary>A failed or unanswered check is retried on the next status request after this long.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>The running version, stamped by build.cmd's -p:Version. Declared before Http, whose
    /// User-Agent reads it: static initializers run in textual order.</summary>
    public static Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _gate = new();
    private UpdateStatus _status = new(CurrentVersion.ToString(3));
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private bool _installing;

    public UpdateStatus Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>Checks GitHub unless a check ran recently (or <paramref name="force"/>). Never throws.</summary>
    public async Task<UpdateStatus> CheckAsync(bool force, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.UpdateRepository))
            return Status;

        await _checkGate.WaitAsync(ct);
        try
        {
            if (!force && DateTimeOffset.UtcNow - _lastCheck < CheckInterval)
                return Status;
            _lastCheck = DateTimeOffset.UtcNow;

            var release = await FetchLatestAsync(ct);
            var next = release is null
                ? Status with { Error = null, CheckedAt = DateTimeOffset.UtcNow }
                : new UpdateStatus(CurrentVersion.ToString(3))
                {
                    Latest = release.Version.ToString(3),
                    Available = release.Version > CurrentVersion && release.Installer is not null,
                    Notes = release.Notes,
                    ReleaseUrl = release.HtmlUrl,
                    CheckedAt = DateTimeOffset.UtcNow
                };
            lock (_gate) _status = next with { Installing = _installing };
            return Status;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Offline, proxied, or rate-limited: the dashboard works fine without knowing.
            logger.LogInformation("Update check failed: {Message}", e.Message);
            lock (_gate) _status = _status with { Error = e.Message, CheckedAt = DateTimeOffset.UtcNow };
            return Status;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    /// <summary>
    /// Downloads the latest installer, verifies it, starts it, and returns. The caller then shuts this
    /// process down so the installer can replace the exe. Throws InvalidOperationException with a
    /// sentence for the dashboard when there's nothing to install or the download doesn't check out.
    /// </summary>
    public async Task InstallAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_installing) throw new InvalidOperationException("An update is already being installed.");
            _installing = true;
            _status = _status with { Installing = true };
        }

        try
        {
            var release = await FetchLatestAsync(ct)
                ?? throw new InvalidOperationException("No release was found to update to.");
            if (release.Version <= CurrentVersion || release.Installer is not { } asset)
                throw new InvalidOperationException($"SnipeSync {CurrentVersion.ToString(3)} is already the latest.");

            var dir = Path.Combine(LocalSettings.DataDirectory, "updates");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.EnumerateFiles(dir, "*.exe"))
                TryDelete(old);
            var path = Path.Combine(dir, asset.Name);

            logger.LogInformation("Downloading {Asset} from {Url}", asset.Name, asset.BrowserDownloadUrl);
            await using (var file = File.Create(path))
            await using (var body = await Http.GetStreamAsync(asset.BrowserDownloadUrl, ct))
                await body.CopyToAsync(file, ct);

            if (new FileInfo(path).Length != asset.Size)
            {
                TryDelete(path);
                throw new InvalidOperationException("The update download was incomplete. Try again.");
            }

            // GitHub records a SHA-256 for every uploaded asset; refuse a file that doesn't match it.
            if (asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = File.OpenRead(path);
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
                if (!string.Equals(actual, digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase))
                {
                    stream.Close();
                    TryDelete(path);
                    throw new InvalidOperationException("The update download failed its checksum. Try again.");
                }
            }

            // /SILENT shows only a progress bar; tasks chosen at first install (desktop icon, nightly sync)
            // are remembered. /RELAUNCH is ours: see ShouldRelaunch in SnipeSync.iss.
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            lock (_gate)
            {
                _installing = false;
                _status = _status with { Installing = false };
            }
            throw;
        }
    }

    private async Task<Release?> FetchLatestAsync(CancellationToken ct)
    {
        // /releases/latest skips drafts and pre-releases, so a pre-release can be tried by hand first.
        using var res = await Http.GetAsync(
            $"https://api.github.com/repos/{options.Value.UpdateRepository}/releases/latest", ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // no releases published yet
        res.EnsureSuccessStatusCode();

        var dto = await res.Content.ReadFromJsonAsync<GitHubRelease>(ct);
        if (dto is null || !Version.TryParse(dto.TagName.TrimStart('v', 'V'), out var version))
            return null;

        // Only an installer named for this release, served by GitHub, is ever downloaded.
        var expected = $"SnipeSync-Setup-{dto.TagName.TrimStart('v', 'V')}.exe";
        var installer = dto.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, expected, StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(a.BrowserDownloadUrl, UriKind.Absolute, out var u)
            && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com");

        return new Release(Normalize(version), dto.Body, dto.HtmlUrl, installer);
    }

    /// <summary>"1.2" and "1.2.0.0" compare equal to "1.2.0".</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SnipeSync", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed record Release(Version Version, string? Notes, string? HtmlUrl, GitHubAsset? Installer);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}

/// <summary>GET /api/update. Mirrored by UpdateStatus in the SPA's api.ts.</summary>
public sealed record UpdateStatus(string Current)
{
    public string? Latest { get; init; }
    public bool Available { get; init; }
    public string? Notes { get; init; }
    public string? ReleaseUrl { get; init; }
    public bool Installing { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
}
