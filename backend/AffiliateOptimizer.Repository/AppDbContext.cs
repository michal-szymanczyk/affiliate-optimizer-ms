using AffiliateOptimizer.Repository.Entities;
using Common.Domain.Persistence;
using Common.Repository;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AffiliateOptimizer.Repository;

/// <summary>The application's own database (schema and migrations belong here). Identity tables come from the base context.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser, AppRole, int>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    /// <summary>Every DateTime is UTC and comes back as UTC; every decimal is money with four significant decimals.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime2(3)");
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime2(3)");
        configurationBuilder.Properties<decimal>().HavePrecision(10, 4);
        configurationBuilder.Properties<decimal?>().HavePrecision(10, 4);
    }
}

/// <summary>SQL Server stores no kind; this restores Utc on read so serialised timestamps carry their "Z".</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

public interface IAppUnitOfWork : IBaseUnitOfWork
{
}

public sealed class AppUnitOfWork(AppDbContext context, IServiceProvider serviceProvider)
    : UnitOfWork<AppDbContext>(context, serviceProvider), IAppUnitOfWork
{
}
