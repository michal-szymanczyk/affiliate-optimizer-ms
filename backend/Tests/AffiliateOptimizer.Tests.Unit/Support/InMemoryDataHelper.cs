using System.Net;
using System.Text;
using AffiliateOptimizer.Repository;
using Common.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AffiliateOptimizer.Tests.Unit.Support;

/// <summary>
/// Repository tests run the real repository classes, registered by the same LoadFeatureRepositories scan the host
/// uses, over the EF in-memory provider (ArchitectureSimple.md §12). One provider = one isolated database.
/// </summary>
public static class InMemoryDataHelper
{
    public static ServiceProvider CreateProvider(string? databaseName = null)
    {
        var name = databaseName ?? Guid.NewGuid().ToString("N");   // decided once: the options lambda runs per scope
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddRepositoryCore();
        return services.BuildServiceProvider();
    }
}

/// <summary>Scripted HttpMessageHandler: queue responses (or exceptions) and inspect the requests the client sent.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public StubHttpMessageHandler Respond(HttpStatusCode status, string? body, string contentType = "application/json")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
        });
        return this;
    }

    public StubHttpMessageHandler Throw(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (_responses.Count == 0) throw new InvalidOperationException("No stubbed response is left for " + request.RequestUri);
        return _responses.Dequeue()(request);
    }
}

/// <summary>Controller tests only check that an action forwards exactly its request (ArchitectureSimple.md §12).</summary>
public abstract class ControllerTestsBase
{
    protected static async Task CheckIfControllerIsWrappingRequest<TRequest, TResponse>(
        TRequest request, TResponse response, Func<IDispatcher, Task<TResponse>> execute)
        where TRequest : IRequest<TResponse>
    {
        var dispatcher = new Mock<IDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(d => d.Send(request, It.IsAny<CancellationToken>())).ReturnsAsync(response);

        var actual = await execute(dispatcher.Object);

        Assert.Equal(response, actual);
        dispatcher.Verify(d => d.Send(request, It.IsAny<CancellationToken>()), Times.Once);
        dispatcher.VerifyNoOtherCalls();
    }

    protected static async Task CheckIfControllerIsWrappingCommand<TRequest>(TRequest request, Func<IDispatcher, Task> execute)
        where TRequest : IRequest
    {
        var dispatcher = new Mock<IDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(d => d.Send(request, It.IsAny<CancellationToken>())).ReturnsAsync(Common.Domain.Requests.Unit.Value);

        await execute(dispatcher.Object);

        dispatcher.Verify(d => d.Send(request, It.IsAny<CancellationToken>()), Times.Once);
        dispatcher.VerifyNoOtherCalls();
    }
}
