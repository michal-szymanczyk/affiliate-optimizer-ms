using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Domain.Features.Runs.GetCurrent;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Domain.Features.Runs.Stop;
using AffiliateOptimizer.Tests.Unit.Support;
using AffiliateOptimizer.Web.Infrastructure.Loops;
using Common.Domain.Requests;
using Common.Domain.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace AffiliateOptimizer.Tests.Unit.Web.Loops;

public sealed class LoopServiceTests
{
    private sealed class ScriptedLoop(IServiceScopeFactory scopes, ILoopHeartbeat heartbeat, TimeProvider timeProvider, ISystemTime time, Func<int, (TimeSpan, string)> script)
        : LoopService(scopes, heartbeat, timeProvider, time, NullLogger.Instance)
    {
        public int Turns;

        protected override LoopKind Kind => LoopKind.Click;

        protected override Task<(TimeSpan NextDelay, string Outcome)> Turn(IDispatcher dispatcher, CancellationToken ct)
        {
            Turns++;
            return Task.FromResult(script(Turns));
        }
    }

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
            await Task.Delay(10);
        }

        await Task.Delay(50);   // let the loop register its next delay before the fake clock moves
    }

    [Fact]
    public async Task ExecuteAsync_WaitsTheReturnedDelay_SurvivesAFailingTurn_AndYieldsOnZero()
    {
        var provider = new ServiceCollection().AddSingleton(Mock.Of<IDispatcher>()).BuildServiceProvider();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(Fixtures.T0));
        var heartbeat = new LoopHeartbeat();
        var time = new Mock<ISystemTime>();
        time.Setup(t => t.Now()).Returns(() => timeProvider.GetUtcNow().UtcDateTime);

        var loop = new ScriptedLoop(provider.GetRequiredService<IServiceScopeFactory>(), heartbeat, timeProvider, time.Object, turn => turn switch
        {
            1 => (TimeSpan.FromSeconds(3), "Routed"),
            2 => throw new InvalidOperationException("database down"),
            3 => (TimeSpan.Zero, "drain"),
            _ => (TimeSpan.FromSeconds(3), "Idle"),
        });

        await loop.StartAsync(CancellationToken.None);
        await WaitFor(() => loop.Turns == 1, "the first turn");
        Assert.Equal("Routed", heartbeat.Last(LoopKind.Click)!.Outcome);

        timeProvider.Advance(TimeSpan.FromSeconds(2.9));
        await Task.Delay(50);
        Assert.Equal(1, loop.Turns);                                   // a fixed delay after the turn, not before

        timeProvider.Advance(TimeSpan.FromSeconds(0.1));
        await WaitFor(() => loop.Turns == 2, "the failing turn");
        Assert.Equal("error", heartbeat.Last(LoopKind.Click)!.Outcome);   // the exception was swallowed and reported

        timeProvider.Advance(TimeSpan.FromSeconds(1));                  // first-failure backoff is one second
        await WaitFor(() => loop.Turns == 4, "the zero-delay drain and the turn after it");
        Assert.Equal("Idle", heartbeat.Last(LoopKind.Click)!.Outcome);

        await loop.StopAsync(CancellationToken.None);
        Assert.Equal(4, loop.Turns);
    }
}

public sealed class LoopStartupServiceTests
{
    private static readonly DateTime T0 = Fixtures.T0;

    private static (LoopStartupService Service, Mock<IDispatcher> Dispatcher, List<LoopEventModel> Events) Build(LoopOptions options, RunsGetCurrent.ActiveRunModel? active)
    {
        var dispatcher = new Mock<IDispatcher>();
        dispatcher.Setup(d => d.Send(It.IsAny<RunsGetCurrent.Query>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RunsGetCurrent.Result(active, T0));
        dispatcher.Setup(d => d.Send(It.IsAny<RunsStop.Command>(), It.IsAny<CancellationToken>())).ReturnsAsync(Common.Domain.Requests.Unit.Value);
        dispatcher.Setup(d => d.Send(It.IsAny<RunsStart.Command>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RunsStart.Result(9));
        var events = new List<LoopEventModel>();
        var writer = new Mock<ILoopEventWriter>();
        writer.Setup(w => w.Add(It.IsAny<LoopEventModel>(), It.IsAny<CancellationToken>())).Callback<LoopEventModel, CancellationToken>((e, _) => events.Add(e)).Returns(Task.CompletedTask);
        var provider = new ServiceCollection().AddSingleton(dispatcher.Object).AddSingleton(writer.Object).BuildServiceProvider();
        var time = new Mock<ISystemTime>();
        time.Setup(t => t.Now()).Returns(T0);
        return (new LoopStartupService(provider.GetRequiredService<IServiceScopeFactory>(), options, time.Object, NullLogger<LoopStartupService>.Instance), dispatcher, events);
    }

    private static RunsGetCurrent.ActiveRunModel Active(RunStatus status) => new(4, status, ["random"], false, T0, null, 10, "fp", "1.0");

    [Fact]
    public async Task StartAsync_ActiveRunAndResumeOn_ContinuesItWithAnInfoEvent()
    {
        var (service, dispatcher, events) = Build(new LoopOptions { ResumeActiveRunOnStartup = true }, Active(RunStatus.Paused));

        await service.StartAsync(CancellationToken.None);

        var loopEvent = Assert.Single(events);
        Assert.Equal(LoopEventCodes.HostRestarted, loopEvent.Code);
        Assert.Contains("continues", loopEvent.Message);
        dispatcher.Verify(d => d.Send(It.IsAny<RunsStop.Command>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ActiveRunAndResumeOff_StopsIt()
    {
        var (service, dispatcher, events) = Build(new LoopOptions { ResumeActiveRunOnStartup = false }, Active(RunStatus.Running));

        await service.StartAsync(CancellationToken.None);

        dispatcher.Verify(d => d.Send(new RunsStop.Command(4), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("stopped", Assert.Single(events).Message);
    }

    [Fact]
    public async Task StartAsync_NoRunAndAutoStartModel_StartsARun()
    {
        var (service, dispatcher, _) = Build(new LoopOptions { AutoStartModel = "epsilon-greedy" }, null);

        await service.StartAsync(CancellationToken.None);

        dispatcher.Verify(d => d.Send(It.Is<RunsStart.Command>(c => c.ModelNames.Count == 1 && c.ModelNames[0] == "epsilon-greedy" && !c.Interleaved), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_Failure_IsLoggedNotFatal()
    {
        var (service, dispatcher, _) = Build(new LoopOptions { AutoStartModel = "random" }, null);
        dispatcher.Setup(d => d.Send(It.IsAny<RunsStart.Command>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("server down"));

        await service.StartAsync(CancellationToken.None);   // does not throw
    }
}
