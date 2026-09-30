using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Domain.Requests;

/// <summary>
/// In-house dispatcher (ArchitectureSimple.md §4.3). One small generic wrapper per request type is built
/// once and cached; handlers and behaviours are resolved from the DI scope of the current request.
/// </summary>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapperBase> RequestWrappers = new();
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapperBase> NotificationWrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = (RequestHandlerWrapper<TResponse>)RequestWrappers.GetOrAdd(
            request.GetType(),
            static (requestType, responseType) => (RequestHandlerWrapperBase)Activator.CreateInstance(
                typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType))!,
            typeof(TResponse));

        return wrapper.Handle(request, serviceProvider, cancellationToken);
    }

    public Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var wrapper = NotificationWrappers.GetOrAdd(
            notification.GetType(),
            static notificationType => (NotificationHandlerWrapperBase)Activator.CreateInstance(
                typeof(NotificationHandlerWrapperImpl<>).MakeGenericType(notificationType))!);

        return wrapper.Handle(notification, serviceProvider, cancellationToken);
    }
}

internal abstract class RequestHandlerWrapperBase { }

internal abstract class RequestHandlerWrapper<TResponse> : RequestHandlerWrapperBase
{
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider provider, CancellationToken ct);
}

internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider provider, CancellationToken ct)
    {
        var handler = provider.GetService<IRequestHandler<TRequest, TResponse>>()
                      ?? throw new HandlerNotFoundException(typeof(TRequest), typeof(TResponse));

        var typedRequest = (TRequest)request;
        RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle(typedRequest, ct);

        // first registered behaviour becomes the outermost one
        foreach (var behavior in provider.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
        {
            var next = pipeline;
            pipeline = () => behavior.Handle(typedRequest, next, ct);
        }

        return pipeline();
    }
}

internal abstract class NotificationHandlerWrapperBase
{
    public abstract Task Handle(INotification notification, IServiceProvider provider, CancellationToken ct);
}

internal sealed class NotificationHandlerWrapperImpl<TNotification> : NotificationHandlerWrapperBase
    where TNotification : INotification
{
    public override async Task Handle(INotification notification, IServiceProvider provider, CancellationToken ct)
    {
        foreach (var handler in provider.GetServices<INotificationHandler<TNotification>>())
        {
            await handler.Handle((TNotification)notification, ct).ConfigureAwait(false);
        }
    }
}
