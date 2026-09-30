namespace Common.Domain.Requests;

// Request pipeline abstractions (ArchitectureSimple.md §4.2 / Appendix A). No third-party mediator.

/// <summary>A request that produces <typeparamref name="TResponse"/> (query, or command with a result).</summary>
public interface IRequest<out TResponse> { }

/// <summary>A command that produces no result.</summary>
public interface IRequest : IRequest<Unit> { }

/// <summary>Void substitute so that commands and queries flow through the same generic pipeline.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
    public static readonly Task<Unit> CompletedTask = Task.FromResult(Value);
}

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Base class for handlers of result-less commands. Tests call the public Handle directly.</summary>
public abstract class CommandHandler<TCommand> : IRequestHandler<TCommand, Unit> where TCommand : IRequest
{
    public abstract Task Handle(TCommand command, CancellationToken cancellationToken);

    async Task<Unit> IRequestHandler<TCommand, Unit>.Handle(TCommand request, CancellationToken cancellationToken)
    {
        await Handle(request, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

public interface INotification { }

public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

public interface IDispatcher
{
    /// <summary>Runs the pipeline and the single handler registered for the request's concrete type.</summary>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Invokes every handler registered for the notification's concrete type, sequentially.</summary>
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
}

public sealed class HandlerNotFoundException(Type requestType, Type responseType) : InvalidOperationException(
    $"No IRequestHandler<{requestType.Name}, {responseType.Name}> is registered. " +
    "Add the handler class to a scanned assembly or register it explicitly.");
