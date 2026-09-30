namespace AffiliateOptimizer.Domain.Common.AffiliateService;

// The Affiliate Service contract as the Domain sees it (openapi.json, API.md). Every timestamp is UTC.
// The wire DTOs live with the HTTP client; these records are what handlers and models work with.

public sealed record Merchant(int MerchantId, string Name);

/// <summary>A network. Coverage (<see cref="SupportedMerchantIds"/>) is the one thing here that can be treated as fact.</summary>
public sealed record Network(string Id, string Name, decimal AdvertisedRate, IReadOnlyList<int> SupportedMerchantIds)
{
    public bool Covers(int merchantId) => SupportedMerchantIds.Contains(merchantId);
}

/// <summary>One unit of traffic. Expires two minutes after issue.</summary>
public sealed record Click(string ClickId, int MerchantId, DateTime IssuedAt, DateTime ExpiresAt);

/// <summary>An accepted route. Never carries the commission; that arrives in the feed a minute later.</summary>
public sealed record RouteAcknowledgment(string ClickId, string NetworkId, DateTime RoutedAt, bool AlreadyRouted, DateTime CommissionAvailableAt);

/// <summary>One disclosed outcome. A settlement carries a positive commission; a rejection carries an error code.</summary>
public sealed record CommissionRow(
    string ClickId,
    int MerchantId,
    string RoutedNetworkId,
    DateTime RoutedAt,
    DateTime DisclosedAt,
    string Kind,
    string State,
    decimal? Commission,
    string? ErrorCode);

/// <summary>A page of the feed. Store <see cref="NextCursor"/> after every call, including the empty ones.</summary>
public sealed record CommissionFeed(IReadOnlyList<CommissionRow> Commissions, string? NextCursor);
