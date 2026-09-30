using System.Security.Cryptography;
using System.Text;

namespace AffiliateOptimizer.Domain.Common.AffiliateService;

/// <summary>Bound from the "AffiliateService" configuration section (or the legacy APP_KEY / APP_SECRET / SERVER_URL variables).</summary>
public sealed class AffiliateServiceOptions
{
    public const string SectionName = "AffiliateService";

    /// <summary>Base URL of the Affiliate Service, e.g. https://affiliate.example.com. A path prefix is kept.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Public half of the credential pair, sent as X-App-Key.</summary>
    public string AppKey { get; set; } = string.Empty;

    /// <summary>Private half, sent as X-App-Secret. Never logged, never rendered.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Per-call timeout. A timed-out route is retried identically because the server replays outcomes.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Identifies a credential pair without revealing it. The commission feed and its cursor belong to the
/// credential pair, not to a run or a process, so persisted feed state is keyed by this value.
/// </summary>
public static class CredentialFingerprint
{
    public static string Compute(string appKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(appKey.Trim()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
