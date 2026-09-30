using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Domain.Common.Statistics;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Networks.GetInsight;

/// <summary>
/// Decision insight per network: what it claims against what it paid, on how much evidence, how recently, and
/// whether that evidence is thin, stale or changing. The flags are the operator's guard against reading a
/// single lucky observation as a trend.
/// </summary>
public static partial class NetworksGetInsight
{
    /// <summary>RunId null = the active run (or the latest one). AllRuns pools every run's observations under the latest catalogue.</summary>
    public sealed record Query(int? RunId, bool AllRuns = false) : IRequest<Result>;

    public sealed record Model(
        string NetworkId,
        string Name,
        decimal AdvertisedRate,
        int CoverageCount,
        int Routed,
        int Settled,
        int Pending,
        int Rejected,
        decimal? Mean,
        decimal? StandardError,
        decimal? CiLow,
        decimal? CiHigh,
        int RecentSettled,
        decimal? RecentMean,
        DateTime? LastDisclosedAt,
        decimal? ClaimGap,
        decimal? ClaimGapPercent,
        decimal? Trend,
        bool Thin,
        bool Stale,
        bool Changing);

    public sealed record Result(IReadOnlyList<Model> Networks, int? RunId, int RecentWindowMinutes, DateTime ServerNow);

    public interface IRepository : IFeatureRepository
    {
        /// <summary>The run to read the catalogue from: the given one, else the active one, else the latest. Null when no run exists.</summary>
        Task<int?> ResolveRunId(int? runId, CancellationToken ct);

        Task<IReadOnlyList<Network>> GetNetworkSnapshot(int runId, CancellationToken ct);

        /// <summary>Every routed click of the run (or of every run when null), as facts.</summary>
        Task<IReadOnlyList<Observation>> GetObservations(int? runId, CancellationToken ct);

        public sealed record Observation(string NetworkId, DateTime? DisclosedAt, decimal? Commission, string? ErrorCode);
    }

    public sealed class QueryHandler(IRepository repository, EvaluationOptions options, ISystemTime time) : IRequestHandler<Query, Result>
    {
        public async Task<Result> Handle(Query query, CancellationToken ct)
        {
            var now = time.Now();
            var runId = await repository.ResolveRunId(query.RunId, ct);
            if (runId is null) return new Result([], null, options.RecentWindowMinutes, now);

            var networks = await repository.GetNetworkSnapshot(runId.Value, ct);
            var observations = (await repository.GetObservations(query.AllRuns ? null : runId, ct))
                .ToLookup(o => o.NetworkId, StringComparer.Ordinal);

            var windowStart = now.AddMinutes(-options.RecentWindowMinutes);
            var staleBefore = now.AddMinutes(-options.StaleAfterMinutes);

            var models = networks
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(network => Describe(network, observations[network.Id].ToList(), now, windowStart, staleBefore))
                .ToList();

            return new Result(models, query.AllRuns ? null : runId, options.RecentWindowMinutes, now);
        }

        private Model Describe(Network network, IReadOnlyList<IRepository.Observation> observations, DateTime now, DateTime windowStart, DateTime staleBefore)
        {
            var settled = observations.Where(o => o.ErrorCode is null && o.DisclosedAt is { } d && d <= now && o.Commission is not null).ToList();
            var recent = settled.Where(o => o.DisclosedAt >= windowStart).ToList();
            var stats = SampleStatistics.Compute(settled.Select(o => o.Commission!.Value).ToList());
            var recentStats = SampleStatistics.Compute(recent.Select(o => o.Commission!.Value).ToList());
            var lastDisclosedAt = settled.Count == 0 ? null : settled.Max(o => o.DisclosedAt);

            decimal? mean = stats.Count == 0 ? null : stats.Mean;
            decimal? recentMean = recentStats.Count == 0 ? null : recentStats.Mean;
            decimal? claimGap = mean is null ? null : Money.Round4(mean.Value - network.AdvertisedRate);
            decimal? claimGapPercent = claimGap is null || network.AdvertisedRate == 0 ? null : decimal.Round(claimGap.Value / network.AdvertisedRate * 100, 1, MidpointRounding.AwayFromZero);
            decimal? trend = mean is null || recentMean is null ? null : Money.Round4(recentMean.Value - mean.Value);

            var changing = recentStats.Count >= 3 && recentStats.StandardError is { } recentSe && mean is not null
                           && Math.Abs((double)(recentStats.Mean - mean.Value)) > SampleStatistics.Z95 * (double)recentSe;

            return new Model(
                network.Id,
                network.Name,
                network.AdvertisedRate,
                network.SupportedMerchantIds.Count,
                Routed: observations.Count(o => o.ErrorCode is null),
                Settled: settled.Count,
                Pending: observations.Count(o => o.ErrorCode is null && (o.DisclosedAt is null || o.DisclosedAt > now)),
                Rejected: observations.Count(o => o.ErrorCode is not null),
                mean,
                stats.StandardError,
                stats.CiLow,
                stats.CiHigh,
                recent.Count,
                recentMean,
                lastDisclosedAt,
                claimGap,
                claimGapPercent,
                trend,
                Thin: settled.Count < options.ThinBelow,
                Stale: lastDisclosedAt is null || lastDisclosedAt < staleBefore,
                Changing: changing);
        }
    }
}
