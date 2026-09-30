using AffiliateOptimizer.Domain.Features.Runs.Common;
using Common.Domain.Lists;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Runs.GetAll;

/// <summary>Run history with per-run totals, newest first. The place to pick a run to inspect or compare.</summary>
public static partial class RunsGetAll
{
    public sealed record Query : DynamicListQuery<Result>;

    public sealed record Model(
        int Id,
        RunStatus Status,
        DateTime StartedAt,
        DateTime? StoppedAt,
        IReadOnlyList<string> ModelNames,
        bool Interleaved,
        int EvidenceWindowMinutes,
        string CredentialFingerprint,
        string AppVersion,
        int Clicks,
        int Settled,
        decimal TotalCommission,
        decimal? PerSettledClick);

    public sealed record Result(PagedList<Model> Page, DateTime ServerNow);

    public interface IRepository : IFeatureRepository
    {
        Task<PagedList<Model>> GetRuns(IDynamicListParams listParams, DateTime now, CancellationToken ct);
    }

    public sealed class QueryHandler(IRepository repository, ISystemTime time) : IRequestHandler<Query, Result>
    {
        public async Task<Result> Handle(Query query, CancellationToken ct)
        {
            var now = time.Now();
            var effective = string.IsNullOrWhiteSpace(query.SortBy)
                ? query with { SortBy = nameof(Model.Id), SortDirection = SortDirection.Desc }
                : query;

            return new Result(await repository.GetRuns(effective, now, ct), now);
        }
    }
}
