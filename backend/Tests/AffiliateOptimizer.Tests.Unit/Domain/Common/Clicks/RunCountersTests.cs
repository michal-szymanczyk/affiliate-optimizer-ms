using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Tests.Unit.Support;

namespace AffiliateOptimizer.Tests.Unit.Domain.Common.Clicks;

public sealed class RunCountersTests
{
    private static readonly DateTime T0 = Fixtures.T0;
    private static readonly DateTime Expiry = T0.AddSeconds(120);
    private static readonly DateTime Now = T0.AddSeconds(90);

    private static ClickFact Settled(decimal commission) => new(Expiry, T0, T0.AddSeconds(60), null, commission);
    private static ClickFact Withheld(decimal commission) => new(Expiry, T0, T0.AddSeconds(200), null, commission);
    private static ClickFact Rejected() => new(Expiry, T0, T0, "MERCHANT_NOT_SUPPORTED", null);
    private static ClickFact Issued() => new(Expiry, null, null, null, null);
    private static ClickFact Lapsed() => new(T0.AddSeconds(10), null, null, null, null);

    [Fact]
    public void Compute_EmptyRun_DoesNotDivideByZero()
    {
        var counters = RunCounters.Compute([], Now);

        Assert.Equal(0, counters.Processed);
        Assert.Equal(0, counters.Settled);
        Assert.Equal(0m, counters.TotalCommission);
        Assert.Null(counters.PerSettledClick);
    }

    [Fact]
    public void Compute_SeparatesAwaitingDisclosureFromCompleted()
    {
        var counters = RunCounters.Compute([Settled(0.25m), Settled(0.55m), Withheld(0.55m)], Now);

        Assert.Equal(3, counters.Processed);
        Assert.Equal(1, counters.AwaitingDisclosure);
        Assert.Equal(2, counters.Completed);
        Assert.Equal(2, counters.Settled);
        Assert.Equal(0.8m, counters.TotalCommission);
    }

    [Fact]
    public void Compute_AveragesOverSettledClicksOnly()
    {
        var counters = RunCounters.Compute([Settled(0.5m), Rejected(), Issued()], Now);

        Assert.Equal(0.5m, counters.PerSettledClick);
        Assert.Equal(1, counters.Rejected);
    }

    [Fact]
    public void Compute_SumsMoneyExactly()
        => Assert.Equal(0.3m, RunCounters.Compute([Settled(0.1m), Settled(0.2m)], Now).TotalCommission);

    [Fact]
    public void Compute_PreservesFourDecimalPrecision()
    {
        var counters = RunCounters.Compute([Settled(0.0975m), Settled(0.1394m), Settled(0.3037m)], Now);

        Assert.Equal(0.5406m, counters.TotalCommission);
        Assert.Equal(0.1802m, counters.PerSettledClick);
    }

    [Fact]
    public void Compute_CountsALapsedClickAsCompletedButNotSettled()
    {
        var counters = RunCounters.Compute([Lapsed()], Now);

        Assert.Equal(1, counters.Expired);
        Assert.Equal(1, counters.Completed);
        Assert.Equal(0, counters.Settled);
        Assert.Null(counters.PerSettledClick);
    }
}
