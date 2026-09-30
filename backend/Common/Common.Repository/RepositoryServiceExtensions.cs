using System.Reflection;
using Common.Domain.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Common.Repository;

public static class RepositoryServiceExtensions
{
    /// <summary>Registers the open-generic entity repository resolved by UnitOfWork&lt;TDbContext&gt;.Repository&lt;T&gt;().</summary>
    public static IServiceCollection AddBaseRepositoryServices(this IServiceCollection services)
    {
        services.TryAddTransient(typeof(IEntityRepository<,>), typeof(EntityRepository<,>));
        return services;
    }

    /// <summary>
    /// Scans the assemblies for classes implementing interfaces derived from IFeatureRepository and registers
    /// each interface → class as transient (ArchitectureSimple.md §7.2). Adding a slice needs no registration.
    /// </summary>
    public static IServiceCollection LoadFeatureRepositories(this IServiceCollection services, params Assembly[] assemblies)
    {
        var marker = typeof(IFeatureRepository);

        var implementations = assemblies.Distinct()
            .SelectMany(a => a.DefinedTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var implementation in implementations)
        {
            var contracts = implementation.ImplementedInterfaces.Where(i => i != marker && marker.IsAssignableFrom(i));
            foreach (var contract in contracts)
                services.TryAddTransient(contract, implementation);
        }

        return services;
    }
}
