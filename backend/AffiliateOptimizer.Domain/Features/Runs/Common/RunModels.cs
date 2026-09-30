using AffiliateOptimizer.Domain.Common.Optimization;

namespace AffiliateOptimizer.Domain.Features.Runs.Common;

public enum RunStatus
{
    /// <summary>Clicks are being issued and routed.</summary>
    Running = 1,

    /// <summary>No clicks are issued; commissions keep being polled so in-flight clicks still resolve.</summary>
    Paused = 2,

    /// <summary>Final. A new run must be started.</summary>
    Stopped = 3,
}

/// <summary>The run the loops act on. Read model shared by the run slices and the loop slices.</summary>
public sealed record ActiveRun(
    int Id,
    RunStatus Status,
    ModelAssignment Assignment,
    int EvidenceWindowMinutes,
    int ConsecutiveClickFailures,
    DateTime StartedAt,
    DateTime? PausedAt,
    string CredentialFingerprint,
    string AppVersion)
{
    public TimeSpan EvidenceWindow => TimeSpan.FromMinutes(EvidenceWindowMinutes);
}
