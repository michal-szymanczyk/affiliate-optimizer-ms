using AffiliateOptimizer.Domain.Common.Loop;

namespace AffiliateOptimizer.Tests.Unit.Domain.Common.Loop;

public sealed class LoopPacingTests
{
    [Theory]
    [InlineData(1, 4, 4_000)]
    [InlineData(9, 7, 7_000)]
    [InlineData(1, 1.0005, 1_001)]
    public void BackoffAfterFailure_RetryAfterGiven_HonorsItExactly(int attempt, double retryAfterSeconds, int expectedMs)
        => Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), LoopPacing.BackoffAfterFailure(attempt, retryAfterSeconds));

    [Theory]
    [InlineData(0)]
    [InlineData(0.2)]
    public void BackoffAfterFailure_TinyRetryAfter_NeverWaitsLessThanASecond(double retryAfterSeconds)
        => Assert.Equal(TimeSpan.FromSeconds(1), LoopPacing.BackoffAfterFailure(1, retryAfterSeconds));

    [Fact]
    public void BackoffAfterFailure_LongRetryAfter_IsNotCapped()
        => Assert.Equal(TimeSpan.FromSeconds(90), LoopPacing.BackoffAfterFailure(1, 90));

    [Theory]
    [InlineData(1, 1_000)]
    [InlineData(2, 2_000)]
    [InlineData(3, 4_000)]
    [InlineData(5, 16_000)]
    public void BackoffAfterFailure_NoRetryAfter_BacksOffExponentially(int attempt, int expectedMs)
        => Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), LoopPacing.BackoffAfterFailure(attempt, null));

    [Theory]
    [InlineData(6)]
    [InlineData(20)]
    [InlineData(100)]
    public void BackoffAfterFailure_ManyFailures_CapsAtThirtySeconds(int attempt)
        => Assert.Equal(TimeSpan.FromSeconds(30), LoopPacing.BackoffAfterFailure(attempt, null));

    [Fact]
    public void BackoffAfterFailure_AttemptBelowOne_Throws()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => LoopPacing.BackoffAfterFailure(0, null));
        Assert.Contains("at least 1", error.Message);
    }

    [Fact]
    public void Intervals_PaceClicksAtTheSustainedRate()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), LoopPacing.ClickInterval);
        Assert.Equal(TimeSpan.FromSeconds(2), LoopPacing.PollInterval);
    }
}
