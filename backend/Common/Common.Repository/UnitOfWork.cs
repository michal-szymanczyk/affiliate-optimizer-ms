using System.Data;
using Common.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Repository;

/// <summary>
/// Wrapper over one DbContext (ArchitectureSimple.md §7.1). The DbContext is scoped; unit of work and repositories are
/// transient and hold the scoped context, so a handler, its repository and nested Send calls share one context.
/// </summary>
public abstract class UnitOfWork<TDbContext>(TDbContext context, IServiceProvider serviceProvider) : IBaseUnitOfWork
    where TDbContext : DbContext
{
    protected TDbContext Context { get; } = context;

    public bool IsInTransaction => Context.Database.CurrentTransaction is not null;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Context.SaveChangesAsync(ct);

    public IBaseEntityRepository<TEntity> Repository<TEntity>() where TEntity : class
        => serviceProvider.GetRequiredService<IEntityRepository<TDbContext, TEntity>>();

    public async Task RunInTransaction(Func<Task> work, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        if (!Context.Database.IsRelational() || IsInTransaction)
        {
            await work().ConfigureAwait(false);
            return;
        }

        // A retrying execution strategy (EnableRetryOnFailure) requires user transactions to run inside it.
        var strategy = Context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Context.Database.BeginTransactionAsync(isolationLevel, ct).ConfigureAwait(false);
            await work().ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
