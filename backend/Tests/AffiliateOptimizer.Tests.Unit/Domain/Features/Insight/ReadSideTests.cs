using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.Clicks.GetCounters;
using AffiliateOptimizer.Domain.Features.Clicks.List;
using AffiliateOptimizer.Domain.Features.Evaluation.Compare;
using AffiliateOptimizer.Domain.Features.Health.GetStatus;
using AffiliateOptimizer.Domain.Features.Networks.GetInsight;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Lists;
using Common.Domain.Time;
using Moq;

namespace AffiliateOptimizer.Tests.Unit.Domain.Features.Insight;

internal static class Clock
{
    public static ISystemTime At(DateTime now)
    {
        var time = new Mock<ISystemTime>();
        time.Setup(t => t.Now()).Returns(now);
        return time.Object;
    }
}

public sealed class ClicksListHandlerTests
{
    [Fact]
    public async Task Handle_NoSort_DefaultsToNewestFirstWithoutMutatingTheQuery()
    {
        var repository = new Mock<ClicksList.IRepository>();
        IDynamicListParams? seen = null;
        repository.Setup(r => r.GetClicks(It.IsAny<IDynamicListParams>(), It.IsAny<ClicksList.IFilter>(), Fixtures.T0, It.IsAny<CancellationToken>()))
            .Callback<IDynamicListParams, ClicksList.IFilter, DateTime, CancellationToken>((p, _, _, _) => seen = p)
            .ReturnsAsync(new PagedList<ClicksList.Model>([], 0, 40, 0));
        var query = new ClicksList.Query { RunId = 3 };

        var result = await new ClicksList.QueryHandler(repository.Object, Clock.At(Fixtures.T0)).Handle(query, CancellationToken.None);

        Assert.Equal(Fixtures.T0, result.ServerNow);
        Assert.Equal("IssuedAt", seen!.SortBy);
        Assert.Equal(SortDirection.Desc, seen.SortDirection);
        Assert.Null(query.SortBy);
    }
}

public sealed class ClicksGetCountersHandlerTests
{
    [Fact]
    public async Task Handle_ComputesCountersAtServerTime()
    {
        var repository = new Mock<ClicksGetCounters.IRepository>();
        var expiry = Fixtures.T0.AddSeconds(120);
        repository.Setup(r => r.GetClickFacts(5, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ClickFact(expiry, Fixtures.T0, Fixtures.T0.AddSeconds(60), null, 0.25m),
            new ClickFact(expiry, Fixtures.T0, Fixtures.T0.AddSeconds(200), null, null),
            new ClickFact(Fixtures.T0.AddSeconds(10), null, null, null, null),
        ]);
        repository.Setup(r => r.CountOrphanDisclosures(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        repository.Setup(r => r.GetRunStartedAt(5, It.IsAny<CancellationToken>())).ReturnsAsync(Fixtures.T0);

        var result = await new ClicksGetCounters.QueryHandler(repository.Object, Clock.At(Fixtures.T0.AddSeconds(90))).Handle(new ClicksGetCounters.Query(5), CancellationToken.None);

        Assert.Equal(3, result.Processed);
        Assert.Equal(1, result.Settled);
        Assert.Equal(1, result.AwaitingDisclosure);
        Assert.Equal(1, result.Expired);
        Assert.Equal(0.25m, result.TotalCommission);
        Assert.Equal(7, result.OrphanDisclosures);
        Assert.Equal(Fixtures.T0, result.StartedAt);
    }
}

public sealed class NetworksGetInsightHandlerTests
{
    private static readonly DateTime Now = Fixtures.T0.AddMinutes(30);

    private static NetworksGetInsight.IRepository.Observation Settled(string network, decimal commission, DateTime disclosedAt)
        => new(network, disclosedAt, commission, null);

    [Fact]
    public async Task Handle_FlagsThinStaleAndChangingEvidence()
    {
        var repository = new Mock<NetworksGetInsight.IRepository>();
        repository.Setup(r => r.ResolveRunId(null, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        repository.Setup(r => r.GetNetworkSnapshot(1, It.IsAny<CancellationToken>())).ReturnsAsync(Fixtures.Networks);
        var observations = new List<NetworksGetInsight.IRepository.Observation>();
        // net-a: 12 old observations around 0.30 (settled long ago) and 3 recent ones around 0.60 → changing, not thin, not stale
        for (var i = 0; i < 12; i++) observations.Add(Settled("net-a", 0.30m + (i % 2) * 0.01m, Now.AddMinutes(-25).AddSeconds(i)));
        for (var i = 0; i < 3; i++) observations.Add(Settled("net-a", 0.60m + i * 0.001m, Now.AddMinutes(-2).AddSeconds(i)));
        // net-b: 2 observations, 20 minutes ago → thin and stale
        observations.Add(Settled("net-b", 0.50m, Now.AddMinutes(-20)));
        observations.Add(Settled("net-b", 0.52m, Now.AddMinutes(-20)));
        observations.Add(new NetworksGetInsight.IRepository.Observation("net-b", null, null, null));                // pending
        observations.Add(new NetworksGetInsight.IRepository.Observation("net-b", Now.AddMinutes(-1), null, "MERCHANT_NOT_SUPPORTED"));
        repository.Setup(r => r.GetObservations(1, It.IsAny<CancellationToken>())).ReturnsAsync(observations);

        var result = await new NetworksGetInsight.QueryHandler(repository.Object, new EvaluationOptions(), Clock.At(Now)).Handle(new NetworksGetInsight.Query(null), CancellationToken.None);

        Assert.Equal(1, result.RunId);
        var a = result.Networks.Single(n => n.NetworkId == "net-a");
        Assert.Equal(15, a.Settled);
        Assert.Equal(3, a.RecentSettled);
        Assert.False(a.Thin);
        Assert.False(a.Stale);
        Assert.True(a.Changing);
        Assert.True(a.Trend > 0);
        Assert.True(a.ClaimGap < 0);                       // advertised 0.4, mean ≈ 0.36
        Assert.NotNull(a.CiLow);

        var b = result.Networks.Single(n => n.NetworkId == "net-b");
        Assert.True(b.Thin);
        Assert.True(b.Stale);
        Assert.False(b.Changing);
        Assert.Equal(1, b.Pending);
        Assert.Equal(1, b.Rejected);
        Assert.Equal(0.51m, b.Mean);

        var c = result.Networks.Single(n => n.NetworkId == "net-c");
        Assert.Null(c.Mean);
        Assert.True(c.Thin);
        Assert.True(c.Stale);
    }

    [Fact]
    public async Task Handle_NoRunAtAll_IsEmpty()
    {
        var repository = new Mock<NetworksGetInsight.IRepository>();
        repository.Setup(r => r.ResolveRunId(null, It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);

        var result = await new NetworksGetInsight.QueryHandler(repository.Object, new EvaluationOptions(), Clock.At(Now)).Handle(new NetworksGetInsight.Query(null), CancellationToken.None);

        Assert.Empty(result.Networks);
        Assert.Null(result.RunId);
    }
}

public sealed class EvaluationCompareHandlerTests
{
    private static readonly DateTime Now = Fixtures.T0.AddMinutes(60);

    private static EvaluationCompare.IRepository.ArmObservation Settled(string model, decimal commission, int minute, decimal advertised = 0.5m)
        => new(model, advertised, Fixtures.T0.AddMinutes(minute).AddSeconds(120), Fixtures.T0.AddMinutes(minute), Fixtures.T0.AddMinutes(minute).AddSeconds(60), commission, null);

    private static Mock<EvaluationCompare.IRepository> Repository(IReadOnlyList<string> models, IReadOnlyList<EvaluationCompare.IRepository.ArmObservation> observations)
    {
        var repository = new Mock<EvaluationCompare.IRepository>();
        repository.Setup(r => r.ResolveRun(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluationCompare.IRepository.RunScope(1, models, models.Count > 1, Fixtures.T0, null));
        repository.Setup(r => r.GetArmObservations(1, It.IsAny<CancellationToken>())).ReturnsAsync(observations);
        return repository;
    }

    private static Task<EvaluationCompare.Result> Compare(Mock<EvaluationCompare.IRepository> repository)
        => new EvaluationCompare.QueryHandler(repository.Object, new EvaluationOptions(), Clock.At(Now)).Handle(new EvaluationCompare.Query(null), CancellationToken.None);

    [Fact]
    public async Task Handle_BelowTheSampleFloor_IsInconclusiveAndSaysWhy()
    {
        var observations = Enumerable.Range(0, 10).Select(i => Settled("random", 0.3m, i)).Concat(Enumerable.Range(0, 10).Select(i => Settled("epsilon-greedy", 0.6m, i))).ToList();

        var result = await Compare(Repository(["random", "epsilon-greedy"], observations));

        Assert.Equal(EvaluationCompare.Verdict.Inconclusive, result.Verdict);
        Assert.Null(result.LeadingModel);
        Assert.Contains(result.Caveats, c => c.Contains("30 settled clicks"));
        Assert.Equal(2, result.Arms.Count);
        Assert.Equal(0.6m, result.Arms.Single(a => a.ModelName == "epsilon-greedy").Mean);
    }

    [Fact]
    public async Task Handle_ClearlySeparatedArms_Lead()
    {
        var observations = Enumerable.Range(0, 40).Select(i => Settled("random", 0.30m + (i % 3) * 0.01m, i))
            .Concat(Enumerable.Range(0, 40).Select(i => Settled("epsilon-greedy", 0.60m + (i % 3) * 0.01m, i, advertised: 0.55m))).ToList();

        var result = await Compare(Repository(["random", "epsilon-greedy"], observations));

        Assert.Equal(EvaluationCompare.Verdict.Leading, result.Verdict);
        Assert.Equal("epsilon-greedy", result.LeadingModel);
        var arm = result.Arms.Single(a => a.ModelName == "epsilon-greedy");
        Assert.Equal(40, arm.Settled);
        Assert.False(arm.ConditionsChanged);
        Assert.Equal(0.55m, arm.MeanAdvertisedOfChosen);
        Assert.True(arm.ClaimGap > 0);
        Assert.Contains(result.Caveats, c => c.Contains("No counterfactual"));
    }

    [Fact]
    public async Task Handle_OverlappingArms_NoSeparation()
    {
        var observations = Enumerable.Range(0, 40).Select(i => Settled("random", 0.30m + (i % 5) * 0.02m, i))
            .Concat(Enumerable.Range(0, 40).Select(i => Settled("epsilon-greedy", 0.31m + (i % 5) * 0.02m, i))).ToList();

        var result = await Compare(Repository(["random", "epsilon-greedy"], observations));

        Assert.Equal(EvaluationCompare.Verdict.NoSeparation, result.Verdict);
        Assert.Contains(result.Caveats, c => c.Contains("overlap"));
    }

    [Fact]
    public async Task Handle_DriftInsideAnArm_IsCaveatedAndNeverALead()
    {
        // epsilon-greedy pays 0.9 in the first half and 0.3 in the second: the whole-run mean is still above random's.
        var drifting = Enumerable.Range(0, 20).Select(i => Settled("epsilon-greedy", 0.90m + (i % 2) * 0.01m, i))
            .Concat(Enumerable.Range(20, 20).Select(i => Settled("epsilon-greedy", 0.30m + (i % 2) * 0.01m, i)));
        var observations = Enumerable.Range(0, 40).Select(i => Settled("random", 0.20m + (i % 2) * 0.01m, i)).Concat(drifting).ToList();

        var result = await Compare(Repository(["random", "epsilon-greedy"], observations));

        Assert.NotEqual(EvaluationCompare.Verdict.Leading, result.Verdict);
        Assert.True(result.Arms.Single(a => a.ModelName == "epsilon-greedy").ConditionsChanged);
        Assert.Contains(result.Caveats, c => c.Contains("Conditions changed"));
    }

    [Fact]
    public async Task Handle_SingleArm_HasNothingToCompare()
    {
        var result = await Compare(Repository(["random"], Enumerable.Range(0, 40).Select(i => Settled("random", 0.3m, i)).ToList()));

        Assert.Equal(EvaluationCompare.Verdict.Inconclusive, result.Verdict);
        Assert.Contains(result.Caveats, c => c.Contains("Only one arm"));
    }

    [Fact]
    public async Task Handle_NoRun_IsInconclusive()
    {
        var repository = new Mock<EvaluationCompare.IRepository>();
        repository.Setup(r => r.ResolveRun(null, It.IsAny<CancellationToken>())).ReturnsAsync((EvaluationCompare.IRepository.RunScope?)null);

        var result = await Compare(repository);

        Assert.Null(result.RunId);
        Assert.Equal(["No run to compare."], result.Caveats);
    }
}

public sealed class HealthGetStatusHandlerTests
{
    private static readonly DateTime Now = Fixtures.T0.AddMinutes(10);

    private static HealthGetStatus.IRepository.HealthFacts Facts(
        RunStatus? status = RunStatus.Running,
        DateTime? lastClickIssuedAt = null,
        DateTime? lastPollSuccessAt = null,
        int pollFailures = 0,
        int overdue = 0,
        HealthGetStatus.RecentCounts? recent = null,
        params HealthGetStatus.OpenEvent[] open)
        => new(status is null ? null : 1, status, status is null ? null : Fixtures.T0, 0, lastClickIssuedAt ?? Now.AddSeconds(-3), Now.AddSeconds(-3), Now.AddSeconds(-30),
            lastPollSuccessAt ?? Now.AddSeconds(-2), lastPollSuccessAt ?? Now.AddSeconds(-2), pollFailures, true, 5, overdue, 0,
            recent ?? new HealthGetStatus.RecentCounts(0, 0, 0, 0, 0, 0), open);

    private static HealthGetStatus.OpenEvent Open(LoopKind loop, string code)
        => new(1, 1, loop, LoopEventKind.Api, code, "msg", null, Now.AddSeconds(-5), null, code != AffiliateServiceErrorCodes.Unauthorized);

    private static async Task<HealthGetStatus.Result> Status(HealthGetStatus.IRepository.HealthFacts facts, bool beating = true)
    {
        var repository = new Mock<HealthGetStatus.IRepository>();
        repository.Setup(r => r.GetHealthFacts("fp", It.IsAny<DateTime>(), It.IsAny<DateTime>(), Now, It.IsAny<CancellationToken>())).ReturnsAsync(facts);
        var heartbeat = new Mock<ILoopHeartbeat>();
        if (beating)
        {
            heartbeat.Setup(h => h.Last(LoopKind.Click)).Returns(new LoopBeat(Now.AddSeconds(-2), "Routed"));
            heartbeat.Setup(h => h.Last(LoopKind.Poll)).Returns(new LoopBeat(Now.AddSeconds(-1), "polled"));
        }
        var credentials = new Mock<ICredentialContext>();
        credentials.Setup(c => c.Fingerprint).Returns("fp");

        return await new HealthGetStatus.QueryHandler(repository.Object, heartbeat.Object, credentials.Object, Clock.At(Now)).Handle(new HealthGetStatus.Query(), CancellationToken.None);
    }

    [Fact]
    public async Task Healthy_WhenEverythingFlows()
    {
        var result = await Status(Facts());

        Assert.Equal(HealthGetStatus.Diagnosis.Healthy, result.Diagnosis);
        Assert.Equal(2, result.ClickLoop!.AgeSeconds);
        Assert.Equal(Now, result.ServerNow);
    }

    [Fact]
    public async Task Unauthorized_TakesPrecedenceOverEverything()
        => Assert.Equal(HealthGetStatus.Diagnosis.Unauthorized, (await Status(Facts(open: Open(LoopKind.Poll, AffiliateServiceErrorCodes.Unauthorized)), beating: false)).Diagnosis);

    [Fact]
    public async Task AppNotTicking_WhenLoopsHaveNoRecentBeat()
        => Assert.Equal(HealthGetStatus.Diagnosis.AppNotTicking, (await Status(Facts(), beating: false)).Diagnosis);

    [Fact]
    public async Task TrafficStalled_WhenNoClickForThirtySecondsOrThrottled()
    {
        Assert.Equal(HealthGetStatus.Diagnosis.TrafficStalled, (await Status(Facts(lastClickIssuedAt: Now.AddSeconds(-45)))).Diagnosis);
        Assert.Equal(HealthGetStatus.Diagnosis.TrafficStalled, (await Status(Facts(open: Open(LoopKind.Click, AffiliateServiceErrorCodes.RateLimited)))).Diagnosis);
    }

    [Fact]
    public async Task FeedStalled_WhenPollsFailOrDisclosuresAreOverdue()
    {
        Assert.Equal(HealthGetStatus.Diagnosis.FeedStalled, (await Status(Facts(open: Open(LoopKind.Poll, AffiliateServiceErrorCodes.RateLimited)))).Diagnosis);
        Assert.Equal(HealthGetStatus.Diagnosis.FeedStalled, (await Status(Facts(lastPollSuccessAt: Now.AddSeconds(-60)))).Diagnosis);
        var overdue = await Status(Facts(overdue: 2));
        Assert.Equal(HealthGetStatus.Diagnosis.FeedStalled, overdue.Diagnosis);
        Assert.Contains("2 routed click", overdue.Summary);
    }

    [Fact]
    public async Task ModelFaulting_WhenModelsThrowOrRouteOutsideCoverage()
        => Assert.Equal(HealthGetStatus.Diagnosis.ModelFaulting, (await Status(Facts(recent: new HealthGetStatus.RecentCounts(0, 0, 0, 1, 0, 0)))).Diagnosis);

    [Fact]
    public async Task PausedAndNoRun_AreReportedAsSuch()
    {
        Assert.Equal(HealthGetStatus.Diagnosis.Paused, (await Status(Facts(status: RunStatus.Paused))).Diagnosis);
        Assert.Equal(HealthGetStatus.Diagnosis.NoActiveRun, (await Status(Facts(status: null))).Diagnosis);
    }
}
