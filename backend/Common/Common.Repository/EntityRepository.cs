using Common.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Common.Repository;

/// <summary>Closed per (DbContext, entity) so that UnitOfWork&lt;TDbContext&gt;.Repository&lt;T&gt;() resolves the right context.</summary>
public interface IEntityRepository<TDbContext, TEntity> : IBaseEntityRepository<TEntity>
    where TDbContext : DbContext
    where TEntity : class
{
}

public sealed class EntityRepository<TDbContext, TEntity>(TDbContext context) : IEntityRepository<TDbContext, TEntity>
    where TDbContext : DbContext
    where TEntity : class
{
    private readonly DbSet<TEntity> _set = context.Set<TEntity>();

    public void Add(TEntity entity) => _set.Add(entity);

    public async Task AddAsync(TEntity entity, CancellationToken ct = default) => await _set.AddAsync(entity, ct).ConfigureAwait(false);

    public void AddRange(IEnumerable<TEntity> entities) => _set.AddRange(entities);

    public ValueTask<TEntity?> FindAsync(CancellationToken ct, params object[] keyValues) => _set.FindAsync(keyValues, ct);

    public void Remove(TEntity entity) => _set.Remove(entity);

    public void Update(TEntity entity) => _set.Update(entity);

    public IQueryable<TEntity> GetQuery() => _set;
}
