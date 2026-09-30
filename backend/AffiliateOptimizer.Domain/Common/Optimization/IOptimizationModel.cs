namespace AffiliateOptimizer.Domain.Common.Optimization;

/// <summary>
/// A named routing strategy. Models are pure functions of the <see cref="DecisionContext"/>: they hold no
/// state between clicks. The evidence they need is rebuilt from the observation store before every decision,
/// so a model survives a restart, can be replayed over recorded evidence, and is trivially testable.
/// </summary>
public interface IOptimizationModel
{
    /// <summary>Stable identifier used in run provenance and the operator UI, e.g. "epsilon-greedy".</summary>
    string Name { get; }

    /// <summary>Bumped whenever the decision rule changes; stored on every decision so old evidence is not misread.</summary>
    string Version { get; }

    /// <summary>One sentence for the operator.</summary>
    string Description { get; }

    /// <summary>The effective parameters, serialised; stored on the run as provenance.</summary>
    string ParametersJson { get; }

    /// <summary>
    /// Chooses a network for a click, or null when nothing covers its merchant. Null is a real outcome,
    /// not an error: the caller lets such a click lapse rather than spending it on a certain rejection.
    /// </summary>
    Decision? Choose(DecisionContext context);
}

/// <summary>Everything a model may look at. Coverage is already applied to <see cref="EligibleNetworks"/>.</summary>
public sealed record DecisionContext(
    int MerchantId,
    IReadOnlyList<EligibleNetwork> EligibleNetworks,
    IReadOnlyDictionary<string, NetworkEvidence> Evidence,
    DateTime Now,
    IRandomSource Random)
{
    /// <summary>Evidence for a network, or an empty record when this arm has never observed it.</summary>
    public NetworkEvidence EvidenceFor(string networkId)
        => Evidence.TryGetValue(networkId, out var evidence) ? evidence : NetworkEvidence.None(networkId);
}

public sealed record EligibleNetwork(string Id, string Name, decimal AdvertisedRate);

/// <summary>
/// What one arm has seen of one network: all-time and recent-window realized CPC, how many routed clicks are
/// still undisclosed, and how many were rejected. Recent means are what a drifting world is judged by.
/// </summary>
public sealed record NetworkEvidence(
    string NetworkId,
    int SettledCount,
    decimal? MeanCommission,
    int RecentCount,
    decimal? RecentMeanCommission,
    DateTime? LastDisclosedAt,
    int PendingCount,
    int RejectedCount)
{
    public static NetworkEvidence None(string networkId) => new(networkId, 0, null, 0, null, null, 0, 0);
}

/// <summary>The chosen network, why, and the scores every eligible network received (for the operator).</summary>
public sealed record Decision(string NetworkId, string Rationale, IReadOnlyList<NetworkScore> Scores);

public sealed record NetworkScore(string NetworkId, decimal Estimate, string Basis, int Samples);

/// <summary>Source of draws in [0, 1). Random.Shared in production; pinned in tests.</summary>
public interface IRandomSource
{
    double NextDouble();
}

public sealed class SystemRandomSource : IRandomSource
{
    public double NextDouble() => System.Random.Shared.NextDouble();
}

/// <summary>Which models a run uses. Interleaved runs give every model the same traffic, round-robin by click ordinal.</summary>
public sealed record ModelAssignment(IReadOnlyList<string> Names, bool Interleaved)
{
    public string NameForOrdinal(long ordinal)
    {
        if (Names.Count == 0) throw new InvalidOperationException("A run must be assigned at least one optimization model.");
        if (!Interleaved || Names.Count == 1) return Names[0];
        return Names[(int)(ordinal % Names.Count)];
    }
}
