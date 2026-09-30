using AffiliateOptimizer.Domain.Features.LoopEvents.Dismiss;
using AffiliateOptimizer.Domain.Features.Runs.Pause;
using AffiliateOptimizer.Domain.Features.Runs.Resume;
using AffiliateOptimizer.Domain.Features.Runs.Stop;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Exceptions;
using Common.Domain.Time;
using Moq;

namespace AffiliateOptimizer.Tests.Unit.Domain.Features.Runs;

public sealed class RunControlTests
{
    private static ISystemTime Clock()
    {
        var time = new Mock<ISystemTime>();
        time.Setup(t => t.Now()).Returns(Fixtures.T0);
        return time.Object;
    }

    [Fact]
    public async Task Pause_KnownRun_TransitionsAtTheClockTime()
    {
        var repository = new Mock<RunsPause.IRepository>();
        repository.Setup(r => r.Pause(5, Fixtures.T0, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await new RunsPause.CommandHandler(repository.Object, Clock()).Handle(new RunsPause.Command(5), CancellationToken.None);

        repository.VerifyAll();
    }

    [Fact]
    public async Task Pause_UnknownRun_IsNotFound()
    {
        var repository = new Mock<RunsPause.IRepository>();
        repository.Setup(r => r.Pause(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() => new RunsPause.CommandHandler(repository.Object, Clock()).Handle(new RunsPause.Command(5), CancellationToken.None));
    }

    [Fact]
    public async Task Resume_UnknownRun_IsNotFound()
    {
        var repository = new Mock<RunsResume.IRepository>();
        repository.Setup(r => r.Resume(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() => new RunsResume.CommandHandler(repository.Object, Clock()).Handle(new RunsResume.Command(5), CancellationToken.None));
    }

    [Fact]
    public async Task Stop_KnownRun_Transitions()
    {
        var repository = new Mock<RunsStop.IRepository>();
        repository.Setup(r => r.Stop(9, Fixtures.T0, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await new RunsStop.CommandHandler(repository.Object, Clock()).Handle(new RunsStop.Command(9), CancellationToken.None);

        repository.VerifyAll();
    }

    [Fact]
    public async Task Dismiss_UnknownEvent_IsNotFound()
    {
        var repository = new Mock<LoopEventsDismiss.IRepository>();
        repository.Setup(r => r.Dismiss(It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() => new LoopEventsDismiss.CommandHandler(repository.Object, Clock()).Handle(new LoopEventsDismiss.Command(3), CancellationToken.None));
    }

    [Fact]
    public async Task Validators_RejectNonPositiveIds()
    {
        Assert.NotEmpty(await new RunsPause.CommandValidator().Validate(new RunsPause.Command(0)));
        Assert.NotEmpty(await new RunsResume.CommandValidator().Validate(new RunsResume.Command(-1)));
        Assert.NotEmpty(await new RunsStop.CommandValidator().Validate(new RunsStop.Command(0)));
        Assert.NotEmpty(await new LoopEventsDismiss.CommandValidator().Validate(new LoopEventsDismiss.Command(0)));
        Assert.Empty(await new RunsPause.CommandValidator().Validate(new RunsPause.Command(1)));
    }
}
