using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Retry policy for the Snipe-IT HTTP client. Snipe-IT rate-limits aggressively (HTTP 429),
/// and a throttled response is NOT the same as "user not found" — without retries, a 429 storm
/// makes every lookup look like a miss, which then floods the reconciliation queue and hammers
/// Snipe-IT harder. This backs off on 429 / 5xx / transient network errors, honoring the
/// server's Retry-After header when present, and only lets a genuine failure surface after the
/// retries are exhausted (callers treat that as a lookup failure, never as a definitive miss).
/// </summary>
public static class SnipeItRetryPolicy
{
    private const int RetryCount = 5;

    public static IAsyncPolicy<HttpResponseMessage> Create(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()                       // 5xx and 408
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests) // 429
            .WaitAndRetryAsync(
                RetryCount,
                sleepDurationProvider: (attempt, outcome, _) => DelayFor(attempt, outcome),
                onRetryAsync: (outcome, delay, attempt, _) =>
                {
                    var reason = outcome.Result is not null
                        ? $"HTTP {(int)outcome.Result.StatusCode}"
                        : outcome.Exception?.GetType().Name ?? "error";
                    logger.LogWarning(
                        "Snipe-IT request throttled/failed ({Reason}); retry {Attempt}/{Max} in {Delay:n1}s.",
                        reason, attempt, RetryCount, delay.TotalSeconds);
                    return Task.CompletedTask;
                });
    }

    private static TimeSpan DelayFor(int attempt, DelegateResult<HttpResponseMessage> outcome)
    {
        // Prefer the server's Retry-After header (seconds or an HTTP date) if it sent one.
        var retryAfter = outcome.Result?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
                return until;
        }

        // Otherwise exponential backoff (2s, 4s, 8s, …) with a little jitter to avoid a thundering herd.
        var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
        return backoff + jitter;
    }
}
