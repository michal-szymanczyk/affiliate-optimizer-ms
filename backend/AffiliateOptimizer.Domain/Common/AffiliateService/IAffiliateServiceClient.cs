namespace AffiliateOptimizer.Domain.Common.AffiliateService;

/// <summary>
/// Transport to the Affiliate Service: one call in, one response or one thrown exception out.
/// No retries and no policy here; the loop slices own both (the route endpoint replays outcomes, the
/// click endpoint does not, so a blanket retry policy would be wrong for one of them).
/// </summary>
public interface IAffiliateServiceClient
{
    Task<IReadOnlyList<Merchant>> GetMerchants(CancellationToken ct);

    Task<IReadOnlyList<Network>> GetNetworks(CancellationToken ct);

    /// <summary>Issues one click. Rationed at 20 per minute (burst 40); not idempotent.</summary>
    Task<Click> NextClick(CancellationToken ct);

    /// <summary>Commits the click to one network. Repeating the identical request is safe: the server replays the original outcome.</summary>
    Task<RouteAcknowledgment> Route(string clickId, string networkId, CancellationToken ct);

    /// <summary>Everything disclosed since the cursor. A null cursor reads from the beginning of the credential pair's history.</summary>
    Task<CommissionFeed> GetCommissions(string? cursor, CancellationToken ct);
}
