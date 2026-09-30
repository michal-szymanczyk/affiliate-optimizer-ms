using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.Commissions.Poll;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Time;
using Moq;

namespace AffiliateOptimizer.Tests.Unit.Domain.Features.Commissions;

public sealed class CommissionsPollTests
{
    private static readonly DateTime T0 = Fixtures.T0;
    private const string Fingerprint = "fp-0001";

    private readonly Mock<CommissionsPoll.IRepository> _repository = new();
    private readonly Mock<ILoopEventWriter> _events = new();
    private readonly Mock<IAffiliateServiceClient> _client = new();
    private readonly Mock<ICredentialContext> _credentials = new();
    private readonly Mock<ISystemTime> _time = new();
    private readonly List<LoopEventModel> _recorded = [];

    public CommissionsPollTests()
    {
        _time.Setup(t => t.Now()).Returns(T0);
        _credentials.Setup(c => c.Fingerprint).Returns(Fingerprint);
        _repository.Setup(r => r.GetOrCreateCursor(Fingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommissionsPoll.IRepository.FeedPosition(null, 0));
        _repository.Setup(r => r.ApplyFeedPage(Fingerprint, It.IsAny<IReadOnlyList<CommissionRow>>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<CommissionRow> rows, string? _, DateTime _, CancellationToken _) => new CommissionsPoll.IRepository.FeedPageResult(rows.Count, 0));
        _repository.Setup(r => r.RecordPollFailure(Fingerprint, It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _events.Setup(e => e.Add(It.IsAny<LoopEventModel>(), It.IsAny<CancellationToken>()))
            .Callback<LoopEventModel, CancellationToken>((e, _) => _recorded.Add(e))
            .Returns(Task.CompletedTask);
    }

    private CommissionsPoll.CommandHandler Handler() => new(_repository.Object, _events.Object, _client.Object, _credentials.Object, _time.Object);

    private Task<CommissionsPoll.Result> Poll() => Handler().Handle(new CommissionsPoll.Command(), CancellationToken.None);

    private static CommissionRow Settlement(string clickId) => new(clickId, 42, "net-a", T0, T0.AddSeconds(60), "settlement", "settled", 0.5713m, null);

    [Fact]
    public async Task Handle_StoresTheCursorAfterEveryPollIncludingTheEmptyOnes()
    {
        _client.Setup(c => c.GetCommissions(null, It.IsAny<CancellationToken>())).ReturnsAsync(new CommissionFeed([], "cursor-1"));
        _client.Setup(c => c.GetCommissions("cursor-1", It.IsAny<CancellationToken>())).ReturnsAsync(new CommissionFeed([], "cursor-1"));

        var first = await Poll();
        _repository.Setup(r => r.GetOrCreateCursor(Fingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommissionsPoll.IRepository.FeedPosition("cursor-1", 0));
        var second = await Poll();

        Assert.Equal(LoopPacing.PollInterval, first.NextDelay);
        Assert.Equal("polled", second.Outcome);
        _repository.Verify(r => r.ApplyFeedPage(Fingerprint, It.Is<IReadOnlyList<CommissionRow>>(rows => rows.Count == 0), "cursor-1", T0, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _client.Verify(c => c.GetCommissions("cursor-1", It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.ResolveOpen(LoopKind.Poll, T0, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_FoldsRowsAndReportsCounts()
    {
        _client.Setup(c => c.GetCommissions(null, It.IsAny<CancellationToken>())).ReturnsAsync(new CommissionFeed([Settlement("clk_1"), Settlement("clk_2")], "c2"));

        var result = await Poll();

        Assert.Equal(2, result.Folded);
        Assert.Equal(LoopPacing.PollInterval, result.NextDelay);
    }

    [Fact]
    public async Task Handle_FullPage_DrainsImmediately()
    {
        var page = Enumerable.Range(0, LoopPacing.FeedPageLimit).Select(i => Settlement($"clk_{i}")).ToList();
        _client.Setup(c => c.GetCommissions(null, It.IsAny<CancellationToken>())).ReturnsAsync(new CommissionFeed(page, "c2"));

        Assert.Equal(TimeSpan.Zero, (await Poll()).NextDelay);
    }

    [Fact]
    public async Task Handle_InvalidCursor_RestartsTheFeedAndRecordsTheEvent()
    {
        _client.Setup(c => c.GetCommissions(null, It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceException(400, "INVALID_CURSOR", "tampered"));

        var result = await Poll();

        Assert.Equal("api-error", result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(1), result.NextDelay);
        _repository.Verify(r => r.RecordPollFailure(Fingerprint, T0, true, It.IsAny<CancellationToken>()), Times.Once);
        var loopEvent = Assert.Single(_recorded);
        Assert.Equal("INVALID_CURSOR", loopEvent.Code);
        Assert.True(loopEvent.Dismissible);
        Assert.Equal(T0.AddSeconds(1), loopEvent.RetryAt);
    }

    [Fact]
    public async Task Handle_Unauthorized_IsNotDismissible()
    {
        _client.Setup(c => c.GetCommissions(null, It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceException(401, "UNAUTHORIZED", "bad credentials"));

        await Poll();

        Assert.False(Assert.Single(_recorded).Dismissible);
        _repository.Verify(r => r.RecordPollFailure(Fingerprint, T0, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RateLimited_HonorsRetryAfter()
    {
        _client.Setup(c => c.GetCommissions(null, It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceException(429, "RATE_LIMITED", "slow down", 7));

        Assert.Equal(TimeSpan.FromSeconds(7), (await Poll()).NextDelay);
    }

    [Fact]
    public async Task Handle_ConsecutiveFailures_BackOffExponentially()
    {
        _client.Setup(c => c.GetCommissions(null, It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceTransportException("down"));
        _repository.Setup(r => r.RecordPollFailure(Fingerprint, It.IsAny<DateTime>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await Poll();

        Assert.Equal("transport-error", result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(4), result.NextDelay);
        Assert.Equal(LoopEventCodes.Transport, Assert.Single(_recorded).Code);
    }

    [Fact]
    public async Task Handle_ClientBug_IsReportedAsAClientErrorWithTheMessage()
    {
        _client.Setup(c => c.GetCommissions(null, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Cannot read property of undefined"));

        var result = await Poll();

        Assert.Equal("client-error", result.Outcome);
        var loopEvent = Assert.Single(_recorded);
        Assert.Equal(LoopEventCodes.ClientError, loopEvent.Code);
        Assert.Contains("Cannot read property of undefined", loopEvent.Message);
        Assert.Contains("inside the client", loopEvent.Message);
    }
}
