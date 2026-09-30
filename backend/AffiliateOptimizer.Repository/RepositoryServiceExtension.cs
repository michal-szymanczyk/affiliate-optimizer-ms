using AffiliateOptimizer.Repository.Entities;
using Common.Repository;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AffiliateOptimizer.Repository;

public static class RepositoryServiceExtension
{
    /// <summary>
    /// DbContext (scoped), Identity stores, the unit of work and every feature repository found in this assembly
    /// (ArchitectureSimple.md §8.1 step 8). Identity options and cookies are configured by the web host.
    /// </summary>
    public static IServiceCollection AddRepository(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

        services.AddIdentity<AppUser, AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services.AddRepositoryCore();
    }

    /// <summary>The provider-independent part, reused by tests over the in-memory provider.</summary>
    public static IServiceCollection AddRepositoryCore(this IServiceCollection services)
    {
        services.AddBaseRepositoryServices();
        services.AddTransient<IAppUnitOfWork, AppUnitOfWork>();
        services.LoadFeatureRepositories(typeof(RepositoryServiceExtension).Assembly);
        return services;
    }
}
