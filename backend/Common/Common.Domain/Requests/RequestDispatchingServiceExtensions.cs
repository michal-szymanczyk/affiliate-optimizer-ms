using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Common.Domain.Requests;

/// <summary>Convention-based registration (ArchitectureSimple.md §4.4). Scan only the assemblies you own.</summary>
public static class RequestDispatchingServiceExtensions
{
    /// <summary>Registers the dispatcher and every closed IRequestHandler / INotificationHandler found in the assemblies.</summary>
    public static IServiceCollection AddRequestDispatching(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.TryAddTransient<IDispatcher, Dispatcher>();
        services.AddClosedImplementations(typeof(IRequestHandler<,>), assemblies, allowMultiple: false);
        services.AddClosedImplementations(typeof(INotificationHandler<>), assemblies, allowMultiple: true);
        return services;
    }

    /// <summary>Adds an open-generic behaviour, e.g. typeof(ValidationBehavior&lt;,&gt;). Order of calls = order of execution.</summary>
    public static IServiceCollection AddPipelineBehavior(this IServiceCollection services, Type openBehaviorType)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), openBehaviorType);
        return services;
    }

    /// <summary>
    /// Registers every non-generic class implementing a closed version of <paramref name="openInterface"/>
    /// (e.g. IValidatorBase&lt;&gt;) under that closed interface.
    /// </summary>
    public static IServiceCollection AddClosedImplementations(this IServiceCollection services, Type openInterface,
        IEnumerable<Assembly> assemblies, bool allowMultiple = true)
    {
        if (!openInterface.IsGenericTypeDefinition)
            throw new ArgumentException("An open generic interface is required.", nameof(openInterface));

        var implementations = assemblies.Distinct()
            .SelectMany(a => a.DefinedTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var implementation in implementations)
        {
            var closedInterfaces = implementation.ImplementedInterfaces
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

            foreach (var closedInterface in closedInterfaces)
            {
                if (allowMultiple)
                    services.TryAddEnumerable(ServiceDescriptor.Transient(closedInterface, implementation));
                else
                    services.TryAddTransient(closedInterface, implementation);
            }
        }

        return services;
    }
}
