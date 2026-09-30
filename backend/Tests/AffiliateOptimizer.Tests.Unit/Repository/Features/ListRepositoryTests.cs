using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Common.Optimization.Models;
using AffiliateOptimizer.Domain.Features.Clicks.GetCounters;
using AffiliateOptimizer.Domain.Features.Clicks.IssueAndRoute;
using AffiliateOptimizer.Domain.Features.Clicks.List;
using AffiliateOptimizer.Domain.Features.Clicks.Route;
using AffiliateOptimizer.Domain.Features.Commissions.Poll;
using AffiliateOptimizer.Domain.Features.Evaluation.Compare;
using AffiliateOptimizer.Domain.Features.Networks.GetInsight;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Domain.Features.Runs.GetAll;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Domain.Features.Runs.Stop;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Lists;
using Microsoft.Extensions.DependencyInjection;

namespace AffiliateOptimizer.Tests.Unit.Repository.Features;

/// <summary>A small seeded store: five clicks in every state, so list, counter, insight and history queries can be checked together.</summary>
public sealed class SeededStoreTests : IDisposable
{
    private static readonly DateTime T0 = Fixtures.T0;
    private static readonly DateTime Now = T0.AddMinutes(5);
    private readonly ServiceProvider _provider = InMemoryDataHelper.CreateProvider();
    private readonly int _runId;

    public SeededStoreTests()
    {
        using var scope = _provider.CreateScope();
        var services = scope.ServiceProvider;
        var runs = services.GetRequiredService<RunsStart.IRepository>();
        _runId = runs.CreateRun(new RunsStart.IRepository.RunWriteModel(new ModelAssignment(["random", "advertised-greedy"], true), "{}", 10, "fp", "1.0", T0),
            Fixtures.Networks, [new Merchant(3, "Acme Tools"), new Merchant(4, "Beta Bikes"), new Merchant(42, "Zeta Zoo")], CancellationToken.None).GetAwaiter().GetResult();

        var issue = services.GetRequiredService<ClicksIssueAndRoute.IRepository>();
        var route = services.GetRequiredService<ClicksRoute.IRepository>();
        var poll = services.GetRequiredService<CommissionsPoll.IRepository>();

        Task Issue(string id, int merchant, int secondsAfterT0, string model)
            => issue.IssueClick(new ClicksIssueAndRoute.IRepository.IssueClickModel(_runId, new AffiliateOptimizer.Domain.Common.AffiliateService.Click(id, merchant, T0.AddSeconds(secondsAfterT0), T0.AddSeconds(secondsAfterT0 + 120)), T0.AddSeconds(secondsAfterT0)), CancellationToken.None)
                .ContinueWith(_ => issue.RecordDecision(id, new ClicksIssueAndRoute.IRepository.DecisionModel(model == "random" ? new RandomModel() : new AdvertisedGreedyModel(), new Decision("net-a", "why", []), T0.AddSeconds(secondsAfterT0)), CancellationToken.None)).Unwrap();

        // clk_1 settled (0.5), clk_2 routed and pending, clk_3 rejected, clk_4 lapsed (never routed, expired), clk_5 still live (issued)
        Issue("clk_1", 3, 0, "random").GetAwaiter().GetResult();
        Issue("clk_2", 4, 10, "advertised-greedy").GetAwaiter().GetResult();
        Issue("clk_3", 42, 20, "random").GetAwaiter().GetResult();
        Issue("clk_4", 3, 30, "advertised-greedy").GetAwaiter().GetResult();
        Issue("clk_5", 4, 290, "random").GetAwaiter().GetResult();

        route.RecordRoute("clk_1", new RouteAcknowledgment("clk_1", "net-a", T0.AddSeconds(1), false, T0.AddSeconds(61)), "Network A", 1, CancellationToken.None).GetAwaiter().GetResult();
        route.RecordRoute("clk_2", new RouteAcknowledgment("clk_2", "net-b", T0.AddSeconds(11), false, T0.AddSeconds(71)), "Network B", 1, CancellationToken.None).GetAwaiter().GetResult();
        route.RecordRejection("clk_3", "net-c", "Network C", T0.AddSeconds(21), "MERCHANT_NOT_SUPPORTED", 1, CancellationToken.None).GetAwaiter().GetResult();
        poll.GetOrCreateCursor("fp", CancellationToken.None).GetAwaiter().GetResult();
        poll.ApplyFeedPage("fp",
        [
            new CommissionRow("clk_1", 3, "net-a", T0.AddSeconds(1), T0.AddSeconds(61), "settlement", "settled", 0.5m, null),
            new CommissionRow("clk_2", 4, "net-b", T0.AddSeconds(11), Now.AddSeconds(30), "settlement", "settled", 0.7m, null),   // disclosed in the future: still routed at Now
        ], "c1", T0.AddSeconds(62), CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose() => _provider.Dispose();

    private T Get<T>(IServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    private Task<PagedList<ClicksList.Model>> List(IServiceScope scope, ClicksList.Query query)
        => Get<ClicksList.IRepository>(scope).GetClicks(query, query, Now, CancellationToken.None);

    [Fact]
    public async Task GetClicks_DerivesStateAtServerTimeAndSortsNewestFirst()
    {
        using var scope = _provider.CreateScope();

        var page = await List(scope, new ClicksList.Query { SortBy = "IssuedAt", SortDirection = SortDirection.Desc });

        Assert.Equal(5, page.TotalItems);
        Assert.Equal(["clk_5", "clk_4", "clk_3", "clk_2", "clk_1"], page.Items.Select(c => c.ClickId));
        Assert.Equal([ClickState.Issued, ClickState.Expired, ClickState.Rejected, ClickState.Routed, ClickState.Settled], page.Items.Select(c => c.State));
        Assert.Equal("Acme Tools", page.Items.Last().MerchantName);
        Assert.Equal("random", page.Items.Last().ModelName);
    }

    [Fact]
    public async Task GetClicks_FiltersByStateErrorsNetworkModelAndMerchant()
    {
        using var scope = _provider.CreateScope();

        Assert.Equal(["clk_1"], (await List(scope, new ClicksList.Query { State = ClickState.Settled })).Items.Select(c => c.ClickId));
        Assert.Equal(["clk_4", "clk_3"], (await List(scope, new ClicksList.Query { ErrorsOnly = true, SortBy = "IssuedAt", SortDirection = SortDirection.Desc })).Items.Select(c => c.ClickId));
        Assert.Equal(["clk_2"], (await List(scope, new ClicksList.Query { NetworkId = "net-b" })).Items.Select(c => c.ClickId));
        Assert.Equal(2, (await List(scope, new ClicksList.Query { ModelName = "advertised-greedy" })).TotalItems);
        Assert.Equal(["clk_5", "clk_2"], (await List(scope, new ClicksList.Query { Merchant = "4", SortBy = "IssuedAt", SortDirection = SortDirection.Desc })).Items.Select(c => c.ClickId));   // exact id 4, never 42
        Assert.Equal(["clk_4", "clk_1"], (await List(scope, new ClicksList.Query { Merchant = "acme", SortBy = "IssuedAt", SortDirection = SortDirection.Desc })).Items.Select(c => c.ClickId));
        Assert.Equal(0, (await List(scope, new ClicksList.Query { RunId = _runId + 1 })).TotalItems);
    }

    [Fact]
    public async Task GetClicks_SortsCommissionWithNullsLastInBothDirectionsAndPages()
    {
        using var scope = _provider.CreateScope();

        var ascending = await List(scope, new ClicksList.Query { SortBy = "commission", SortDirection = SortDirection.Asc });
        var descending = await List(scope, new ClicksList.Query { SortBy = "commission", SortDirection = SortDirection.Desc });
        var page = await List(scope, new ClicksList.Query { SortBy = "IssuedAt", SortDirection = SortDirection.Asc, PageIndex = 1, PageSize = 2 });

        Assert.Equal(["clk_1", "clk_2"], ascending.Items.Take(2).Select(c => c.ClickId));
        Assert.Equal(["clk_2", "clk_1"], descending.Items.Take(2).Select(c => c.ClickId));
        Assert.Null(ascending.Items.Last().Commission);
        Assert.Equal(["clk_3", "clk_4"], page.Items.Select(c => c.ClickId));
        Assert.Equal(5, page.TotalItems);
        Assert.Equal(1, page.PageIndex);
    }

    [Fact]
    public async Task Counters_History_Insight_AndArms_ReadTheSameStore()
    {
        using var scope = _provider.CreateScope();

        var facts = await Get<ClicksGetCounters.IRepository>(scope).GetClickFacts(_runId, CancellationToken.None);
        var counters = RunCounters.Compute(facts, Now);
        Assert.Equal(new RunCounters(5, 1, 1, 1, 1, 3, 0.5m, 0.5m), counters);
        Assert.Equal(T0, await Get<ClicksGetCounters.IRepository>(scope).GetRunStartedAt(null, CancellationToken.None));

        var history = await Get<RunsGetAll.IRepository>(scope).GetRuns(new RunsGetAll.Query { SortBy = "Id", SortDirection = SortDirection.Desc }, Now, CancellationToken.None);
        var run = Assert.Single(history.Items);
        Assert.Equal(5, run.Clicks);
        Assert.Equal(1, run.Settled);
        Assert.Equal(0.5m, run.TotalCommission);
        Assert.Equal(["random", "advertised-greedy"], run.ModelNames);

        var networks = Get<NetworksGetInsight.IRepository>(scope);
        Assert.Equal(_runId, await networks.ResolveRunId(null, CancellationToken.None));
        Assert.Null(await networks.ResolveRunId(999, CancellationToken.None));
        var observations = await networks.GetObservations(_runId, CancellationToken.None);
        Assert.Equal(3, observations.Count);   // clk_1, clk_2, clk_3 have a routed network

        var evaluation = Get<EvaluationCompare.IRepository>(scope);
        var scopeInfo = await evaluation.ResolveRun(null, CancellationToken.None);
        Assert.True(scopeInfo!.Interleaved);
        var arms = await evaluation.GetArmObservations(_runId, CancellationToken.None);
        Assert.Equal(5, arms.Count);
        Assert.Equal(0.4m, arms.Single(a => a.Commission == 0.5m).AdvertisedRateOfChosen);   // net-a advertises 0.4

        await Get<RunsStop.IRepository>(scope).Stop(_runId, Now, CancellationToken.None);
        Assert.Equal(_runId, await networks.ResolveRunId(null, CancellationToken.None));       // falls back to the latest run
    }
}
