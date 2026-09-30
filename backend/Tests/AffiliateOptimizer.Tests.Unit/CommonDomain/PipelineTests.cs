using Common.Domain.Exceptions;
using Common.Domain.Lists;
using Common.Domain.Requests;
using Common.Domain.Validation;
using FluentValidation;
using ValidationException = Common.Domain.Exceptions.ValidationException;
using Microsoft.Extensions.DependencyInjection;

namespace AffiliateOptimizer.Tests.Unit.CommonDomain;

// The request pipeline copied from ArchitectureSimple.md Appendix A, pinned by a few behavioural tests.

public sealed record Ping(string Text) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult($"pong:{request.Text}");
}

public sealed record Touch(List<string> Trail) : IRequest;

public sealed class TouchHandler : CommandHandler<Touch>
{
    public override Task Handle(Touch command, CancellationToken ct)
    {
        command.Trail.Add("handler");
        return Task.CompletedTask;
    }
}

public sealed record Orphan : IRequest<int>;

public sealed record Validated(string Name, Validated.Inner Nested) : IRequest
{
    public sealed record Inner(int Value);
}

public sealed class ValidatedHandler : CommandHandler<Validated>
{
    public override Task Handle(Validated command, CancellationToken ct) => Task.CompletedTask;
}

public sealed class ValidatedValidator : ValidatorBase<Validated>
{
    public ValidatedValidator()
    {
        RuleFor(v => v.Name).NotEmpty();
        RuleFor(v => v.Nested.Value).GreaterThan(0);
    }
}

public abstract class TrailBehavior<TRequest, TResponse>(string name) : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is Touch touch) touch.Trail.Add($"{name}:before");
        var response = await next();
        if (request is Touch touch2) touch2.Trail.Add($"{name}:after");
        return response;
    }
}

public sealed class OuterBehavior<TRequest, TResponse>() : TrailBehavior<TRequest, TResponse>("outer") where TRequest : IRequest<TResponse>;

public sealed class InnerBehavior<TRequest, TResponse>() : TrailBehavior<TRequest, TResponse>("inner") where TRequest : IRequest<TResponse>;

public sealed class DispatcherTests
{
    private static IServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddRequestDispatching(typeof(DispatcherTests).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_Query_ResolvesTheSingleHandler()
    {
        var dispatcher = Build().GetRequiredService<IDispatcher>();

        Assert.Equal("pong:hi", await dispatcher.Send(new Ping("hi")));
    }

    [Fact]
    public async Task Send_Command_ReturnsUnit()
    {
        var trail = new List<string>();

        await Build().GetRequiredService<IDispatcher>().Send(new Touch(trail));

        Assert.Equal(["handler"], trail);
    }

    [Fact]
    public async Task Send_NoHandler_ThrowsWithAClearMessage()
    {
        var error = await Assert.ThrowsAsync<HandlerNotFoundException>(() => Build().GetRequiredService<IDispatcher>().Send(new Orphan()));

        Assert.Contains("Orphan", error.Message);
    }

    [Fact]
    public async Task Send_Behaviours_RunInRegistrationOrderOutermostFirst()
    {
        var trail = new List<string>();
        var provider = Build(s =>
        {
            s.AddPipelineBehavior(typeof(OuterBehavior<,>));
            s.AddPipelineBehavior(typeof(InnerBehavior<,>));
        });

        await provider.GetRequiredService<IDispatcher>().Send(new Touch(trail));

        Assert.Equal(["outer:before", "inner:before", "handler", "inner:after", "outer:after"], trail);
    }

    [Fact]
    public async Task Send_InvalidRequest_ValidationBehaviourThrowsBeforeTheHandler()
    {
        var provider = Build(s =>
        {
            s.AddPipelineBehavior(typeof(ValidationBehavior<,>));
            s.AddClosedImplementations(typeof(IValidatorBase<>), [typeof(DispatcherTests).Assembly]);
        });

        var error = await Assert.ThrowsAsync<Common.Domain.Exceptions.ValidationException>(
            () => provider.GetRequiredService<IDispatcher>().Send(new Validated("", new Validated.Inner(0))));

        Assert.Equal(["name", "nested.value"], error.Errors.Select(e => e.FieldName));
    }

    [Fact]
    public async Task PerformValidation_ValidRequest_DoesNotThrow()
        => await new ValidatedValidator().PerformValidation(new Validated("ok", new Validated.Inner(1)));
}

public sealed class ListExtensionsTests
{
    private sealed record Row(string Id, decimal? Amount, string Name);

    private static readonly IQueryable<Row> Rows = new List<Row>
    {
        new("c", 0.3m, "gamma"),
        new("a", null, "alpha"),
        new("d", 0.1m, "delta"),
        new("b", 0.3m, "beta"),
    }.AsQueryable();

    [Fact]
    public void OrderByPropertyName_Ascending_PutsNullsLastAndBreaksTiesByTheTieBreaker()
        => Assert.Equal(["d", "b", "c", "a"], Rows.OrderByPropertyName("amount", SortDirection.Asc, "id").Select(r => r.Id));

    [Fact]
    public void OrderByPropertyName_Descending_StillPutsNullsLast()
        => Assert.Equal(["c", "b", "d", "a"], Rows.OrderByPropertyName("Amount", SortDirection.Desc, "id").Select(r => r.Id));

    [Fact]
    public void OrderByPropertyName_NonNullableKey_SortsPlainly()
        => Assert.Equal(["alpha", "beta", "delta", "gamma"], Rows.OrderByPropertyName("NAME", SortDirection.Asc).Select(r => r.Name));

    [Fact]
    public void OrderByPropertyName_UnknownProperty_IsAValidationError()
    {
        var error = Assert.Throws<ValidationException>(() => Rows.OrderByPropertyName("nope", SortDirection.Asc));

        Assert.Equal("sortBy", error.Errors.Single().FieldName);
    }

    [Fact]
    public void NormalizePaging_ClampsToSaneBounds()
    {
        var query = new TestListQuery { PageIndex = -3, PageSize = 100_000 };

        Assert.Equal((0, ListExtensions.MaxPageSize), query.NormalizePaging());
        Assert.Equal((0, ListExtensions.DefaultPageSize), new TestListQuery().NormalizePaging());
    }

    private sealed record TestListQuery : DynamicListQuery<int>;
}
