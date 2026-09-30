using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Repository.Entities;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Exceptions;
using ClickEntity = AffiliateOptimizer.Repository.Entities.Click;

namespace AffiliateOptimizer.Tests.Unit.Repository.Entities;

public sealed class RunEntityTests
{
    private static Run NewRun(bool interleaved = false) => Run.Start(new RunsStart.IRepository.RunWriteModel(
        new ModelAssignment(["random", "epsilon-greedy"], interleaved), "{}", 10, "fp", "1.0", Fixtures.T0));

    [Fact]
    public void Start_IsRunningAndKeepsTheAssignment()
    {
        var run = NewRun(interleaved: true);

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(["random", "epsilon-greedy"], run.Assignment.Names);
        Assert.True(run.Assignment.Interleaved);
        Assert.Equal(Fixtures.T0, run.StartedAt);
    }

    [Fact]
    public void Pause_Resume_Stop_FollowTheStateMachine()
    {
        var run = NewRun();

        Assert.Throws<LogicErrorException>(() => run.Resume(Fixtures.T0));
        run.Pause(Fixtures.T0.AddMinutes(1));
        Assert.Equal(RunStatus.Paused, run.Status);
        Assert.Equal(Fixtures.T0.AddMinutes(1), run.PausedAt);
        Assert.Throws<LogicErrorException>(() => run.Pause(Fixtures.T0));

        run.Resume(Fixtures.T0.AddMinutes(2));
        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Null(run.PausedAt);

        run.Stop(Fixtures.T0.AddMinutes(3));
        Assert.Equal(RunStatus.Stopped, run.Status);
        Assert.Equal(Fixtures.T0.AddMinutes(3), run.StoppedAt);
        Assert.Throws<LogicErrorException>(() => run.Stop(Fixtures.T0));
        Assert.Throws<LogicErrorException>(() => run.Resume(Fixtures.T0));
    }

    [Fact]
    public void AllocateOrdinal_CountsFromZero()
    {
        var run = NewRun();

        Assert.Equal(0, run.AllocateOrdinal());
        Assert.Equal(1, run.AllocateOrdinal());
        Assert.Equal(2, run.ClickOrdinal);
    }

    [Fact]
    public void ClickFailures_CountConsecutivelyAndReset()
    {
        var run = NewRun();

        Assert.Equal(1, run.RecordClickFailure());
        Assert.Equal(2, run.RecordClickFailure());
        run.ResetClickFailures();
        Assert.Equal(0, run.ConsecutiveClickFailures);
    }

    [Fact]
    public void ToActiveRun_ProjectsEveryField()
    {
        var run = NewRun(interleaved: true);
        run.RecordClickFailure();

        var active = run.ToActiveRun();

        Assert.Equal(RunStatus.Running, active.Status);
        Assert.Equal(TimeSpan.FromMinutes(10), active.EvidenceWindow);
        Assert.Equal(1, active.ConsecutiveClickFailures);
        Assert.Equal("fp", active.CredentialFingerprint);
        Assert.True(active.Assignment.Interleaved);
    }
}

public sealed class ClickEntityTests
{
    private static ClickEntity Issue() => ClickEntity.Issue(1, 0, new AffiliateOptimizer.Domain.Common.AffiliateService.Click("clk_1", 42, Fixtures.T0, Fixtures.T0.AddSeconds(120)), "Merchant 42");

    [Fact]
    public void Issue_IsAnUnroutedRow()
    {
        var click = Issue();

        Assert.Equal("clk_1", click.ClickId);
        Assert.Null(click.RoutedAt);
        Assert.Null(click.ErrorCode);
        Assert.Equal(Fixtures.T0.AddSeconds(120), click.ToFact().ExpiresAt);
    }

    [Fact]
    public void RecordRejection_SpendsTheClickWithTheCode()
    {
        var click = Issue();

        click.RecordRejection("net-b", "B", Fixtures.T0.AddSeconds(1), "MERCHANT_NOT_SUPPORTED", 1);

        var fact = click.ToFact();
        Assert.Equal("MERCHANT_NOT_SUPPORTED", fact.ErrorCode);
        Assert.Equal(Fixtures.T0.AddSeconds(1), fact.RoutedAt);
        Assert.Equal("net-b", click.RoutedNetworkId);
    }

    [Fact]
    public void ApplyDisclosure_IsAuthoritativeAndIdempotent()
    {
        var click = Issue();
        click.MarkUnconfirmed(3);

        click.ApplyDisclosure("net-a", "A", Fixtures.T0.AddSeconds(2), 0.5713m, null, Fixtures.T0.AddSeconds(62));
        click.ApplyDisclosure("net-a", "A", Fixtures.T0.AddSeconds(2), 0.5713m, null, Fixtures.T0.AddSeconds(62));

        Assert.False(click.RouteUnconfirmed);
        Assert.Equal(0.5713m, click.Commission);
        Assert.Equal(Fixtures.T0.AddSeconds(62), click.DisclosedAt);
        Assert.Equal("net-a", click.RoutedNetworkId);
    }
}

public sealed class FeedCursorAndLoopEventTests
{
    [Fact]
    public void FeedCursor_FailuresCountUntilASuccess()
    {
        var cursor = FeedCursor.Create("fp");

        Assert.Equal(1, cursor.RecordPollFailure(Fixtures.T0, resetCursor: false));
        Assert.Equal(2, cursor.RecordPollFailure(Fixtures.T0, resetCursor: false));
        cursor.RecordPollSuccess("c1", Fixtures.T0.AddSeconds(2));
        Assert.Equal(0, cursor.ConsecutiveFailures);
        Assert.Equal("c1", cursor.Cursor);

        cursor.RecordPollFailure(Fixtures.T0.AddSeconds(4), resetCursor: true);
        Assert.Null(cursor.Cursor);
    }

    [Fact]
    public void LoopEvent_UnauthorizedIsNotDismissible()
    {
        var loopEvent = LoopEvent.Create(LoopEventModel.Api(1, LoopKind.Poll, AffiliateServiceErrorCodes.Unauthorized, "bad credentials", Fixtures.T0, null));

        Assert.False(loopEvent.Dismissible);
        Assert.Throws<LogicErrorException>(() => loopEvent.Dismiss(Fixtures.T0));
        Assert.True(loopEvent.IsOpen);

        loopEvent.Resolve(Fixtures.T0.AddSeconds(5));
        Assert.False(loopEvent.IsOpen);
    }

    [Fact]
    public void LoopEvent_RateLimitedCanBeDismissed()
    {
        var loopEvent = LoopEvent.Create(LoopEventModel.Api(1, LoopKind.Click, AffiliateServiceErrorCodes.RateLimited, "slow down", Fixtures.T0, Fixtures.T0.AddSeconds(4)));

        loopEvent.Dismiss(Fixtures.T0.AddSeconds(1));

        Assert.False(loopEvent.IsOpen);
        Assert.Equal(Fixtures.T0.AddSeconds(4), loopEvent.RetryAt);
    }
}
