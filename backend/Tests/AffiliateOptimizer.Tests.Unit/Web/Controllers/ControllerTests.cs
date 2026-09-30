using System.Reflection;
using AffiliateOptimizer.Domain.Features.LoopEvents.Dismiss;
using AffiliateOptimizer.Domain.Features.Models.GetAll;
using AffiliateOptimizer.Domain.Features.Runs.GetCurrent;
using AffiliateOptimizer.Domain.Features.Runs.Pause;
using AffiliateOptimizer.Domain.Features.Runs.Resume;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Domain.Features.Runs.Stop;
using AffiliateOptimizer.Tests.Unit.Support;
using AffiliateOptimizer.Web.Configuration;
using AffiliateOptimizer.Web.Features.Authentication;
using AffiliateOptimizer.Web.Features.LoopEvents;
using AffiliateOptimizer.Web.Features.Models;
using AffiliateOptimizer.Web.Features.Runs;
using Common.Domain.Lists;
using Microsoft.AspNetCore.Authorization;

namespace AffiliateOptimizer.Tests.Unit.Web.Controllers;

public sealed class RunsControllerTests : ControllerTestsBase
{
    [Fact]
    public Task Start_WrapsTheCommand()
    {
        var command = new RunsStart.Command(["random"], false, null);
        return CheckIfControllerIsWrappingRequest(command, new RunsStart.Result(1), d => new RunsController(d).Start(command, CancellationToken.None));
    }

    [Fact]
    public Task Pause_WrapsTheCommand()
        => CheckIfControllerIsWrappingCommand(new RunsPause.Command(1), d => new RunsController(d).Pause(new RunsPause.Command(1), CancellationToken.None));

    [Fact]
    public Task Resume_WrapsTheCommand()
        => CheckIfControllerIsWrappingCommand(new RunsResume.Command(1), d => new RunsController(d).Resume(new RunsResume.Command(1), CancellationToken.None));

    [Fact]
    public Task Stop_WrapsTheCommand()
        => CheckIfControllerIsWrappingCommand(new RunsStop.Command(1), d => new RunsController(d).Stop(new RunsStop.Command(1), CancellationToken.None));

    [Fact]
    public Task GetCurrent_SendsTheQuery()
        => CheckIfControllerIsWrappingRequest(new RunsGetCurrent.Query(), new RunsGetCurrent.Result(null, Fixtures.T0), d => new RunsController(d).GetCurrent(CancellationToken.None));
}

public sealed class ModelsControllerTests : ControllerTestsBase
{
    [Fact]
    public Task GetAll_SendsTheListQuery()
        => CheckIfControllerIsWrappingRequest(new ListQuery<ModelsGetAll.Model>(), new List<ModelsGetAll.Model>(), d => new ModelsController(d).GetAll(CancellationToken.None));
}

public sealed class LoopEventsControllerTests : ControllerTestsBase
{
    [Fact]
    public Task Dismiss_WrapsTheCommand()
        => CheckIfControllerIsWrappingCommand(new LoopEventsDismiss.Command(4), d => new LoopEventsController(d).Dismiss(new LoopEventsDismiss.Command(4), CancellationToken.None));
}

public sealed class AuthorizationAttributeTests
{
    [Theory]
    [InlineData(typeof(RunsController))]
    [InlineData(typeof(ModelsController))]
    [InlineData(typeof(LoopEventsController))]
    public void ApiControllers_RequireTheOperatorsPolicy(Type controller)
    {
        var authorize = controller.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Policies.Operators, authorize.Policy);
    }

    [Fact]
    public void Login_IsAnonymous_LogoutIsNot()
    {
        var login = typeof(AuthenticationController).GetMethods().Where(m => m.Name == nameof(AuthenticationController.Login)).ToList();

        Assert.Equal(2, login.Count);
        Assert.All(login, m => Assert.NotNull(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        Assert.Null(typeof(AuthenticationController).GetMethod(nameof(AuthenticationController.Logout))!.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
