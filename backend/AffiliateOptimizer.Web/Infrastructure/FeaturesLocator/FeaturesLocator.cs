using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Razor;

namespace AffiliateOptimizer.Web.Infrastructure.FeaturesLocator;

/// <summary>Stores the feature name (namespace segment after "Features") on the controller (ArchitectureSimple.md §8.4).</summary>
public sealed class FeatureConvention : IControllerModelConvention
{
    public const string FeatureKey = "feature";

    public void Apply(ControllerModel controller)
        => controller.Properties[FeatureKey] = FeatureName(controller.ControllerType.FullName);

    private static string FeatureName(string? typeName)
    {
        if (typeName is null) return string.Empty;
        var tokens = typeName.Split('.');
        var index = Array.IndexOf(tokens, "Features");
        return index >= 0 && index + 1 < tokens.Length ? tokens[index + 1] : string.Empty;
    }
}

/// <summary>Resolves views from /Features/{feature}/{view}.cshtml and /Features/Shared/{view}.cshtml.</summary>
public sealed class FeatureViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context)
    {
        var descriptor = context.ActionContext.ActionDescriptor as ControllerActionDescriptor;
        context.Values[FeatureConvention.FeatureKey] = descriptor?.Properties.TryGetValue(FeatureConvention.FeatureKey, out var feature) == true
            ? feature as string
            : null;
    }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        var feature = context.Values.TryGetValue(FeatureConvention.FeatureKey, out var value) ? value : null;
        var featureLocations = string.IsNullOrEmpty(feature)
            ? []
            : new[] { $"/Features/{feature}/{{0}}.cshtml" };

        return featureLocations.Concat(["/Features/Shared/{0}.cshtml"]).Concat(viewLocations);
    }
}

public static class RazorOptions
{
    public static void AddFeaturesLocator(RazorViewEngineOptions options)
        => options.ViewLocationExpanders.Add(new FeatureViewLocationExpander());
}
