using System.Reflection;
using Common.Domain.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Domain.Lists;

// Generic list queries for reference data (ArchitectureSimple.md §4.6). The cached variant is omitted: no distributed cache here.

/// <summary>Marker: the model is served by the generic list-query handler (auto-registered).</summary>
public interface IListQueryModel { }

public sealed record ListQuery<T> : IRequest<List<T>> where T : IListQueryModel;

/// <summary>Read-only projection repository backing ListQuery&lt;T&gt;.</summary>
public interface IListQueryRepository<T>
{
    Task<List<T>> GetAll(CancellationToken ct);
}

public sealed class ListQueryHandler<T>(IListQueryRepository<T> repository) : IRequestHandler<ListQuery<T>, List<T>>
    where T : IListQueryModel
{
    public Task<List<T>> Handle(ListQuery<T> request, CancellationToken ct) => repository.GetAll(ct);
}

public static class ListQueryServiceExtension
{
    public static IServiceCollection AddListQueryHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        var models = assemblies.Distinct()
            .SelectMany(a => a.DefinedTypes)
            .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(IListQueryModel).IsAssignableFrom(t));

        foreach (var model in models)
        {
            services.AddTransient(
                typeof(IRequestHandler<,>).MakeGenericType(typeof(ListQuery<>).MakeGenericType(model), typeof(List<>).MakeGenericType(model)),
                typeof(ListQueryHandler<>).MakeGenericType(model));
        }

        return services;
    }
}
