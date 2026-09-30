using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Common.Optimization.Models;
using AffiliateOptimizer.Domain.Features.Clicks.IssueAndRoute;
using AffiliateOptimizer.Domain.Features.Clicks.Route;
using AffiliateOptimizer.Domain.Features.Commissions.Poll;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Repository;
using AffiliateOptimizer.Repository.Entities;
using AffiliateOptimizer.Tests.Unit.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClickEntity = AffiliateOptimizer.Repository.Entities.Click;

namespace AffiliateOptimizer.Tests.Unit.Repository.Features;

/// <summary>The click loop's persistence, exercised end to end over the in-memory provider: issue → decide → route → disclose.</summary>
public sealed class ClicksRepositoryTests : IDisposable
{
    private static readonly DateTime T0 = Fixtures.T0;
    private static readonly RandomModel Random = new();
    private readonly ServiceProvider _provider = InMemoryDataHelper.CreateProvider();
    private readonly int _runId;

    public ClicksRepositoryTests()
    {
        using var scope = _provider.CreateScope();
        var model = new RunsStart.IRepository.RunWriteModel(new ModelAssignment(["random", "advertised-greedy"], true), "{}", 10, "fp", "1.0", T0);
        _runId = scope.ServiceProvider.GetRequiredService<RunsStart.IRepository>()
            .CreateRun(model, Fixtures.Networks, [new Merchant(3, "Acme")], CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose() => _provider.Dispose();

    private T Get<T>(IServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    private static AffiliateOptimizer.Domain.Common.AffiliateService.Click Click(string id, int merchantId = 3) => new(id, merchantId, T0, T0.AddSeconds(120));

    [Fact]
    public async Task IssueClick_AllocatesOrdinalsAndNamesTheMerchantFromTheSnapshot()
    {
        using var scope = _provider.CreateScope();
        var repository = Get<ClicksIssueAndRoute.IRepository>(scope);

        var first = await repository.IssueClick(new ClicksIssueAndRoute.IRepository.IssueClickModel(_runId, Click("clk_1"), T0), CancellationToken.None);
        var second = await repository.IssueClick(new ClicksIssueAndRoute.IRepository.IssueClickModel(_runId, Click("clk_2", merchantId: 7), T0.AddSeconds(3)), CancellationToken.None);

        Assert.Equal(0, first.Ordinal);
        Assert.Equal("Acme", first.MerchantName);
        Assert.Equal(1, second.Ordinal);
        Assert.Equal("Merchant 7", second.MerchantName);
        var active = await repository.GetActiveRun(CancellationToken.None);
        Assert.Equal(_runId, active!.Id);
    }

    [Fact]
    public async Task GetEligibleNetworks_UsesTheRunSnapshot()
    {
        using var scope = _provider.CreateScope();

        var eligible = await Get<ClicksIssueAndRoute.IRepository>(scope).GetEligibleNetworks(_runId, 3, CancellationToken.None);

        Assert.Equal(["net-a", "net-b"], eligible.Select(n => n.Id));
        Assert.Equal(0.6m, eligible.Single(n => n.Id == "net-b").AdvertisedRate);
    }

    [Fact]
    public async Task ClickFailures_CountAndReset()
    {
        using var scope = _provider.CreateScope();
        var repository = Get<ClicksIssueAndRoute.IRepository>(scope);

        Assert.Equal(1, await repository.RecordClickFailure(_runId, CancellationToken.None));
        Assert.Equal(2, await repository.RecordClickFailure(_runId, CancellationToken.None));
        await repository.ResetClickFailures(_runId, CancellationToken.None);

        Assert.Equal(0, (await repository.GetActiveRun(CancellationToken.None))!.ConsecutiveClickFailures);
    }

    [Fact]
    public async Task Route_Disclose_Evidence_FlowsThroughTheStore()
    {
        using (var scope = _provider.CreateScope())
        {
            var issue = Get<ClicksIssueAndRoute.IRepository>(scope);
            var route = Get<ClicksRoute.IRepository>(scope);
            var decisionA = new ClicksIssueAndRoute.IRepository.DecisionModel(Random, new Decision("net-a", "why", []), T0);

            foreach (var id in new[] { "clk_a", "clk_b", "clk_c", "clk_d" })
                await issue.IssueClick(new ClicksIssueAndRoute.IRepository.IssueClickModel(_runId, Click(id), T0), CancellationToken.None);

            await issue.RecordDecision("clk_a", decisionA, CancellationToken.None);
            await issue.RecordDecision("clk_b", decisionA, CancellationToken.None);
            await issue.RecordDecision("clk_c", new ClicksIssueAndRoute.IRepository.DecisionModel(new AdvertisedGreedyModel(), new Decision("net-a", "why", []), T0), CancellationToken.None);
            await issue.RecordDecision("clk_d", decisionA, CancellationToken.None);

            var target = await route.GetRouteTarget("clk_a", "net-a", CancellationToken.None);
            Assert.Equal(new ClicksRoute.IRepository.RouteTarget(_runId, "Network A", false), target);
            Assert.Null(await route.GetRouteTarget("nope", "net-a", CancellationToken.None));

            await route.RecordRoute("clk_a", new RouteAcknowledgment("clk_a", "net-a", T0.AddSeconds(1), false, T0.AddSeconds(61)), "Network A", 1, CancellationToken.None);
            await route.RecordRoute("clk_b", new RouteAcknowledgment("clk_b", "net-a", T0.AddSeconds(2), false, T0.AddSeconds(62)), "Network A", 2, CancellationToken.None);
            await route.RecordRoute("clk_c", new RouteAcknowledgment("clk_c", "net-a", T0.AddSeconds(3), false, T0.AddSeconds(63)), "Network A", 1, CancellationToken.None);
            await route.RecordRejection("clk_d", "net-a", "Network A", T0.AddSeconds(4), "MERCHANT_NOT_SUPPORTED", 1, CancellationToken.None);
        }

        using (var scope = _provider.CreateScope())
        {
            var commissions = Get<CommissionsPoll.IRepository>(scope);
            await commissions.GetOrCreateCursor("fp", CancellationToken.None);
            var applied = await commissions.ApplyFeedPage("fp",
            [
                new CommissionRow("clk_a", 3, "net-a", T0.AddSeconds(1), T0.AddSeconds(61), "settlement", "settled", 0.5m, null),
                new CommissionRow("clk_c", 3, "net-a", T0.AddSeconds(3), T0.AddSeconds(63), "settlement", "settled", 0.9m, null),
                new CommissionRow("clk_d", 3, "net-a", T0.AddSeconds(4), T0.AddSeconds(4), "rejection", "rejected", null, "MERCHANT_NOT_SUPPORTED"),
                new CommissionRow("clk_ghost", 9, "net-c", T0, T0.AddSeconds(60), "settlement", "settled", 0.1m, null),
            ], "cursor-2", T0.AddSeconds(70), CancellationToken.None);

            Assert.Equal(new CommissionsPoll.IRepository.FeedPageResult(3, 1), applied);
        }

        using (var scope = _provider.CreateScope())
        {
            var evidence = await Get<ClicksIssueAndRoute.IRepository>(scope).GetEvidence(_runId, "random", T0, T0.AddSeconds(120), CancellationToken.None);

            var netA = evidence["net-a"];
            Assert.Equal(1, netA.SettledCount);           // clk_a only: clk_c belongs to the other arm
            Assert.Equal(0.5m, netA.MeanCommission);
            Assert.Equal(1, netA.RecentCount);
            Assert.Equal(T0.AddSeconds(61), netA.LastDisclosedAt);
            Assert.Equal(1, netA.PendingCount);           // clk_b routed, not yet disclosed
            Assert.Equal(1, netA.RejectedCount);          // clk_d

            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clickD = await context.Set<ClickEntity>().SingleAsync(c => c.ClickId == "clk_d");
            Assert.Equal(ClickState.Rejected, ClickStates.Derive(clickD.ToFact(), T0.AddSeconds(120)));
            Assert.Equal(T0.AddSeconds(4), clickD.DisclosedAt);
            var cursor = await context.Set<FeedCursor>().SingleAsync();
            Assert.Equal("cursor-2", cursor.Cursor);
            Assert.Single(await context.Set<OrphanDisclosure>().ToListAsync());
        }
    }
}

public sealed class CommissionsRepositoryTests
{
    private static readonly DateTime T0 = Fixtures.T0;

    [Fact]
    public async Task ApplyFeedPage_ReplayedRows_AreIdempotent()
    {
        using var provider = InMemoryDataHelper.CreateProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<CommissionsPoll.IRepository>();
        var row = new CommissionRow("clk_ghost", 9, "net-c", T0, T0.AddSeconds(60), "settlement", "settled", 0.1m, null);

        var position = await repository.GetOrCreateCursor("fp", CancellationToken.None);
        var first = await repository.ApplyFeedPage("fp", [row], "c1", T0, CancellationToken.None);
        var second = await repository.ApplyFeedPage("fp", [row], "c1", T0.AddSeconds(2), CancellationToken.None);

        Assert.Null(position.Cursor);
        Assert.Equal(1, first.Orphans);
        Assert.Equal(0, second.Orphans);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await context.Set<OrphanDisclosure>().ToListAsync());
        Assert.Equal("c1", (await context.Set<FeedCursor>().SingleAsync()).Cursor);
    }

    [Fact]
    public async Task RecordPollFailure_CountsAndCanResetTheCursor()
    {
        using var provider = InMemoryDataHelper.CreateProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<CommissionsPoll.IRepository>();
        await repository.GetOrCreateCursor("fp", CancellationToken.None);
        await repository.ApplyFeedPage("fp", [], "c1", T0, CancellationToken.None);

        Assert.Equal(1, await repository.RecordPollFailure("fp", T0, resetCursor: false, CancellationToken.None));
        Assert.Equal("c1", (await repository.GetOrCreateCursor("fp", CancellationToken.None)).Cursor);
        Assert.Equal(2, await repository.RecordPollFailure("fp", T0, resetCursor: true, CancellationToken.None));

        var position = await repository.GetOrCreateCursor("fp", CancellationToken.None);
        Assert.Null(position.Cursor);
        Assert.Equal(2, position.ConsecutiveFailures);
    }
}
