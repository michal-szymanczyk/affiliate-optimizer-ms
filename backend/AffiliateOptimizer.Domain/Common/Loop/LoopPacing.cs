namespace AffiliateOptimizer.Domain.Common.Loop;

/// <summary>
/// Pacing and retry arithmetic, pure (a port of the client's backoff.ts). The click loop waits
/// <see cref="ClickInterval"/> after each turn completes, which keeps the sustained rate just under the
/// 20-per-minute allowance without ever using the burst.
/// </summary>
public static class LoopPacing
{
    public const int ClickRatePerMinute = 20;
    public const int CommissionPollRatePerMinute = 60;

    public static readonly TimeSpan ClickInterval = TimeSpan.FromMilliseconds(60_000 / ClickRatePerMinute);   // 3 s
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);                                    // half the allowance
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(1);                                    // no active run: look again soon
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    /// <summary>The server's page size. Not in the published contract; only decides whether the feed is drained immediately.</summary>
    public const int FeedPageLimit = 100;

    public const int MaxRouteAttempts = 3;

    /// <summary>
    /// How long to wait after the n-th consecutive failure. A server-supplied retryAfterSeconds is honoured
    /// exactly (whole milliseconds, one-second floor, no cap); otherwise 1 s doubling to a 30 s cap.
    /// </summary>
    public static TimeSpan BackoffAfterFailure(int attempt, double? retryAfterSeconds, int baseMs = 1_000)
    {
        if (retryAfterSeconds is { } seconds)
            return TimeSpan.FromMilliseconds(Math.Max(1_000, Math.Ceiling(seconds * 1_000)));

        if (attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "attempt must be at least 1");

        var milliseconds = (long)baseMs << Math.Min(attempt - 1, 20);
        return TimeSpan.FromMilliseconds(Math.Min((long)MaxBackoff.TotalMilliseconds, milliseconds));
    }
}

public enum LoopKind
{
    Click,
    Poll,
}

/// <summary>Codes the loop itself produces, next to the server's error codes.</summary>
public static class LoopEventCodes
{
    public const string Transport = "TRANSPORT";
    public const string RouteUnconfirmed = "ROUTE_UNCONFIRMED";
    public const string ClientError = "CLIENT_ERROR";
    public const string ModelError = "MODEL_ERROR";
    public const string HostRestarted = "HOST_RESTARTED";
}

/// <summary>Liveness of the two loops in this process. The only loop state that is not persisted, because it describes the process.</summary>
public interface ILoopHeartbeat
{
    void Beat(LoopKind loop, string outcome, DateTime at);

    LoopBeat? Last(LoopKind loop);
}

public sealed record LoopBeat(DateTime At, string Outcome);
