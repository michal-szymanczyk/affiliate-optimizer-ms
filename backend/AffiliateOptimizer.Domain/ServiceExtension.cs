using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Common.Optimization.Models;
using AffiliateOptimizer.Domain.Features.Models.GetAll;
using Common.Domain.Lists;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AffiliateOptimizer.Domain;

public static class ServiceExtension
{
    /// <summary>
    /// Domain registrations: every IOptimizationModel in this assembly (adding a model class is enough), the
    /// catalog, the random source and the generic list-query handlers. Model option objects use their defaults
    /// unless the host registered configured instances before calling this.
    /// </summary>
    public static IServiceCollection AddDomain(this IServiceCollection services)
    {
        var assembly = typeof(ServiceExtension).Assembly;

        services.TryAddSingleton<EpsilonGreedyOptions>();
        services.TryAddSingleton<SlidingWindowUcbOptions>();
        services.TryAddSingleton<IRandomSource, SystemRandomSource>();

        var models = assembly.DefinedTypes
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IOptimizationModel).IsAssignableFrom(t));
        foreach (var model in models)
            services.AddSingleton(typeof(IOptimizationModel), model);

        services.TryAddSingleton<IOptimizationModelCatalog>(sp => new OptimizationModelCatalog(sp.GetServices<IOptimizationModel>()));
        services.AddListQueryHandlers(assembly);
        services.TryAddSingleton<IListQueryRepository<ModelsGetAll.Model>, ModelsGetAll.CatalogRepository>();

        return services;
    }
}
