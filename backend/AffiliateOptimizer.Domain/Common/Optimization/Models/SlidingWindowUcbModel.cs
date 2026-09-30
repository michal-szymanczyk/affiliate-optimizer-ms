using static System.FormattableString;
using System.Text.Json;

namespace AffiliateOptimizer.Domain.Common.Optimization.Models;

/// <summary>Bound from "Models:sliding-window-ucb".</summary>
public sealed class SlidingWindowUcbOptions
{
    /// <summary>Exploration weight. Commissions are cents per click, so the bonus is scaled to that magnitude.</summary>
    public decimal C { get; set; } = 0.05m;
}

/// <summary>
/// Upper-confidence-bound over the recent window only: score = recent mean + C·√(ln(N) / n), where n is the
/// network's recent observations and N the arm's total recent observations. A network with no recent evidence
/// is tried first (forced exploration), which is how the model keeps re-checking networks whose behaviour may
/// have changed since the window moved past them.
/// </summary>
public sealed class SlidingWindowUcbModel : IOptimizationModel
{
    private readonly SlidingWindowUcbOptions _options;

    public SlidingWindowUcbModel(SlidingWindowUcbOptions options)
    {
        if (options.C < 0) throw new ArgumentOutOfRangeException(nameof(options), options.C, "C must not be negative.");
        _options = options;
    }

    public string Name => "sliding-window-ucb";
    public string Version => "1";
    public string Description => "Upper confidence bound over the recent evidence window; re-explores networks that left the window.";
    public string ParametersJson => JsonSerializer.Serialize(new { c = _options.C });

    public Decision? Choose(DecisionContext context)
    {
        var eligible = context.EligibleNetworks;
        if (eligible.Count == 0) return null;

        var evidence = eligible.Select(n => (Network: n, Evidence: context.EvidenceFor(n.Id))).ToList();
        var totalRecent = evidence.Sum(e => e.Evidence.RecentCount);

        var unobserved = evidence
            .Where(e => e.Evidence.RecentCount == 0 || e.Evidence.RecentMeanCommission is null)
            .OrderBy(e => e.Network.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var scores = evidence.Select(e =>
        {
            if (e.Evidence.RecentCount == 0 || e.Evidence.RecentMeanCommission is null)
                return new NetworkScore(e.Network.Id, e.Network.AdvertisedRate, "unobserved", 0);

            var bonus = _options.C * (decimal)Math.Sqrt(Math.Log(totalRecent + 1) / e.Evidence.RecentCount);
            return new NetworkScore(e.Network.Id, e.Evidence.RecentMeanCommission.Value + bonus, "ucb", e.Evidence.RecentCount);
        }).ToList();

        if (unobserved.Network is not null)
            return new Decision(unobserved.Network.Id, $"forced exploration: {unobserved.Network.Id} has no evidence in the recent window", scores);

        var best = scores
            .OrderByDescending(s => s.Estimate)
            .ThenBy(s => s.NetworkId, StringComparer.Ordinal)
            .First();

        return new Decision(best.NetworkId, Invariant($"ucb: {best.NetworkId} bound {best.Estimate:0.0000} (n={best.Samples} of {totalRecent} recent)"), scores);
    }
}
