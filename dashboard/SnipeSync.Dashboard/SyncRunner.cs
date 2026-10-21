using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SnipeITSyncFormerEmployees;

namespace SnipeSync.Dashboard;

/// <summary>
/// Runs the full sync on demand, replacing the Azure timers: offboard → reconcile the unmatched →
/// onboard/rehires → license seats, in that order so they never hit Snipe-IT's rate limit at once
/// (the same reason the timers were spaced an hour apart).
///
/// One run at a time. Within this process a flag guards it; across processes (the Sync button and a
/// nightly <c>--sync</c> scheduled task) an exclusive lock file does, so two runs can never interleave
/// writes against the same Snipe-IT users.
///
/// Each run builds its own service provider from freshly re-read settings, so a settings.json edit and
/// the per-run dry-run choice both apply without restarting the dashboard.
/// </summary>
public sealed class SyncRunner(ILogger<SyncRunner> logger)
{
    private readonly object _gate = new();
    private SyncRunState? _current;
    private SyncRunState? _last;

    /// <summary>The run in progress, else the most recent finished one, else null.</summary>
    public SyncRunState? Snapshot
    {
        get { lock (_gate) return _current ?? _last; }
    }

    public bool IsRunning
    {
        get { lock (_gate) return _current is not null; }
    }

    /// <summary>
    /// Starts a run in the background. Returns null when one is already running here. A run already
    /// going in another process surfaces as a failed run whose message says so.
    /// </summary>
    public SyncRunState? TryStart(bool dryRun)
    {
        var state = TryClaim(dryRun);
        if (state is not null)
            _ = Task.Run(() => ExecuteAsync(state));
        return state;
    }

    /// <summary>Runs to completion on the caller (the <c>--sync</c> command line).</summary>
    public async Task<SyncRunState> RunToCompletionAsync(bool dryRun)
    {
        var state = TryClaim(dryRun) ?? throw new InvalidOperationException("A sync is already running.");
        await ExecuteAsync(state);
        return state;
    }

    private SyncRunState? TryClaim(bool dryRun)
    {
        lock (_gate)
        {
            if (_current is not null) return null;
            return _current = new SyncRunState(dryRun);
        }
    }

    private async Task ExecuteAsync(SyncRunState state)
    {
        try
        {
            await RunAsync(state);
        }
        finally
        {
            lock (_gate)
            {
                _last = state;
                _current = null;
            }
        }
    }

    private async Task RunAsync(SyncRunState state)
    {
        var lockPath = Path.Combine(LocalSettings.DataDirectory, "sync.lock");
        FileStream? lockFile = null;
        try
        {
            Directory.CreateDirectory(LocalSettings.DataDirectory);
            try
            {
                lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                state.Fail("Another SnipeSync sync is already running (the nightly task, or a second window).");
                return;
            }

            var missing = LocalSettings.Apply();
            if (missing.Count > 0)
            {
                state.Fail($"Missing settings: {string.Join(", ", missing)}. Add them to {LocalSettings.SettingsPath}.");
                return;
            }

            // A run is dry if the button asked for it OR settings.json has DRY_RUN=true, so a global
            // safety switch in the settings can't be overridden by an unchecked box.
            var options = state.RequestedDryRun ? new SyncOptions { DryRun = true } : new SyncOptions();
            state.DryRun = options.DryRun;

            await using var provider = BuildProvider(options, state);
            AuditRunContext.RunId = state.RunId;

            logger.LogInformation("Sync {RunId} started{DryRun}.", state.RunId, options.DryRun ? " [DRY-RUN]" : "");

            var queue = (InMemoryReconciliationQueue)provider.GetRequiredService<IReconciliationQueue>();

            await state.RunStepAsync(SyncStep.Offboard,
                async () => await provider.GetRequiredService<FormerEmployeeSyncJob>().RunAsync());

            await state.RunStepAsync(SyncStep.Reconcile, async Task<SyncRunSummary?> () =>
            {
                var job = provider.GetRequiredService<ReconciliationJob>();
                var summary = new SyncRunSummary(ReconciliationJob.Name) { DryRun = options.DryRun };
                foreach (var user in queue.Drain())
                {
                    summary.Processed++;
                    if (await job.ReconcileAsync(user, summary) == OffboardOutcome.NotMatched)
                        summary.Unmatched.Add(user.DisplayName);
                }
                return summary;
            });

            await state.RunStepAsync(SyncStep.Onboard,
                async () => await provider.GetRequiredService<OnboardEmployeeSyncJob>().RunAsync());

            await state.RunStepAsync(SyncStep.Licenses,
                () => provider.GetRequiredService<LicenseSyncJob>().RunAsync());

            state.Finish();
            logger.LogInformation("Sync {RunId} finished: {Outcome}.", state.RunId, state.Outcome);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Sync {RunId} crashed.", state.RunId);
            state.Fail(e.Message);
        }
        finally
        {
            AuditRunContext.RunId = null;
            lockFile?.Dispose();
        }
    }

    private static ServiceProvider BuildProvider(SyncOptions options, SyncRunState state)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            // HttpClient logs every request at Information; the run log only wants the sync's own lines.
            b.AddFilter("System.Net.Http", LogLevel.Warning);
            b.AddProvider(new RunLogProvider(state));
        });
        services.AddSnipeSyncCore(options);
        services.AddSingleton(new SqliteAuditDb(LocalSettings.AuditDbPath));
        services.AddSingleton<IAuditService, SqliteAuditService>();
        services.AddSingleton<IReconciliationQueue, InMemoryReconciliationQueue>();
        return services.BuildServiceProvider();
    }

    /// <summary>Feeds a run's log lines into its state so the dashboard can show them live.</summary>
    private sealed class RunLogProvider(SyncRunState state) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RunLogger(state);
        public void Dispose() { }

        private sealed class RunLogger(SyncRunState state) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState s) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;

            public void Log<TState>(LogLevel level, EventId id, TState s, Exception? e, Func<TState, Exception?, string> format)
            {
                if (IsEnabled(level)) state.AddLog(level, format(s, e));
            }
        }
    }
}

/// <summary>
/// The local stand-in for the Azure Storage queue: FormerEmployeeSyncJob enqueues users it couldn't
/// match, and the runner reconciles them straight after, in the same run.
/// </summary>
public sealed class InMemoryReconciliationQueue : IReconciliationQueue
{
    private readonly ConcurrentQueue<UnmatchedUser> _queue = new();

    public Task EnqueueAsync(UnmatchedUser user)
    {
        _queue.Enqueue(user);
        return Task.CompletedTask;
    }

    public IEnumerable<UnmatchedUser> Drain()
    {
        while (_queue.TryDequeue(out var user))
            yield return user;
    }
}

public enum SyncStep { Offboard, Reconcile, Onboard, Licenses }

/// <summary>Live state of one sync run, serialized as-is by GET /api/sync/status.</summary>
public sealed class SyncRunState(bool requestedDryRun)
{
    private const int MaxLogLines = 300;
    private readonly object _gate = new();
    private readonly List<SyncLogLine> _log = [];

    [JsonPropertyName("runId")] public string RunId { get; } = Guid.NewGuid().ToString("N")[..12];
    [JsonPropertyName("requestedDryRun")] public bool RequestedDryRun { get; } = requestedDryRun;
    [JsonPropertyName("dryRun")] public bool DryRun { get; set; } = requestedDryRun;
    [JsonPropertyName("startedAt")] public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("finishedAt")] public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>"running", "succeeded", "completedWithErrors" or "failed".</summary>
    [JsonPropertyName("outcome")] public string Outcome { get; private set; } = "running";
    [JsonPropertyName("error")] public string? Error { get; private set; }

    [JsonPropertyName("steps")]
    public List<SyncStepState> Steps { get; } = Enum.GetValues<SyncStep>().Select(s => new SyncStepState(s)).ToList();

    [JsonPropertyName("log")]
    public IReadOnlyList<SyncLogLine> Log
    {
        get { lock (_gate) return _log.ToList(); }
    }

    internal void AddLog(LogLevel level, string message)
    {
        lock (_gate)
        {
            _log.Add(new SyncLogLine(DateTimeOffset.UtcNow, level.ToString(), message));
            if (_log.Count > MaxLogLines) _log.RemoveAt(0);
        }
    }

    internal async Task RunStepAsync(SyncStep step, Func<Task<SyncRunSummary?>> run)
    {
        var s = Steps[(int)step];
        s.State = "running";
        s.StartedAt = DateTimeOffset.UtcNow;
        try
        {
            var summary = await run();
            s.State = summary is null ? "skipped" : "done";
            if (summary is not null) s.Counts = SyncStepCounts.From(summary);
        }
        catch (Exception e)
        {
            // One step failing (say, Graph refusing the license query) shouldn't stop the others.
            s.State = "failed";
            s.Error = e.Message;
            AddLog(LogLevel.Error, $"{step} failed: {e.Message}");
        }
        s.FinishedAt = DateTimeOffset.UtcNow;
    }

    internal void Finish()
    {
        FinishedAt = DateTimeOffset.UtcNow;
        var anyProblem = Steps.Any(s => s.State == "failed" || (s.Counts?.Failed ?? 0) > 0);
        Outcome = anyProblem ? "completedWithErrors" : "succeeded";
    }

    internal void Fail(string message)
    {
        Error = message;
        FinishedAt = DateTimeOffset.UtcNow;
        Outcome = "failed";
        foreach (var s in Steps.Where(s => s.State is "pending" or "running"))
            s.State = "skipped";
        AddLog(LogLevel.Error, message);
    }
}

public sealed class SyncStepState(SyncStep step)
{
    [JsonPropertyName("step")] public string Step { get; } = step.ToString().ToLowerInvariant();

    /// <summary>"pending", "running", "done", "skipped" or "failed".</summary>
    [JsonPropertyName("state")] public string State { get; set; } = "pending";
    [JsonPropertyName("startedAt")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("finishedAt")] public DateTimeOffset? FinishedAt { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("counts")] public SyncStepCounts? Counts { get; set; }
}

/// <summary>The parts of <see cref="SyncRunSummary"/> worth showing on the dashboard.</summary>
public sealed record SyncStepCounts(
    [property: JsonPropertyName("processed")] int Processed,
    [property: JsonPropertyName("offboarded")] int Offboarded,
    [property: JsonPropertyName("onboarded")] int Onboarded,
    [property: JsonPropertyName("rehired")] int Rehired,
    [property: JsonPropertyName("assetsCheckedIn")] int AssetsCheckedIn,
    [property: JsonPropertyName("licensesReclaimed")] int LicensesReclaimed,
    [property: JsonPropertyName("licensesCheckedOut")] int LicensesCheckedOut,
    [property: JsonPropertyName("accessoriesReclaimed")] int AccessoriesReclaimed,
    [property: JsonPropertyName("alreadyCurrent")] int AlreadyCurrent,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("unmatched")] IReadOnlyList<string> Unmatched,
    [property: JsonPropertyName("needsAttention")] IReadOnlyList<string> NeedsAttention)
{
    public static SyncStepCounts From(SyncRunSummary s) => new(
        s.Processed, s.Offboarded, s.Onboarded, s.Reactivated, s.AssetsCheckedIn, s.LicensesReclaimed,
        s.LicensesCheckedOut, s.AccessoriesReclaimed, s.AlreadyCurrent, s.Failed,
        s.Unmatched.ToList(), s.AssetsNeedingAttention.Concat(s.SeatsExhausted).ToList());
}

public sealed record SyncLogLine(
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("message")] string Message);
