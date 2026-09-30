using AffiliateOptimizer.Domain.Common.AffiliateService;

namespace AffiliateOptimizer.Web.Infrastructure.AffiliateService;

// Wire shapes of openapi.json, one record per schema. Dates arrive as ISO 8601 strings and are read as
// DateTimeOffset so an offset in the payload can never be misread as local time; the mapping methods hand
// the Domain plain UTC DateTimes (ArchitectureSimple.md §6.2 b: the destination knows how to build itself).

internal sealed record MerchantListDto(List<MerchantDto> Merchants);

internal sealed record MerchantDto(int MerchantId, string Name)
{
    public Merchant ToMerchant() => new(MerchantId, Name);
}

internal sealed record NetworkListDto(List<NetworkDto> Networks);

internal sealed record NetworkDto(string Id, string Name, decimal AdvertisedRate, List<int> SupportedMerchantIds)
{
    public Network ToNetwork() => new(Id, Name, AdvertisedRate, SupportedMerchantIds);
}

internal sealed record ClickDto(string ClickId, int MerchantId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)
{
    public Click ToClick() => new(ClickId, MerchantId, IssuedAt.UtcDateTime, ExpiresAt.UtcDateTime);
}

internal sealed record RouteAcceptedDto(string ClickId, string NetworkId, DateTimeOffset RoutedAt, bool AlreadyRouted, DateTimeOffset CommissionAvailableAt)
{
    public RouteAcknowledgment ToAcknowledgment() => new(ClickId, NetworkId, RoutedAt.UtcDateTime, AlreadyRouted, CommissionAvailableAt.UtcDateTime);
}

internal sealed record CommissionFeedDto(List<CommissionRowDto> Commissions, string? NextCursor)
{
    public CommissionFeed ToFeed() => new(Commissions.Select(row => row.ToRow()).ToList(), NextCursor);
}

internal sealed record CommissionRowDto(
    string ClickId,
    int MerchantId,
    string RoutedNetworkId,
    DateTimeOffset RoutedAt,
    DateTimeOffset DisclosedAt,
    string Kind,
    string State,
    decimal? Commission,
    string? ErrorCode)
{
    public CommissionRow ToRow() => new(ClickId, MerchantId, RoutedNetworkId, RoutedAt.UtcDateTime, DisclosedAt.UtcDateTime, Kind, State, Commission, ErrorCode);
}
