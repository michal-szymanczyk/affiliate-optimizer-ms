using System.Reflection;
using AffiliateOptimizer.Domain.Features.Clicks.GetCounters;
using AffiliateOptimizer.Domain.Features.Clicks.List;
using AffiliateOptimizer.Domain.Features.Evaluation.Compare;
using AffiliateOptimizer.Domain.Features.Health.GetStatus;
using AffiliateOptimizer.Domain.Features.Models.GetAll;
using AffiliateOptimizer.Domain.Features.Networks.GetInsight;
using AffiliateOptimizer.Tests.Unit.Support;
using AffiliateOptimizer.Web;
using AffiliateOptimizer.Web.Features.Clicks;
using AffiliateOptimizer.Web.Features.Evaluation;
using AffiliateOptimizer.Web.Features.Health;
using AffiliateOptimizer.Web.Features.Networks;
using Common.Domain.Lists;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace AffiliateOptimizer.Tests.Unit.Web;

/// <summary>
/// The container built from the real Startup resolves a handler for every request type in the Domain and an
/// implementation for every feature repository contract (ArchitectureSimple.md §12, "Composition"). No database
/// or network is touched: resolving a handler constructs it, nothing more.
/// </summary>
public sealed class CompositionTests
{
    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:App"] = "Server=(localdb)\\MSSQLLocalDB;Database=AffiliateOptimizer_CompositionTest;Trusted_Connection=True",
            ["AffiliateService:ServerUrl"] = "http://affiliate.test/",
            ["AffiliateService:AppKey"] = "ak_test",
            ["AffiliateService:AppSecret"] = "as_test",
            ["Operator:Password"] = "x",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        new Startup(configuration, new TestEnvironment()).ConfigureServices(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static IEnumerable<Type> DomainRequestTypes()
        => typeof(AffiliateOptimizer.Domain.ServiceExtension).Assembly.DefinedTypes
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && t.ImplementedInterfaces.Any(IsRequestInterface))
            .Select(t => t.AsType());

    private static bool IsRequestInterface(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequest<>);

    public static TheoryData<Type> RequestTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in DomainRequestTypes()) data.Add(type);
        data.Add(typeof(ListQuery<ModelsGetAll.Model>));
        return data;
    }

    [Theory]
    [MemberData(nameof(RequestTypes))]
    public void EveryRequest_HasExactlyOneHandlerThatResolves(Type requestType)
    {
        using var provider = Build();
        using var scope = provider.CreateScope();
        var responseType = requestType.GetInterfaces().Single(IsRequestInterface).GetGenericArguments()[0];
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);

        var handlers = scope.ServiceProvider.GetServices(handlerType).ToList();

        Assert.Single(handlers);
    }

    [Fact]
    public void EveryFeatureRepositoryContract_HasAnImplementation()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();
        var contracts = typeof(AffiliateOptimizer.Domain.ServiceExtension).Assembly.DefinedTypes
            .Where(t => t.IsInterface && t != typeof(IFeatureRepository) && typeof(IFeatureRepository).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(contracts);
        Assert.All(contracts, contract => Assert.NotNull(scope.ServiceProvider.GetService(contract)));
    }

    [Fact]
    public void ThereAreAtLeastFourteenRequestTypes() => Assert.True(DomainRequestTypes().Count() >= 14);

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "AffiliateOptimizer.Web";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class ReadSideControllerTests : ControllerTestsBase
{
    [Fact]
    public Task Clicks_GetAll_WrapsTheQuery()
    {
        var query = new ClicksList.Query { RunId = 1 };
        return CheckIfControllerIsWrappingRequest(query, new ClicksList.Result(new PagedList<ClicksList.Model>([], 0, 40, 0), Fixtures.T0), d => new ClicksController(d).GetAll(query, CancellationToken.None));
    }

    [Fact]
    public Task Clicks_GetCounters_WrapsTheQuery()
    {
        var query = new ClicksGetCounters.Query(1);
        return CheckIfControllerIsWrappingRequest(query, new ClicksGetCounters.Result(0, 0, 0, 0, 0, 0, 0m, null, 0, null, Fixtures.T0), d => new ClicksController(d).GetCounters(query, CancellationToken.None));
    }

    [Fact]
    public Task Networks_GetInsight_WrapsTheQuery()
    {
        var query = new NetworksGetInsight.Query(null);
        return CheckIfControllerIsWrappingRequest(query, new NetworksGetInsight.Result([], null, 10, Fixtures.T0), d => new NetworksController(d).GetInsight(query, CancellationToken.None));
    }

    [Fact]
    public Task Evaluation_Compare_WrapsTheQuery()
    {
        var query = new EvaluationCompare.Query(2);
        var result = new EvaluationCompare.Result(2, true, Fixtures.T0, null, [], EvaluationCompare.Verdict.Inconclusive, null, [], 30, Fixtures.T0);
        return CheckIfControllerIsWrappingRequest(query, result, d => new EvaluationController(d).Compare(query, CancellationToken.None));
    }

    [Fact]
    public Task Health_GetStatus_SendsTheQuery()
    {
        var result = new HealthGetStatus.Result
        {
            Diagnosis = HealthGetStatus.Diagnosis.NoActiveRun, Summary = "s", RunId = null, RunStatus = null, RunStartedAt = null,
            LastClickIssuedAt = null, LastRouteAt = null, LastDisclosureAt = null, LastPollAt = null, LastPollSuccessAt = null, HasCursor = false,
            ClickConsecutiveFailures = 0, PollConsecutiveFailures = 0, AwaitingDisclosure = 0, OverduePending = 0, OrphanDisclosures = 0,
            Recent = new HealthGetStatus.RecentCounts(0, 0, 0, 0, 0, 0), ClickLoop = null, PollLoop = null, OpenEvents = [], ServerNow = Fixtures.T0,
        };
        return CheckIfControllerIsWrappingRequest(new HealthGetStatus.Query(), result, d => new HealthController(d).GetStatus(CancellationToken.None));
    }
}
