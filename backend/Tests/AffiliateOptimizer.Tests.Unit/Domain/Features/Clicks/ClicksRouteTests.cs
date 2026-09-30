using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.Clicks.Route;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Exceptions;
using Common.Domain.Time;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace AffiliateOptimizer.Tests.Unit.Domain.Features.Clicks;

public sealed class ClicksRouteTests
{
    private static readonly DateTime T0 = Fixtures.T0;
    private static readonly RouteAcknowledgment Accepted = new("clk_1", "net-covers", T0.AddMilliseconds(500), false, T0.AddSeconds(60));
    private static readonly ClicksRoute.Command Command = new("clk_1", "net-covers");

    private readonly Mock<ClicksRoute.IRepository> _repository = new();
    private readonly Mock<ILoopEventWriter> _events = new();
    private readonly Mock<IAffiliateServiceClient> _client = new();
    private readonly Mock<ISystemTime> _time = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(Fixtures.T0));
    private readonly List<LoopEventModel> _recorded = [];

    public ClicksRouteTests()
    {
        _time.Setup(t => t.Now()).Returns(T0);
        _repository.Setup(r => r.GetRouteTarget("clk_1", "net-covers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClicksRoute.IRepository.RouteTarget(1, "Covers", false));
        _repository.Setup(r => r.IsRunStopped(1, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _events.Setup(e => e.Add(It.IsAny<LoopEventModel>(), It.IsAny<CancellationToken>()))
            .Callback<LoopEventModel, CancellationToken>((e, _) => _recorded.Add(e))
            .Returns(Task.CompletedTask);
    }

    private ClicksRoute.CommandHandler Handler() => new(_repository.Object, _events.Object, _client.Object, _timeProvider, _time.Object);

    [Fact]
    public async Task Handle_Accepted_RecordsTheAcknowledgmentValues()
    {
        _client.Setup(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>())).ReturnsAsync(Accepted);

        var result = await Handler().Handle(Command, CancellationToken.None);

        Assert.Equal(ClicksRoute.RouteOutcome.Accepted, result.Outcome);
        _repository.Verify(r => r.RecordRoute("clk_1", Accepted, "Covers", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CommittedRejection_IsRecordedOnceAndNeverRetried()
    {
        _client.Setup(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AffiliateServiceException(422, "MERCHANT_NOT_SUPPORTED", "nope"));

        var result = await Handler().Handle(Command, CancellationToken.None);

        Assert.Equal(ClicksRoute.RouteOutcome.Rejected, result.Outcome);
        Assert.Equal("MERCHANT_NOT_SUPPORTED", result.ErrorCode);
        _client.Verify(c => c.Route(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.RecordRejection("clk_1", "net-covers", "Covers", T0, "MERCHANT_NOT_SUPPORTED", 1, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_recorded);
    }

    [Fact]
    public async Task Handle_RequestError_LeavesTheRowUnroutedAndRecordsAnEvent()
    {
        _client.Setup(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AffiliateServiceException(404, "NETWORK_NOT_FOUND", "unknown network"));

        var result = await Handler().Handle(Command, CancellationToken.None);

        Assert.Equal(ClicksRoute.RouteOutcome.Failed, result.Outcome);
        _repository.Verify(r => r.RecordRoute(It.IsAny<string>(), It.IsAny<RouteAcknowledgment>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.RecordRejection(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        var loopEvent = Assert.Single(_recorded);
        Assert.Equal("NETWORK_NOT_FOUND", loopEvent.Code);
        Assert.Equal("clk_1", loopEvent.ClickId);
    }

    [Fact]
    public async Task Handle_TransportFailureThenAccepted_ReplaysTheIdenticalRequestAfterOneSecond()
    {
        _client.SetupSequence(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AffiliateServiceTransportException("lost"))
            .ReturnsAsync(Accepted with { AlreadyRouted = true });

        var pending = Handler().Handle(Command, CancellationToken.None);
        _client.Verify(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(pending.IsCompleted);

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await pending;

        Assert.Equal(ClicksRoute.RouteOutcome.Accepted, result.Outcome);
        _client.Verify(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>()), Times.Exactly(2));
        _repository.Verify(r => r.RecordRoute("clk_1", It.Is<RouteAcknowledgment>(a => a.AlreadyRouted), "Covers", 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThreeTransportFailures_MarksTheClickUnconfirmed()
    {
        _client.Setup(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceTransportException("lost"));

        var pending = Handler().Handle(Command, CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.False(pending.IsCompleted);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        var result = await pending;

        Assert.Equal(ClicksRoute.RouteOutcome.Unconfirmed, result.Outcome);
        _client.Verify(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>()), Times.Exactly(3));
        _repository.Verify(r => r.MarkUnconfirmed("clk_1", 3, It.IsAny<CancellationToken>()), Times.Once);
        var loopEvent = Assert.Single(_recorded);
        Assert.Equal(LoopEventCodes.RouteUnconfirmed, loopEvent.Code);
        Assert.True(loopEvent.Dismissible);
    }

    [Fact]
    public async Task Handle_StoppedDuringBackoff_MakesNoFurtherAttempt()
    {
        _client.Setup(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceTransportException("lost"));
        _repository.SetupSequence(r => r.IsRunStopped(1, It.IsAny<CancellationToken>())).ReturnsAsync(false).ReturnsAsync(true);

        var pending = Handler().Handle(Command, CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await pending;

        Assert.Equal(ClicksRoute.RouteOutcome.Stopped, result.Outcome);
        _client.Verify(c => c.Route("clk_1", "net-covers", It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.MarkUnconfirmed(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_recorded);
    }

    [Fact]
    public async Task Handle_UnknownClick_IsNotFound()
    {
        _repository.Setup(r => r.GetRouteTarget("clk_1", "net-covers", It.IsAny<CancellationToken>())).ReturnsAsync((ClicksRoute.IRepository.RouteTarget?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Handler().Handle(Command, CancellationToken.None));
    }
}
