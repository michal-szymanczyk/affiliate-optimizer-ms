using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Common.Optimization.Models;
using AffiliateOptimizer.Domain.Features.Clicks.IssueAndRoute;
using AffiliateOptimizer.Domain.Features.Clicks.Route;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Requests;
using Common.Domain.Time;
using Moq;

namespace AffiliateOptimizer.Tests.Unit.Domain.Features.Clicks;

public sealed class ClicksIssueAndRouteTests
{
    private static readonly DateTime T0 = Fixtures.T0;
    private static readonly Click Click1 = new("clk_1", 42, T0, T0.AddSeconds(120));
    private static readonly IReadOnlyList<EligibleNetwork> Covers = [new("net-covers", "Covers", 0.5m)];

    private readonly Mock<ClicksIssueAndRoute.IRepository> _repository = new();
    private readonly Mock<ILoopEventWriter> _events = new();
    private readonly Mock<IAffiliateServiceClient> _client = new();
    private readonly Mock<IDispatcher> _dispatcher = new();
    private readonly Mock<ISystemTime> _time = new();
    private readonly List<LoopEventModel> _recorded = [];
    private readonly List<ClicksIssueAndRoute.IRepository.DecisionModel> _decisions = [];

    public ClicksIssueAndRouteTests()
    {
        _time.Setup(t => t.Now()).Returns(T0);
        _repository.Setup(r => r.GetActiveRun(It.IsAny<CancellationToken>())).ReturnsAsync(Run(["random"], false));
        _repository.Setup(r => r.IssueClick(It.IsAny<ClicksIssueAndRoute.IRepository.IssueClickModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClicksIssueAndRoute.IRepository.IssuedClick(0, "Merchant 42"));
        _repository.Setup(r => r.GetEligibleNetworks(1, 42, It.IsAny<CancellationToken>())).ReturnsAsync(Covers);
        _repository.Setup(r => r.GetEvidence(1, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, NetworkEvidence>());
        _repository.Setup(r => r.RecordDecision(It.IsAny<string>(), It.IsAny<ClicksIssueAndRoute.IRepository.DecisionModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, ClicksIssueAndRoute.IRepository.DecisionModel, CancellationToken>((_, d, _) => _decisions.Add(d))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.RecordClickFailure(1, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _events.Setup(e => e.Add(It.IsAny<LoopEventModel>(), It.IsAny<CancellationToken>()))
            .Callback<LoopEventModel, CancellationToken>((e, _) => _recorded.Add(e))
            .Returns(Task.CompletedTask);
        _client.Setup(c => c.NextClick(It.IsAny<CancellationToken>())).ReturnsAsync(Click1);
        _dispatcher.Setup(d => d.Send(It.IsAny<ClicksRoute.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClicksRoute.Result(ClicksRoute.RouteOutcome.Accepted, null));
    }

    private static ActiveRun Run(IReadOnlyList<string> models, bool interleaved, RunStatus status = RunStatus.Running)
        => new(1, status, new ModelAssignment(models, interleaved), 10, 0, T0, null, "fp", "1.0");

    private ClicksIssueAndRoute.CommandHandler Handler(params IOptimizationModel[] models)
        => new(_repository.Object, _events.Object, _client.Object,
            new OptimizationModelCatalog(models.Length == 0 ? [new RandomModel(), new AdvertisedGreedyModel()] : models),
            new PinnedRandom(0), _dispatcher.Object, _time.Object);

    private Task<ClicksIssueAndRoute.Result> Turn(params IOptimizationModel[] models)
        => Handler(models).Handle(new ClicksIssueAndRoute.Command(), CancellationToken.None);

    [Fact]
    public async Task Handle_NoActiveRun_IsIdleAndTouchesNothing()
    {
        _repository.Setup(r => r.GetActiveRun(It.IsAny<CancellationToken>())).ReturnsAsync((ActiveRun?)null);

        var result = await Turn();

        Assert.Equal(ClicksIssueAndRoute.Outcome.Idle, result.Outcome);
        Assert.Equal(LoopPacing.IdleInterval, result.NextDelay);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_PausedRun_IsIdle()
    {
        _repository.Setup(r => r.GetActiveRun(It.IsAny<CancellationToken>())).ReturnsAsync(Run(["random"], false, RunStatus.Paused));

        Assert.Equal(ClicksIssueAndRoute.Outcome.Idle, (await Turn()).Outcome);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_RoutesToACoveringNetworkThroughTheRouteCommand()
    {
        var result = await Turn();

        Assert.Equal(ClicksIssueAndRoute.Outcome.Routed, result.Outcome);
        Assert.Equal(LoopPacing.ClickInterval, result.NextDelay);
        Assert.Equal("clk_1", result.ClickId);
        _dispatcher.Verify(d => d.Send(new ClicksRoute.Command("clk_1", "net-covers"), It.IsAny<CancellationToken>()), Times.Once);
        var decision = Assert.Single(_decisions);
        Assert.Equal("random", decision.ModelName);
        Assert.Equal("net-covers", decision.Decision!.NetworkId);
        Assert.Equal(T0, decision.DecidedAt);
    }

    [Fact]
    public async Task Handle_IssueRateLimited_HonorsRetryAfterAndRecordsTheEvent()
    {
        _client.Setup(c => c.NextClick(It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceException(429, "RATE_LIMITED", "slow down", 4));

        var result = await Turn();

        Assert.Equal(ClicksIssueAndRoute.Outcome.IssueFailed, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(4), result.NextDelay);
        var loopEvent = Assert.Single(_recorded);
        Assert.Equal("RATE_LIMITED", loopEvent.Code);
        Assert.Equal(LoopKind.Click, loopEvent.Loop);
        Assert.Equal(T0.AddSeconds(4), loopEvent.RetryAt);
        _repository.Verify(r => r.IssueClick(It.IsAny<ClicksIssueAndRoute.IRepository.IssueClickModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IssueTransportFailure_IsVisibleAndBacksOffExponentially()
    {
        _client.Setup(c => c.NextClick(It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceTransportException("down"));
        _repository.Setup(r => r.RecordClickFailure(1, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await Turn();

        Assert.Equal(TimeSpan.FromSeconds(4), result.NextDelay);
        var loopEvent = Assert.Single(_recorded);
        Assert.Equal(LoopEventKind.Transport, loopEvent.Kind);
        Assert.Equal(LoopEventCodes.Transport, loopEvent.Code);
    }

    [Fact]
    public async Task Handle_IssueSucceeds_ResetsFailuresAndResolvesOpenEvents()
    {
        await Turn();

        _repository.Verify(r => r.ResetClickFailures(1, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.ResolveOpen(LoopKind.Click, T0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NothingCovers_LetsTheClickLapseWithoutRouting()
    {
        _repository.Setup(r => r.GetEligibleNetworks(1, 42, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await Turn();

        Assert.Equal(ClicksIssueAndRoute.Outcome.Lapsed, result.Outcome);
        _dispatcher.VerifyNoOtherCalls();
        var decision = Assert.Single(_decisions);
        Assert.Null(decision.Decision);
        Assert.Contains("lapse", decision.Rationale);
    }

    [Fact]
    public async Task Handle_ModelThrows_RecordsTheFaultAndTheLoopGoesOn()
    {
        _repository.Setup(r => r.GetActiveRun(It.IsAny<CancellationToken>())).ReturnsAsync(Run(["boom"], false));

        var result = await Turn(new ThrowingModel());

        Assert.Equal(ClicksIssueAndRoute.Outcome.ModelFailed, result.Outcome);
        Assert.Equal(LoopPacing.ClickInterval, result.NextDelay);
        var loopEvent = Assert.Single(_recorded);
        Assert.Equal(LoopEventCodes.ModelError, loopEvent.Code);
        Assert.Equal("clk_1", loopEvent.ClickId);
        Assert.Contains("model threw", Assert.Single(_decisions).Rationale);
        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_InterleavedRun_PicksTheModelByOrdinal()
    {
        _repository.Setup(r => r.GetActiveRun(It.IsAny<CancellationToken>())).ReturnsAsync(Run(["random", "advertised-greedy"], true));
        _repository.Setup(r => r.IssueClick(It.IsAny<ClicksIssueAndRoute.IRepository.IssueClickModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClicksIssueAndRoute.IRepository.IssuedClick(1, "Merchant 42"));

        await Turn();

        Assert.Equal("advertised-greedy", Assert.Single(_decisions).ModelName);
        _repository.Verify(r => r.GetEvidence(1, "advertised-greedy", T0.AddMinutes(-10), T0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RouteRejected_ReportsTheRejection()
    {
        _dispatcher.Setup(d => d.Send(It.IsAny<ClicksRoute.Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClicksRoute.Result(ClicksRoute.RouteOutcome.Rejected, "MERCHANT_NOT_SUPPORTED"));

        Assert.Equal(ClicksIssueAndRoute.Outcome.Rejected, (await Turn()).Outcome);
    }

    private sealed class ThrowingModel : IOptimizationModel
    {
        public string Name => "boom";
        public string Version => "1";
        public string Description => "throws";
        public string ParametersJson => "{}";
        public Decision? Choose(DecisionContext context) => throw new InvalidOperationException("kaboom");
    }
}
