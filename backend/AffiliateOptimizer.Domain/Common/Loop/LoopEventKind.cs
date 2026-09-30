namespace AffiliateOptimizer.Domain.Common.Loop;

/// <summary>What sort of thing went wrong (or, for Info, happened) in a loop. Drives the operator's fault localisation.</summary>
public enum LoopEventKind
{
    /// <summary>The server answered with an error code (rate limit, bad credentials, unknown click…).</summary>
    Api = 1,

    /// <summary>The server could not be reached, or did not answer in time.</summary>
    Transport = 2,

    /// <summary>A route request's outcome is unknown; the feed will reconcile it.</summary>
    Unconfirmed = 3,

    /// <summary>A failure inside this application, not the server's.</summary>
    ClientError = 4,

    /// <summary>An optimization model threw while choosing.</summary>
    ModelError = 5,

    /// <summary>Not a fault: host restarted, run resumed, and similar.</summary>
    Info = 6,
}
