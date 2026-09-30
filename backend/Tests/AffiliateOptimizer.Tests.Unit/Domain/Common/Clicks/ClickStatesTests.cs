using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Tests.Unit.Support;

namespace AffiliateOptimizer.Tests.Unit.Domain.Common.Clicks;

public sealed class ClickStatesTests
{
    private static readonly DateTime T0 = Fixtures.T0;
    private static readonly DateTime Expiry = T0.AddSeconds(120);

    private static ClickFact Click(DateTime? routedAt = null, DateTime? disclosedAt = null, string? errorCode = null, decimal? commission = null)
        => new(Expiry, routedAt, disclosedAt, errorCode, commission);

    [Fact]
    public void Derive_LiveAndUnrouted_IsIssued()
        => Assert.Equal(ClickState.Issued, ClickStates.Derive(Click(), T0.AddSeconds(1)));

    [Fact]
    public void Derive_RoutedBeforeDisclosure_IsRouted()
        => Assert.Equal(ClickState.Routed, ClickStates.Derive(Click(routedAt: T0, disclosedAt: T0.AddSeconds(60)), T0.AddSeconds(30)));

    [Fact]
    public void Derive_AtDisclosureTime_IsSettled()
    {
        var disclosedAt = T0.AddSeconds(60);
        Assert.Equal(ClickState.Settled, ClickStates.Derive(Click(routedAt: T0, disclosedAt: disclosedAt, commission: 0.5m), disclosedAt));
    }

    [Fact]
    public void Derive_WithErrorCode_IsRejectedWhateverElseIsTrue()
    {
        Assert.Equal(ClickState.Rejected, ClickStates.Derive(Click(routedAt: T0, errorCode: "MERCHANT_NOT_SUPPORTED"), T0.AddSeconds(1)));
        // A late route attempt is a rejection the server committed, not a lapse.
        Assert.Equal(ClickState.Rejected, ClickStates.Derive(Click(routedAt: Expiry.AddSeconds(5), errorCode: "CLICK_EXPIRED"), Expiry.AddSeconds(10)));
    }

    [Fact]
    public void Derive_NeverRoutedAndPastExpiry_IsExpired()
    {
        Assert.Equal(ClickState.Expired, ClickStates.Derive(Click(), Expiry));
        Assert.Equal(ClickState.Issued, ClickStates.Derive(Click(), Expiry.AddMilliseconds(-1)));
    }

    [Fact]
    public void Derive_RoutedClick_NeverExpires()
        => Assert.Equal(ClickState.Routed, ClickStates.Derive(Click(routedAt: T0), Expiry.AddMinutes(10)));

    [Theory]
    [InlineData(ClickState.Settled, true)]
    [InlineData(ClickState.Rejected, true)]
    [InlineData(ClickState.Expired, true)]
    [InlineData(ClickState.Routed, false)]
    [InlineData(ClickState.Issued, false)]
    public void IsTerminal_MarksTheRightStates(ClickState state, bool terminal)
        => Assert.Equal(terminal, ClickStates.IsTerminal(state));
}
