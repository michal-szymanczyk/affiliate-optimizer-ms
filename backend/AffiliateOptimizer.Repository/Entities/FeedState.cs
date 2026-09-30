using AffiliateOptimizer.Domain.Common.AffiliateService;

namespace AffiliateOptimizer.Repository.Entities;

/// <summary>
/// The commission feed's position, per credential pair (the feed belongs to the credentials, not to a run or a
/// process). Stored after every poll, including the empty ones, so a restart never replays history.
/// </summary>
public class FeedCursor
{
    public string CredentialFingerprint { get; private set; } = string.Empty;
    public string? Cursor { get; private set; }
    public DateTime? LastPolledAt { get; private set; }
    public DateTime? LastSuccessAt { get; private set; }
    public int ConsecutiveFailures { get; private set; }

    private FeedCursor() { }

    public static FeedCursor Create(string credentialFingerprint) => new() { CredentialFingerprint = credentialFingerprint };

    public void RecordPollSuccess(string? nextCursor, DateTime now)
    {
        Cursor = nextCursor;            // unchanged on an empty page, null only before anything was disclosed
        LastPolledAt = now;
        LastSuccessAt = now;
        ConsecutiveFailures = 0;
    }

    public int RecordPollFailure(DateTime now, bool resetCursor)
    {
        LastPolledAt = now;
        if (resetCursor) Cursor = null;   // INVALID_CURSOR: a cursor that cannot work is not retried
        return ++ConsecutiveFailures;
    }
}

/// <summary>
/// A disclosed outcome for a click this store never issued: the feed replaying history from before the
/// database existed, or from another host on the same credentials. Counted, never folded, never silently dropped.
/// </summary>
public class OrphanDisclosure
{
    public string ClickId { get; private set; } = string.Empty;
    public int MerchantId { get; private set; }
    public string RoutedNetworkId { get; private set; } = string.Empty;
    public DateTime RoutedAt { get; private set; }
    public DateTime DisclosedAt { get; private set; }
    public decimal? Commission { get; private set; }
    public string? ErrorCode { get; private set; }
    public DateTime SeenAt { get; private set; }

    private OrphanDisclosure() { }

    public static OrphanDisclosure From(CommissionRow row, DateTime now) => new()
    {
        ClickId = row.ClickId,
        MerchantId = row.MerchantId,
        RoutedNetworkId = row.RoutedNetworkId,
        RoutedAt = row.RoutedAt,
        DisclosedAt = row.DisclosedAt,
        Commission = row.Commission,
        ErrorCode = row.ErrorCode,
        SeenAt = now,
    };
}
