using System.Data;

namespace Common.Domain.Persistence;

// Persistence abstractions the Domain may depend on (ArchitectureSimple.md §7.1). Implemented in Common.Repository.

/// <summary>Marker for slice repository contracts (IRepository : IFeatureRepository). Auto-registered by LoadFeatureRepositories().</summary>
public interface IFeatureRepository { }

/// <summary>One unit of work per database; the application declares a marker interface per database (IAppUnitOfWork : IBaseUnitOfWork).</summary>
public interface IBaseUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>DbSet wrapper, resolved per entity type.</summary>
    IBaseEntityRepository<TEntity> Repository<TEntity>() where TEntity : class;

    /// <summary>Runs the work inside one transaction (joins an ambient one). Non-relational providers run it plainly.</summary>
    Task RunInTransaction(Func<Task> work, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default);

    bool IsInTransaction { get; }
}

public interface IBaseEntityRepository<TEntity> where TEntity : class
{
    void Add(TEntity entity);
    Task AddAsync(TEntity entity, CancellationToken ct = default);
    void AddRange(IEnumerable<TEntity> entities);
    ValueTask<TEntity?> FindAsync(CancellationToken ct, params object[] keyValues);
    void Remove(TEntity entity);
    void Update(TEntity entity);

    /// <summary>The workhorse: LINQ-to-Entities.</summary>
    IQueryable<TEntity> GetQuery();
}
