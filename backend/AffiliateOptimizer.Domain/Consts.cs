using System.Reflection;

namespace AffiliateOptimizer.Domain;

public static class Consts
{
    public const string ServiceType = "AffiliateOptimizer";

    /// <summary>Informational version stored on every run as provenance.</summary>
    public static string AppVersion { get; } =
        typeof(Consts).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Consts).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
