using AffiliateOptimizer.Domain.Common.AffiliateService;

namespace AffiliateOptimizer.Domain.Common.Optimization;

/// <summary>
/// Coverage is published and trustworthy, so it is applied once, here, before any model sees the click.
/// Routing outside coverage is an avoidable mistake that still spends the click; no model gets the chance.
/// </summary>
public static class Eligibility
{
    public static IReadOnlyList<EligibleNetwork> Of(int merchantId, IEnumerable<Network> networks)
        => networks
            .Where(n => n.Covers(merchantId))
            .OrderBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => new EligibleNetwork(n.Id, n.Name, n.AdvertisedRate))
            .ToList();
}
