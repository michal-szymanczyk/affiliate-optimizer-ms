using AffiliateOptimizer.Domain.Common.Optimization;
using Common.Domain.Lists;

namespace AffiliateOptimizer.Domain.Features.Models.GetAll;

/// <summary>The optimization models this host can run, served by the generic list-query handler (ArchitectureSimple.md §4.6).</summary>
public static partial class ModelsGetAll
{
    public sealed record Model(string Name, string Version, string Description, string ParametersJson) : IListQueryModel
    {
        public Model(IOptimizationModel model) : this(model.Name, model.Version, model.Description, model.ParametersJson) { }
    }

    /// <summary>Backed by the catalog rather than a database projection: the list of models is code, not data.</summary>
    public sealed class CatalogRepository(IOptimizationModelCatalog catalog) : IListQueryRepository<Model>
    {
        public Task<List<Model>> GetAll(CancellationToken ct) => Task.FromResult(catalog.All.Select(m => new Model(m)).ToList());
    }
}
