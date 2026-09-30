using AffiliateOptimizer.Domain.Common.Clicks;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Clicks.GetCounters;

/// <summary>Whole-run totals, derived at server time. RunId null means every click in the store.</summary>
public static partial class ClicksGetCounters
{
    public sealed record Query(int? RunId) : IRequest<Result>;

    public sealed record Result(
        int Processed,
        int Settled,
        int Rejected,
        int Expired,
        int AwaitingDisclosure,
        int Completed,
        decimal TotalCommission,
        decimal? PerSettledClick,
        int OrphanDisclosures,
        DateTime? StartedAt,
        DateTime ServerNow)
    {
        public Result(RunCounters counters, int orphanDisclosures, DateTime? startedAt, DateTime serverNow) : this(
            counters.Processed,
            counters.Settled,
            counters.Rejected,
            counters.Expired,
            counters.AwaitingDisclosure,
            counters.Completed,
            counters.TotalCommission,
            counters.PerSettledClick,
            orphanDisclosures,
            startedAt,
            serverNow) { }
    }

    public interface IRepository : IFeatureRepository
    {
        Task<IReadOnlyList<ClickFact>> GetClickFacts(int? runId, CancellationToken ct);

        /// <summary>When the run started; for a null run id, the active run's start, if any.</summary>
        Task<DateTime?> GetRunStartedAt(int? runId, CancellationToken ct);

        /// <summary>Disclosures for clicks this store never issued (history replayed by the feed). Worth showing, never hidden.</summary>
        Task<int> CountOrphanDisclosures(CancellationToken ct);
    }

    public sealed class QueryHandler(IRepository repository, ISystemTime time) : IRequestHandler<Query, Result>
    {
        public async Task<Result> Handle(Query query, CancellationToken ct)
        {
            var now = time.Now();
            var facts = await repository.GetClickFacts(query.RunId, ct);
            return new Result(
                RunCounters.Compute(facts, now),
                await repository.CountOrphanDisclosures(ct),
                await repository.GetRunStartedAt(query.RunId, ct),
                now);
        }
    }
}
