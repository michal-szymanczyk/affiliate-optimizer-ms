using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Domain.Common.Optimization;

namespace AffiliateOptimizer.Repository.Entities;

/// <summary>
/// One click, keyed by the server's id. Holds timestamps and whatever the feed reported; its state is derived
/// by <see cref="ClickStates"/> and never stored. The decision columns are the model's provenance for this click.
/// </summary>
public class Click
{
    public string ClickId { get; private set; } = string.Empty;
    public int RunId { get; private set; }
    public long Ordinal { get; private set; }
    public int MerchantId { get; private set; }
    public string MerchantName { get; private set; } = string.Empty;
    public DateTime IssuedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    // decision
    public string? ModelName { get; private set; }
    public string? ModelVersion { get; private set; }
    public string? DecidedNetworkId { get; private set; }
    public string? Rationale { get; private set; }
    public string? ScoresJson { get; private set; }
    public DateTime? DecidedAt { get; private set; }

    // routing: set when the route call succeeded, or when it was rejected; both spend the click
    public DateTime? RoutedAt { get; private set; }
    public string? RoutedNetworkId { get; private set; }
    public string? RoutedNetworkName { get; private set; }
    public int RouteAttempts { get; private set; }
    public bool RouteUnconfirmed { get; private set; }

    // disclosure, from the feed
    public decimal? Commission { get; private set; }
    public DateTime? DisclosedAt { get; private set; }
    public string? ErrorCode { get; private set; }

    private Click() { }

    public static Click Issue(int runId, long ordinal, Domain.Common.AffiliateService.Click click, string merchantName) => new()
    {
        ClickId = click.ClickId,
        RunId = runId,
        Ordinal = ordinal,
        MerchantId = click.MerchantId,
        MerchantName = merchantName,
        IssuedAt = click.IssuedAt,
        ExpiresAt = click.ExpiresAt,
    };

    public void RecordDecision(string modelName, string modelVersion, Decision? decision, string? scoresJson, string? rationaleOverride, DateTime now)
    {
        ModelName = modelName;
        ModelVersion = modelVersion;
        DecidedNetworkId = decision?.NetworkId;
        Rationale = rationaleOverride ?? decision?.Rationale;
        ScoresJson = scoresJson;
        DecidedAt = now;
    }

    public void RecordRoute(string networkId, string networkName, DateTime routedAt, int attempts)
    {
        RoutedAt = routedAt;
        RoutedNetworkId = networkId;
        RoutedNetworkName = networkName;
        RouteAttempts = attempts;
        RouteUnconfirmed = false;
    }

    public void RecordRejection(string networkId, string networkName, DateTime at, string errorCode, int attempts)
    {
        RoutedAt = at;
        RoutedNetworkId = networkId;
        RoutedNetworkName = networkName;
        ErrorCode = errorCode;
        RouteAttempts = attempts;
        RouteUnconfirmed = false;
    }

    public void MarkUnconfirmed(int attempts)
    {
        RouteAttempts = attempts;
        RouteUnconfirmed = true;
    }

    /// <summary>The feed is authoritative: it overwrites the routing and disclosure fields, and applying the same row twice changes nothing.</summary>
    public void ApplyDisclosure(string networkId, string networkName, DateTime routedAt, decimal? commission, string? errorCode, DateTime disclosedAt)
    {
        RoutedAt = routedAt;
        RoutedNetworkId = networkId;
        RoutedNetworkName = networkName;
        Commission = commission;
        ErrorCode = errorCode;
        DisclosedAt = disclosedAt;
        RouteUnconfirmed = false;
    }

    public ClickFact ToFact() => new(ExpiresAt, RoutedAt, DisclosedAt, ErrorCode, Commission);
}
