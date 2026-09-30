using AffiliateOptimizer.Domain.Common.Clicks;
using Common.Domain.Lists;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Clicks.List;

/// <summary>
/// The click feed: paged, sortable and filterable on the server (the original client filtered in memory, which
/// only worked because a run lived and died with a browser tab). State is derived in the projection at server
/// time, so the operator's clock never decides whether a click looks expired or settled.
/// </summary>
public static partial class ClicksList
{
    public interface IFilter
    {
        int? RunId { get; }
        string? NetworkId { get; }
        ClickState? State { get; }
        string? Merchant { get; }
        bool? ErrorsOnly { get; }
        string? ModelName { get; }
    }

    public sealed record Query : DynamicListQuery<Result>, IFilter
    {
        public int? RunId { get; init; }
        public string? NetworkId { get; init; }
        public ClickState? State { get; init; }

        /// <summary>Name substring, or an exact merchant id.</summary>
        public string? Merchant { get; init; }

        /// <summary>Rejected or expired only: the clicks that were spent or lost without earning.</summary>
        public bool? ErrorsOnly { get; init; }

        public string? ModelName { get; init; }
    }

    public sealed record Model
    {
        public required string ClickId { get; init; }
        public required int RunId { get; init; }
        public required long Ordinal { get; init; }
        public required int MerchantId { get; init; }
        public required string MerchantName { get; init; }
        public required DateTime IssuedAt { get; init; }
        public required DateTime ExpiresAt { get; init; }
        public required string? ModelName { get; init; }
        public required string? DecidedNetworkId { get; init; }
        public required string? Rationale { get; init; }
        public required string? ScoresJson { get; init; }
        public required DateTime? RoutedAt { get; init; }
        public required string? RoutedNetworkId { get; init; }
        public required string? RoutedNetworkName { get; init; }
        public required decimal? Commission { get; init; }
        public required DateTime? DisclosedAt { get; init; }
        public required string? ErrorCode { get; init; }
        public required bool RouteUnconfirmed { get; init; }
        public required ClickState State { get; init; }
    }

    public sealed record Result(PagedList<Model> Page, DateTime ServerNow);

    public interface IRepository : IFeatureRepository
    {
        Task<PagedList<Model>> GetClicks(IDynamicListParams listParams, IFilter filter, DateTime now, CancellationToken ct);
    }

    public sealed class QueryHandler(IRepository repository, ISystemTime time) : IRequestHandler<Query, Result>
    {
        public async Task<Result> Handle(Query query, CancellationToken ct)
        {
            var now = time.Now();

            // Newest first unless the operator sorted a column; derive a copy rather than mutating the request.
            var effective = string.IsNullOrWhiteSpace(query.SortBy)
                ? query with { SortBy = nameof(Model.IssuedAt), SortDirection = SortDirection.Desc }
                : query;

            return new Result(await repository.GetClicks(effective, effective, now, ct), now);
        }
    }
}
