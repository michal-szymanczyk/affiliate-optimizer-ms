namespace AffiliateOptimizer.Domain.Common;

/// <summary>The current operator, exposed to the Domain by the web layer (ArchitectureSimple.md §8.2).</summary>
public interface IUserContext
{
    int UserId { get; }
    string Email { get; }
}

/// <summary>Bound from "Evaluation". Thresholds behind the Thin / Stale / Changing flags and the comparison verdict.</summary>
public sealed class EvaluationOptions
{
    public const string SectionName = "Evaluation";

    /// <summary>Window behind every "recent" figure, and the default evidence window of a new run.</summary>
    public int RecentWindowMinutes { get; set; } = 10;

    /// <summary>A network with no disclosure for this long, while still eligible, is flagged stale.</summary>
    public int StaleAfterMinutes { get; set; } = 5;

    /// <summary>Fewer settled clicks than this is flagged thin.</summary>
    public int ThinBelow { get; set; } = 10;

    /// <summary>A comparison stays inconclusive until every arm has at least this many settled clicks.</summary>
    public int MinSamplesPerArm { get; set; } = 30;
}

/// <summary>Bound from "Loop".</summary>
public sealed class LoopOptions
{
    public const string SectionName = "Loop";

    /// <summary>Continue a run that was running or paused when the host last stopped. Off: stop it with an event.</summary>
    public bool ResumeActiveRunOnStartup { get; set; } = true;

    /// <summary>Model name to start a run with at boot when no run is active (unattended operation). Null: wait for the operator.</summary>
    public string? AutoStartModel { get; set; }
}
