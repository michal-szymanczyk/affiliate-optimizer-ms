using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Statistics;

namespace AffiliateOptimizer.Tests.Unit.Domain.Common.Statistics;

public sealed class SampleStatisticsTests
{
    [Fact]
    public void Compute_Empty_IsEmpty()
    {
        var stats = SampleStatistics.Compute([]);

        Assert.Equal(0, stats.Count);
        Assert.Null(stats.StandardError);
        Assert.False(stats.Reliable);
    }

    [Fact]
    public void Compute_SingleValue_HasMeanButNoInterval()
    {
        var stats = SampleStatistics.Compute([0.1234m]);

        Assert.Equal(0.1234m, stats.Mean);
        Assert.Null(stats.CiLow);
    }

    [Fact]
    public void Compute_KnownValues_MatchesHandComputation()
    {
        var stats = SampleStatistics.Compute([0.1m, 0.2m, 0.3m]);   // sd = 0.1, se = 0.1/√3

        Assert.Equal(3, stats.Count);
        Assert.Equal(0.2m, stats.Mean);
        Assert.Equal(0.057735m, stats.StandardError);
        Assert.Equal(0.0868m, stats.CiLow);
        Assert.Equal(0.3132m, stats.CiHigh);
        Assert.False(stats.Reliable);
    }

    [Fact]
    public void Compute_ThirtyValues_IsReliable()
        => Assert.True(SampleStatistics.Compute(Enumerable.Range(0, 30).Select(i => 0.1m + i * 0.001m).ToList()).Reliable);

    [Fact]
    public void MeansDiffer_FarApart_True()
    {
        var low = SampleStatistics.Compute(Enumerable.Repeat(0.10m, 20).Select((v, i) => v + (i % 2) * 0.01m).ToList());
        var high = SampleStatistics.Compute(Enumerable.Repeat(0.50m, 20).Select((v, i) => v + (i % 2) * 0.01m).ToList());

        Assert.True(SampleStatistics.MeansDiffer(low, high));
        Assert.True(high.ClearlyAbove(low));
        Assert.False(low.ClearlyAbove(high));
    }

    [Fact]
    public void MeansDiffer_Overlapping_False()
    {
        var a = SampleStatistics.Compute([0.10m, 0.30m, 0.20m, 0.25m]);
        var b = SampleStatistics.Compute([0.12m, 0.28m, 0.22m, 0.24m]);

        Assert.False(SampleStatistics.MeansDiffer(a, b));
        Assert.False(a.ClearlyAbove(b));
    }
}

public sealed class CredentialFingerprintTests
{
    [Fact]
    public void Compute_IsStableAndNeverContainsTheKey()
    {
        var fingerprint = CredentialFingerprint.Compute("ak_live_example_key");

        Assert.Equal(fingerprint, CredentialFingerprint.Compute(" ak_live_example_key "));
        Assert.Equal(16, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
        Assert.DoesNotContain("ak_", fingerprint);
        Assert.NotEqual(fingerprint, CredentialFingerprint.Compute("ak_other"));
    }

    [Fact]
    public void Compute_EmptyKey_Throws() => Assert.Throws<ArgumentException>(() => CredentialFingerprint.Compute(" "));
}
