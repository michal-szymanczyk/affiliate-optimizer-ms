using static System.FormattableString;
using System.Text.Json;

namespace AffiliateOptimizer.Domain.Common.Optimization.Models;

/// <summary>Bound from "Models:epsilon-greedy". Validated in the model constructor.</summary>
public sealed class EpsilonGreedyOptions
{
    /// <summary>Probability of exploring (a uniform draw among eligible networks) instead of exploiting.</summary>
    public double Epsilon { get; set; } = 0.10;

    /// <summary>Observations a network needs before its own mean is trusted over the advertised prior.</summary>
    public int MinSamples { get; set; } = 3;
}

/// <summary>
/// Explore with probability ε, otherwise pick the network with the best estimate. The estimate prefers the
/// recent window (so drift is followed), falls back to the all-time mean, and for an unseen network uses the
/// advertised rate as an optimistic prior so that every network gets tried at least once.
/// </summary>
public sealed class EpsilonGreedyModel : IOptimizationModel
{
    private readonly EpsilonGreedyOptions _options;

    public EpsilonGreedyModel(EpsilonGreedyOptions options)
    {
        if (options.Epsilon is < 0 or > 1 || double.IsNaN(options.Epsilon))
            throw new ArgumentOutOfRangeException(nameof(options), options.Epsilon, "Epsilon must be within [0, 1].");
        if (options.MinSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.MinSamples, "MinSamples must be at least 1.");
        _options = options;
    }

    public string Name => "epsilon-greedy";
    public string Version => "1";
    public string Description => "Explores with probability ε, otherwise exploits the best recent realized CPC (advertised rate as prior for unseen networks).";
    public string ParametersJson => JsonSerializer.Serialize(new { epsilon = _options.Epsilon, minSamples = _options.MinSamples });

    public Decision? Choose(DecisionContext context)
    {
        var eligible = context.EligibleNetworks;
        if (eligible.Count == 0) return null;

        var scores = eligible.Select(n => Score(n, context.EvidenceFor(n.Id))).ToList();

        if (context.Random.NextDouble() < _options.Epsilon)
        {
            var index = (int)Math.Floor(context.Random.NextDouble() * eligible.Count);
            index = Math.Clamp(index, 0, eligible.Count - 1);
            var pick = eligible[index];
            return new Decision(pick.Id, Invariant($"explore (ε={_options.Epsilon:0.##}): uniform among {eligible.Count} eligible"), scores);
        }

        var best = scores
            .OrderByDescending(s => s.Estimate)
            .ThenBy(s => s.NetworkId, StringComparer.Ordinal)
            .First();

        return new Decision(best.NetworkId, Invariant($"exploit: {best.NetworkId} est {best.Estimate:0.0000} ({best.Basis}, n={best.Samples})"), scores);
    }

    private NetworkScore Score(EligibleNetwork network, NetworkEvidence evidence)
    {
        if (evidence.RecentCount >= _options.MinSamples && evidence.RecentMeanCommission is { } recent)
            return new NetworkScore(network.Id, recent, "recent", evidence.RecentCount);

        if (evidence.SettledCount >= _options.MinSamples && evidence.MeanCommission is { } all)
            return new NetworkScore(network.Id, all, "all-time", evidence.SettledCount);

        // Optimistic prior: an unseen network is worth what it claims until proven otherwise.
        return new NetworkScore(network.Id, network.AdvertisedRate, "advertised-prior", evidence.SettledCount);
    }
}
