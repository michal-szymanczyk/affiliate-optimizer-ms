using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Common.Optimization.Models;
using AffiliateOptimizer.Tests.Unit.Support;

namespace AffiliateOptimizer.Tests.Unit.Domain.Common.Optimization;

public sealed class EligibilityTests
{
    [Fact]
    public void Of_KeepsOnlyCoveringNetworksInIdOrder()
    {
        Assert.Equal(["net-a", "net-b"], Eligibility.Of(3, Fixtures.Networks).Select(n => n.Id));
        Assert.Equal(["net-a"], Eligibility.Of(1, Fixtures.Networks).Select(n => n.Id));
        Assert.Empty(Eligibility.Of(99, Fixtures.Networks));
    }
}

public sealed class RandomModelTests
{
    private readonly RandomModel _model = new();

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(0.999999)]
    public void Choose_CoveredMerchant_StaysInsideEligibleNetworks(double draw)
    {
        var decision = _model.Choose(Fixtures.Context(3, new PinnedRandom(draw)));

        Assert.NotNull(decision);
        Assert.Contains(decision.NetworkId, new[] { "net-a", "net-b" });
    }

    [Fact]
    public void Choose_NothingCovers_ReturnsNull()
    {
        Assert.Null(_model.Choose(Fixtures.Context(77)));
        Assert.Null(_model.Choose(new DecisionContext(3, [], new Dictionary<string, NetworkEvidence>(), Fixtures.T0, new PinnedRandom(0))));
    }

    [Fact]
    public void Choose_MapsTheDrawOntoTheEligibleListInOrder()
    {
        Assert.Equal("net-a", _model.Choose(Fixtures.Context(3, new PinnedRandom(0)))!.NetworkId);
        Assert.Equal("net-b", _model.Choose(Fixtures.Context(3, new PinnedRandom(0.999999)))!.NetworkId);
    }

    [Fact]
    public void Choose_IgnoresTheAdvertisedRate()
    {
        // net-b advertises 0.6 against net-a's 0.4; the draw alone decides.
        Assert.Equal("net-a", _model.Choose(Fixtures.Context(3, new PinnedRandom(0.1)))!.NetworkId);
    }

    [Fact]
    public void Choose_ExplainsItself()
        => Assert.Matches("random among 2 networks", _model.Choose(Fixtures.Context(3))!.Rationale);

    [Fact]
    public void Choose_DrawOutsideUnitInterval_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _model.Choose(Fixtures.Context(3, new PinnedRandom(1.5))));
        Assert.Contains("[0, 1)", error.Message);
    }
}

public sealed class AdvertisedGreedyModelTests
{
    private readonly AdvertisedGreedyModel _model = new();

    [Fact]
    public void Choose_PicksTheHighestAdvertisedRate()
    {
        var decision = _model.Choose(Fixtures.Context(3))!;

        Assert.Equal("net-b", decision.NetworkId);
        Assert.Equal(2, decision.Scores.Count);
        Assert.All(decision.Scores, s => Assert.Equal("advertised", s.Basis));
    }

    [Fact]
    public void Choose_TiesGoToTheLowestId()
    {
        var context = new DecisionContext(1, [new("net-z", "Z", 0.5m), new("net-m", "M", 0.5m)], new Dictionary<string, NetworkEvidence>(), Fixtures.T0, new PinnedRandom(0));

        Assert.Equal("net-m", _model.Choose(context)!.NetworkId);
    }

    [Fact]
    public void Choose_NothingCovers_ReturnsNull() => Assert.Null(_model.Choose(Fixtures.Context(77)));
}

public sealed class EpsilonGreedyModelTests
{
    private static EpsilonGreedyModel Model(double epsilon = 0.1, int minSamples = 3) => new(new EpsilonGreedyOptions { Epsilon = epsilon, MinSamples = minSamples });

    [Fact]
    public void Choose_NoEvidence_ExploitsTheAdvertisedPrior()
    {
        var decision = Model().Choose(Fixtures.Context(3, new PinnedRandom(0.5)))!;

        Assert.Equal("net-b", decision.NetworkId);
        Assert.StartsWith("exploit", decision.Rationale);
        Assert.All(decision.Scores, s => Assert.Equal("advertised-prior", s.Basis));
    }

    [Fact]
    public void Choose_DrawBelowEpsilon_ExploresUniformly()
    {
        var decision = Model().Choose(Fixtures.Context(3, new PinnedRandom(0.05, 0.0)))!;

        Assert.Equal("net-a", decision.NetworkId);   // the lower prior, chosen only because it explored
        Assert.StartsWith("explore", decision.Rationale);
    }

    [Fact]
    public void Choose_EnoughRecentEvidence_PrefersTheRecentMean()
    {
        var evidence = Fixtures.Evidence("net-a", settled: 3, mean: 0.9m, recent: 3, recentMean: 0.9m);
        var decision = Model().Choose(Fixtures.Context(3, new PinnedRandom(0.5), evidence))!;

        Assert.Equal("net-a", decision.NetworkId);
        Assert.Equal("recent", decision.Scores.Single(s => s.NetworkId == "net-a").Basis);
    }

    [Fact]
    public void Choose_ThinRecentEvidence_FallsBackToAllTimeMean()
    {
        var evidence = Fixtures.Evidence("net-a", settled: 5, mean: 0.7m, recent: 1, recentMean: 0.1m);
        var decision = Model().Choose(Fixtures.Context(3, new PinnedRandom(0.5), evidence))!;

        Assert.Equal("net-a", decision.NetworkId);
        var score = decision.Scores.Single(s => s.NetworkId == "net-a");
        Assert.Equal("all-time", score.Basis);
        Assert.Equal(0.7m, score.Estimate);
    }

    [Fact]
    public void Choose_EqualEstimates_TiesGoToTheLowestId()
    {
        var context = new DecisionContext(1, [new("net-z", "Z", 0.5m), new("net-m", "M", 0.5m)], new Dictionary<string, NetworkEvidence>(), Fixtures.T0, new PinnedRandom(0.5));

        Assert.Equal("net-m", Model().Choose(context)!.NetworkId);
    }

    [Fact]
    public void Choose_NothingCovers_ReturnsNull() => Assert.Null(Model().Choose(Fixtures.Context(77)));

    [Theory]
    [InlineData(1.5, 3)]
    [InlineData(-0.1, 3)]
    [InlineData(0.1, 0)]
    public void Constructor_InvalidOptions_Throws(double epsilon, int minSamples)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Model(epsilon, minSamples));

    [Fact]
    public void ParametersJson_RecordsTheEffectiveParameters()
        => Assert.Contains("\"epsilon\":0.2", Model(0.2).ParametersJson);
}

public sealed class SlidingWindowUcbModelTests
{
    private static SlidingWindowUcbModel Model(decimal c = 0.05m) => new(new SlidingWindowUcbOptions { C = c });

    [Fact]
    public void Choose_NetworkWithoutRecentEvidence_IsForcedFirst()
    {
        var evidence = Fixtures.Evidence("net-a", settled: 5, mean: 0.5m, recent: 5, recentMean: 0.5m);
        var decision = Model().Choose(Fixtures.Context(3, evidence: evidence))!;

        Assert.Equal("net-b", decision.NetworkId);
        Assert.StartsWith("forced exploration", decision.Rationale);
    }

    [Fact]
    public void Choose_AllObserved_PicksTheHighestUpperBound()
    {
        var a = Fixtures.Evidence("net-a", settled: 10, mean: 0.5m, recent: 10, recentMean: 0.5m);
        var b = Fixtures.Evidence("net-b", settled: 2, mean: 0.45m, recent: 2, recentMean: 0.45m);
        var decision = Model().Choose(Fixtures.Context(3, evidence: [a, b]))!;

        Assert.Equal("net-a", decision.NetworkId);     // 0.5 + 0.0253 beats 0.45 + 0.0566
        Assert.All(decision.Scores, s => Assert.Equal("ucb", s.Basis));
        Assert.True(decision.Scores.Single(s => s.NetworkId == "net-b").Estimate > 0.45m, "the thin arm gets the larger bonus");
    }

    [Fact]
    public void Choose_NothingCovers_ReturnsNull() => Assert.Null(Model().Choose(Fixtures.Context(77)));

    [Fact]
    public void Constructor_NegativeC_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => Model(-1m));
}

public sealed class ModelAssignmentTests
{
    [Fact]
    public void NameForOrdinal_SingleModel_AlwaysThatModel()
    {
        var assignment = new ModelAssignment(["random"], Interleaved: false);
        Assert.All(new long[] { 0, 1, 2, 99 }, ordinal => Assert.Equal("random", assignment.NameForOrdinal(ordinal)));
    }

    [Fact]
    public void NameForOrdinal_Interleaved_RoundRobinsByOrdinal()
    {
        var assignment = new ModelAssignment(["random", "epsilon-greedy"], Interleaved: true);

        Assert.Equal("random", assignment.NameForOrdinal(0));
        Assert.Equal("epsilon-greedy", assignment.NameForOrdinal(1));
        Assert.Equal("random", assignment.NameForOrdinal(2));
    }

    [Fact]
    public void NameForOrdinal_NotInterleaved_UsesTheFirstModelOnly()
        => Assert.Equal("random", new ModelAssignment(["random", "epsilon-greedy"], Interleaved: false).NameForOrdinal(1));

    [Fact]
    public void NameForOrdinal_NoModels_Throws()
        => Assert.Throws<InvalidOperationException>(() => new ModelAssignment([], Interleaved: false).NameForOrdinal(0));
}

public sealed class OptimizationModelCatalogTests
{
    [Fact]
    public void Constructor_DuplicateNames_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new OptimizationModelCatalog([new RandomModel(), new RandomModel()]));
        Assert.Contains("\"random\"", error.Message);
    }

    [Fact]
    public void Get_UnknownName_ThrowsAndListsTheKnownOnes()
    {
        var catalog = new OptimizationModelCatalog([new RandomModel(), new AdvertisedGreedyModel()]);

        var error = Assert.Throws<InvalidOperationException>(() => catalog.Get("nope"));
        Assert.Contains("advertised-greedy", error.Message);
        Assert.True(catalog.Contains("random"));
        Assert.False(catalog.Contains("nope"));
    }

    [Fact]
    public void All_IsSortedByName()
    {
        var catalog = new OptimizationModelCatalog([new RandomModel(), new AdvertisedGreedyModel()]);

        Assert.Equal(["advertised-greedy", "random"], catalog.All.Select(m => m.Name));
    }
}
