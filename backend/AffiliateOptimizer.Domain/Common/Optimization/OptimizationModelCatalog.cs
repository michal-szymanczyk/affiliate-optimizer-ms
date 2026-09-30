namespace AffiliateOptimizer.Domain.Common.Optimization;

/// <summary>Every model known to the application, by name. Populated by assembly scan in AddDomain().</summary>
public interface IOptimizationModelCatalog
{
    IReadOnlyList<IOptimizationModel> All { get; }

    bool Contains(string name);

    /// <summary>Throws when the name is unknown: run start validates names, so an unknown name at decision time is a programming error.</summary>
    IOptimizationModel Get(string name);
}

public sealed class OptimizationModelCatalog : IOptimizationModelCatalog
{
    private readonly Dictionary<string, IOptimizationModel> _models;

    public OptimizationModelCatalog(IEnumerable<IOptimizationModel> models)
    {
        _models = new Dictionary<string, IOptimizationModel>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
                throw new InvalidOperationException($"{model.GetType().Name} has an empty model name.");
            if (!_models.TryAdd(model.Name, model))
                throw new InvalidOperationException($"Two optimization models are named \"{model.Name}\": {_models[model.Name].GetType().Name} and {model.GetType().Name}.");
        }

        All = _models.Values.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<IOptimizationModel> All { get; }

    public bool Contains(string name) => _models.ContainsKey(name);

    public IOptimizationModel Get(string name)
        => _models.TryGetValue(name, out var model)
            ? model
            : throw new InvalidOperationException($"No optimization model is named \"{name}\". Known models: {string.Join(", ", _models.Keys)}.");
}
