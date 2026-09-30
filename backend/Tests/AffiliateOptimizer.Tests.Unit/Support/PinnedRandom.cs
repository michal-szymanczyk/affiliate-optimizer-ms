using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Optimization;

namespace AffiliateOptimizer.Tests.Unit.Support;

/// <summary>Returns the given draws in order and repeats the last one, so a test can pin every choice a model makes.</summary>
public sealed class PinnedRandom(params double[] draws) : IRandomSource
{
    private int _next;

    public double NextDouble() => draws[Math.Min(_next++, draws.Length - 1)];
}

public static class Fixtures
{
    public static readonly DateTime T0 = new(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);   // 1_700_000_000_000 ms since the epoch

    /// <summary>The fixture of the original random.test.ts: net-a advertises 0.4 for merchants 1..3, net-b 0.6 for 3..4, net-c 0.2 for 9.</summary>
    public static readonly IReadOnlyList<Network> Networks =
    [
        new("net-a", "Network A", 0.4m, [1, 2, 3]),
        new("net-b", "Network B", 0.6m, [3, 4]),
        new("net-c", "Network C", 0.2m, [9]),
    ];

    public static DecisionContext Context(int merchantId, IRandomSource? random = null, params NetworkEvidence[] evidence)
        => new(merchantId, Eligibility.Of(merchantId, Networks), evidence.ToDictionary(e => e.NetworkId), T0, random ?? new PinnedRandom(0.5));

    public static NetworkEvidence Evidence(string networkId, int settled = 0, decimal? mean = null, int recent = 0, decimal? recentMean = null,
        int pending = 0, int rejected = 0, DateTime? lastDisclosedAt = null)
        => new(networkId, settled, mean, recent, recentMean, lastDisclosedAt, pending, rejected);
}
