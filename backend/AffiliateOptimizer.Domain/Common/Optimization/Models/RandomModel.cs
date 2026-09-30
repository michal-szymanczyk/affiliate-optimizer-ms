namespace AffiliateOptimizer.Domain.Common.Optimization.Models;

/// <summary>
/// The control: uniform among the networks that cover the merchant. A port of the shipped random model.
/// Eligibility is the one thing it gets right; everything else is left to chance, which is exactly why it
/// is the baseline every other model is compared against.
/// </summary>
public sealed class RandomModel : IOptimizationModel
{
    public string Name => "random";
    public string Version => "1";
    public string Description => "Uniform choice among the networks covering the merchant. The control.";
    public string ParametersJson => "{}";

    public Decision? Choose(DecisionContext context)
    {
        var eligible = context.EligibleNetworks;
        if (eligible.Count == 0) return null;

        var draw = context.Random.NextDouble();
        var index = (int)Math.Floor(draw * eligible.Count);
        if (index < 0 || index >= eligible.Count)
            throw new InvalidOperationException(
                $"random model: drew an index outside {eligible.Count} eligible networks. The random source must return a value in [0, 1).");

        return new Decision(eligible[index].Id, $"random among {eligible.Count} networks covering merchant {context.MerchantId}", []);
    }
}
