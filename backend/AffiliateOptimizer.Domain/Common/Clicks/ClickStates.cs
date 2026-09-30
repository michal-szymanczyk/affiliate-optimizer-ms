namespace AffiliateOptimizer.Domain.Common.Clicks;

public enum ClickState
{
    Issued,
    Routed,
    Settled,
    Rejected,
    Expired,
}

/// <summary>The facts a click row holds. State is never one of them.</summary>
public sealed record ClickFact(DateTime ExpiresAt, DateTime? RoutedAt, DateTime? DisclosedAt, string? ErrorCode, decimal? Commission);

/// <summary>
/// STATE IS DERIVED, NEVER STORED, the same rule the server follows. A row holds timestamps and whatever the
/// feed reported; this turns those plus the current time into a state. Storing a state would mean writing
/// "expired" on a timer for every click the loop did not get to. The order of the checks mirrors the server.
/// </summary>
public static class ClickStates
{
    public static ClickState Derive(ClickFact click, DateTime now)
    {
        if (click.ErrorCode is not null) return ClickState.Rejected;

        if (click.RoutedAt is not null)
            return click.DisclosedAt is { } disclosedAt && disclosedAt <= now ? ClickState.Settled : ClickState.Routed;

        if (click.ExpiresAt <= now) return ClickState.Expired;

        return ClickState.Issued;
    }

    public static bool IsTerminal(ClickState state) => state is ClickState.Settled or ClickState.Rejected or ClickState.Expired;
}

public static class Money
{
    /// <summary>Four decimals are significant: some networks differ by less than a cent. Halves round away from zero, as JavaScript's Math.round does for positive values.</summary>
    public static decimal Round4(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}

/// <summary>Whole-run totals. Money is summed in decimal, so 0.1 + 0.2 is 0.3.</summary>
public sealed record RunCounters(
    int Processed,
    int Settled,
    int Rejected,
    int Expired,
    int AwaitingDisclosure,
    int Completed,
    decimal TotalCommission,
    decimal? PerSettledClick)
{
    public static RunCounters Compute(IEnumerable<ClickFact> clicks, DateTime now)
    {
        int processed = 0, settled = 0, rejected = 0, expired = 0, awaiting = 0;
        decimal total = 0m;

        foreach (var click in clicks)
        {
            processed++;
            switch (ClickStates.Derive(click, now))
            {
                case ClickState.Settled:
                    settled++;
                    total += click.Commission ?? 0m;
                    break;
                case ClickState.Rejected:
                    rejected++;
                    break;
                case ClickState.Expired:
                    expired++;
                    break;
                case ClickState.Routed:
                    awaiting++;
                    break;
            }
        }

        return new RunCounters(
            processed,
            settled,
            rejected,
            expired,
            awaiting,
            Completed: settled + rejected + expired,
            TotalCommission: Money.Round4(total),
            PerSettledClick: settled == 0 ? null : Money.Round4(total / settled));
    }
}
