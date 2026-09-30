using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Domain.Features.LoopEvents.Dismiss;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Domain.Features.Runs.GetCurrent;
using AffiliateOptimizer.Domain.Features.Runs.Pause;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Domain.Features.Runs.Stop;
using AffiliateOptimizer.Repository;
using AffiliateOptimizer.Repository.Entities;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AffiliateOptimizer.Tests.Unit.Repository.Features;

public sealed class RunsRepositoryTests
{
    private static readonly RunsStart.IRepository.RunWriteModel Model = new(new ModelAssignment(["random"], false), "{}", 10, "fp", "1.0", Fixtures.T0);
    private static readonly IReadOnlyList<Merchant> Merchants = [new(1, "Alpha"), new(2, "Beta")];

    [Fact]
    public async Task CreateRun_StoresTheRunWithItsSnapshots()
    {
        using var provider = InMemoryDataHelper.CreateProvider();
        int id;
        using (var scope = provider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<RunsStart.IRepository>();
            Assert.False(await repository.HasActiveRun(CancellationToken.None));
            id = await repository.CreateRun(Model, Fixtures.Networks, Merchants, CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = await context.Set<Run>().Include(r => r.Networks).Include(r => r.Merchants).SingleAsync(r => r.Id == id);

            Assert.Equal(RunStatus.Running, run.Status);
            Assert.Equal(3, run.Networks.Count);
            Assert.Equal(2, run.Merchants.Count);
            var netB = run.Networks.Single(n => n.NetworkId == "net-b");
            Assert.Equal(0.6m, netB.AdvertisedRate);
            Assert.Equal([3, 4], netB.ToNetwork().SupportedMerchantIds);
            Assert.Equal(2, netB.CoverageCount);
        }
    }

    [Fact]
    public async Task GetActiveRun_ReturnsTheRunningRunAndTransitionsPersist()
    {
        using var provider = InMemoryDataHelper.CreateProvider();
        int id;
        using (var scope = provider.CreateScope())
            id = await scope.ServiceProvider.GetRequiredService<RunsStart.IRepository>().CreateRun(Model, Fixtures.Networks, Merchants, CancellationToken.None);

        using (var scope = provider.CreateScope())
        {
            var active = await scope.ServiceProvider.GetRequiredService<RunsGetCurrent.IRepository>().GetActiveRun(CancellationToken.None);
            Assert.NotNull(active);
            Assert.Equal(id, active.Id);
            Assert.Equal(["random"], active.Assignment.Names);
            Assert.True(await scope.ServiceProvider.GetRequiredService<RunsStart.IRepository>().HasActiveRun(CancellationToken.None));
        }

        using (var scope = provider.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<RunsPause.IRepository>().Pause(id, Fixtures.T0.AddMinutes(1), CancellationToken.None));
            Assert.False(await scope.ServiceProvider.GetRequiredService<RunsPause.IRepository>().Pause(999, Fixtures.T0, CancellationToken.None));
        }

        using (var scope = provider.CreateScope())
        {
            var active = await scope.ServiceProvider.GetRequiredService<RunsGetCurrent.IRepository>().GetActiveRun(CancellationToken.None);
            Assert.Equal(RunStatus.Paused, active!.Status);
            await Assert.ThrowsAsync<LogicErrorException>(() => scope.ServiceProvider.GetRequiredService<RunsPause.IRepository>().Pause(id, Fixtures.T0, CancellationToken.None));
        }

        using (var scope = provider.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<RunsStop.IRepository>().Stop(id, Fixtures.T0.AddMinutes(2), CancellationToken.None));

        using (var scope = provider.CreateScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<RunsGetCurrent.IRepository>().GetActiveRun(CancellationToken.None));
            Assert.False(await scope.ServiceProvider.GetRequiredService<RunsStart.IRepository>().HasActiveRun(CancellationToken.None));
        }
    }
}

public sealed class LoopEventsRepositoryTests
{
    [Fact]
    public async Task ResolveOpen_ResolvesOnlyThatLoopsOpenEvents()
    {
        using var provider = InMemoryDataHelper.CreateProvider();
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ILoopEventWriter>();
        await writer.Add(LoopEventModel.Api(null, LoopKind.Click, "RATE_LIMITED", "slow down", Fixtures.T0, Fixtures.T0.AddSeconds(4)), CancellationToken.None);
        await writer.Add(LoopEventModel.Transport(null, LoopKind.Poll, "down", Fixtures.T0, null), CancellationToken.None);

        var resolved = await writer.ResolveOpen(LoopKind.Click, Fixtures.T0.AddSeconds(5), CancellationToken.None);

        Assert.Equal(1, resolved);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = await context.Set<LoopEvent>().OrderBy(e => e.Id).ToListAsync();
        Assert.False(events[0].IsOpen);
        Assert.True(events[1].IsOpen);
        Assert.Equal(0, await writer.ResolveOpen(LoopKind.Click, Fixtures.T0.AddSeconds(6), CancellationToken.None));
    }

    [Fact]
    public async Task Dismiss_UnknownReturnsFalse_NonDismissibleThrows()
    {
        using var provider = InMemoryDataHelper.CreateProvider();
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ILoopEventWriter>();
        await writer.Add(LoopEventModel.Api(null, LoopKind.Poll, AffiliateServiceErrorCodes.Unauthorized, "bad credentials", Fixtures.T0, null), CancellationToken.None);
        var repository = scope.ServiceProvider.GetRequiredService<LoopEventsDismiss.IRepository>();

        Assert.False(await repository.Dismiss(999, Fixtures.T0, CancellationToken.None));
        await Assert.ThrowsAsync<LogicErrorException>(() => repository.Dismiss(1, Fixtures.T0, CancellationToken.None));
    }
}
