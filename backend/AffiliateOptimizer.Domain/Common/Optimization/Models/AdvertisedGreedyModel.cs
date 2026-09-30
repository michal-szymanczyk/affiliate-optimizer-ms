using static System.FormattableString;
namespace AffiliateOptimizer.Domain.Common.Optimization.Models;

/// <summary>
/// Trusts the claim: always the covering network with the highest advertised CPC. Ties go to the lowest id.
/// Deliberately naive in the opposite direction from random: it never learns, so the gap between what it
/// expects and what it earns is a direct measure of how honest the advertised rates are.
/// </summary>
public sealed class AdvertisedGreedyModel : IOptimizationModel
{
    public string Name => "advertised-greedy";
    public string Version => "1";
    public string Description => "Highest advertised CPC among covering networks. Never learns; measures how honest the claims are.";
    public string ParametersJson => "{}";

    public Decision? Choose(DecisionContext context)
    {
        if (context.EligibleNetworks.Count == 0) return null;

        var scores = context.EligibleNetworks
            .Select(n => new NetworkScore(n.Id, n.AdvertisedRate, "advertised", 0))
            .ToList();

        var best = scores
            .OrderByDescending(s => s.Estimate)
            .ThenBy(s => s.NetworkId, StringComparer.Ordinal)
            .First();

        return new Decision(best.NetworkId, Invariant($"highest advertised CPC {best.Estimate:0.0000} among {scores.Count} eligible"), scores);
    }
}
