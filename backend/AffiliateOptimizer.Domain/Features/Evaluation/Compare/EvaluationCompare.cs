using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Domain.Common.Statistics;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Evaluation.Compare;

/// <summary>
/// Compares the arms of one run: one arm per model, each judged on the clicks it decided. Interleaving gives
/// every arm the same clock window and (statistically) the same merchant mix, which is the most honest design
/// available here; what no design gives is a counterfactual, so the unit of comparison is "mean realized CPC per
/// settled click of an arm's own decisions inside one run". The verdict never claims more than the evidence
/// supports: inconclusive below the sample floor, no separation when intervals overlap, and a drift caveat
/// whenever an arm's first and second halves disagree.
/// </summary>
public static partial class EvaluationCompare
{
    /// <summary>RunId null = the active run, else the latest.</summary>
    public sealed record Query(int? RunId) : IRequest<Result>;

    public enum Verdict
    {
        Inconclusive,
        NoSeparation,
        Leading,
    }

    public sealed record Arm(
        string ModelName,
        int Decided,
        int Routed,
        int Settled,
        int Rejected,
        int Expired,
        int Pending,
        decimal TotalCommission,
        decimal? Mean,
        decimal? StandardError,
        decimal? CiLow,
        decimal? CiHigh,
        decimal? MeanAdvertisedOfChosen,
        decimal? ClaimGap,
        decimal? FirstHalfMean,
        decimal? SecondHalfMean,
        bool ConditionsChanged);

    public sealed record Result(
        int? RunId,
        bool Interleaved,
        DateTime? StartedAt,
        DateTime? StoppedAt,
        IReadOnlyList<Arm> Arms,
        Verdict Verdict,
        string? LeadingModel,
        IReadOnlyList<string> Caveats,
        int MinSamplesPerArm,
        DateTime ServerNow);

    public interface IRepository : IFeatureRepository
    {
        Task<RunScope?> ResolveRun(int? runId, CancellationToken ct);

        Task<IReadOnlyList<ArmObservation>> GetArmObservations(int runId, CancellationToken ct);

        public sealed record RunScope(int RunId, IReadOnlyList<string> ModelNames, bool Interleaved, DateTime StartedAt, DateTime? StoppedAt);

        public sealed record ArmObservation(
            string? ModelName,
            decimal? AdvertisedRateOfChosen,
            DateTime ExpiresAt,
            DateTime? RoutedAt,
            DateTime? DisclosedAt,
            decimal? Commission,
            string? ErrorCode);
    }

    public sealed class QueryHandler(IRepository repository, EvaluationOptions options, ISystemTime time) : IRequestHandler<Query, Result>
    {
        private const int MinPerHalf = 5;

        public async Task<Result> Handle(Query query, CancellationToken ct)
        {
            var now = time.Now();
            var run = await repository.ResolveRun(query.RunId, ct);
            if (run is null)
                return new Result(null, false, null, null, [], Verdict.Inconclusive, null, ["No run to compare."], options.MinSamplesPerArm, now);

            var observations = (await repository.GetArmObservations(run.RunId, ct)).ToLookup(o => o.ModelName ?? string.Empty, StringComparer.Ordinal);
            var arms = run.ModelNames.Select(name => Describe(name, observations[name].ToList(), now)).ToList();

            var caveats = new List<string>
            {
                "No counterfactual exists: an arm's figures cover only the clicks it decided, never what another network would have paid.",
                "Each arm learns from its own decisions only, so an arm in an interleaved run sees half (or less) of the traffic it would see alone.",
            };
            if (run.StoppedAt is { } stoppedAt) caveats.Add($"The run ended at {stoppedAt:u}; late disclosures may still change the last minute of figures.");

            var (verdict, leading) = Judge(arms, caveats);
            return new Result(run.RunId, run.Interleaved, run.StartedAt, run.StoppedAt, arms, verdict, leading, caveats, options.MinSamplesPerArm, now);
        }

        private (Verdict, string?) Judge(IReadOnlyList<Arm> arms, List<string> caveats)
        {
            if (arms.Count < 2)
            {
                caveats.Add("Only one arm: there is nothing to compare it against. Start an interleaved run with two or more models.");
                return (Verdict.Inconclusive, null);
            }

            var thin = arms.Where(a => a.Settled < options.MinSamplesPerArm).ToList();
            if (thin.Count > 0)
            {
                caveats.Add($"Inconclusive until every arm has {options.MinSamplesPerArm} settled clicks; still short: {string.Join(", ", thin.Select(a => $"{a.ModelName} ({a.Settled})"))}.");
                return (Verdict.Inconclusive, null);
            }

            var drifting = arms.Where(a => a.ConditionsChanged).ToList();
            if (drifting.Count > 0)
                caveats.Add($"Conditions changed during the run for {string.Join(", ", drifting.Select(a => a.ModelName))}: first and second halves disagree, so a whole-run mean mixes two regimes.");

            var best = arms.OrderByDescending(a => a.Mean ?? decimal.MinValue).First();
            var bestStats = Stats(best);
            var separated = arms.Where(a => a != best).All(other => bestStats.ClearlyAbove(Stats(other)));

            if (separated && drifting.Count == 0) return (Verdict.Leading, best.ModelName);
            if (separated)
            {
                caveats.Add($"{best.ModelName} leads on the whole run, but the drift above means the lead may not hold now.");
                return (Verdict.Inconclusive, best.ModelName);
            }

            caveats.Add("The 95% intervals overlap: no arm is separated from the others on this evidence.");
            return (Verdict.NoSeparation, null);
        }

        private static SampleStatistics Stats(Arm arm)
            => new(arm.Settled, arm.Mean ?? 0m, arm.StandardError, arm.CiLow, arm.CiHigh);

        private static Arm Describe(string modelName, IReadOnlyList<IRepository.ArmObservation> observations, DateTime now)
        {
            var facts = observations.Select(o => (Observation: o, Fact: new ClickFact(o.ExpiresAt, o.RoutedAt, o.DisclosedAt, o.ErrorCode, o.Commission))).ToList();
            var counters = RunCounters.Compute(facts.Select(f => f.Fact), now);

            var settled = facts
                .Where(f => ClickStates.Derive(f.Fact, now) == ClickState.Settled && f.Observation.Commission is not null)
                .OrderBy(f => f.Observation.DisclosedAt)
                .ToList();
            var stats = SampleStatistics.Compute(settled.Select(f => f.Observation.Commission!.Value).ToList());

            var advertised = settled.Where(f => f.Observation.AdvertisedRateOfChosen is not null).Select(f => f.Observation.AdvertisedRateOfChosen!.Value).ToList();
            decimal? meanAdvertised = advertised.Count == 0 ? null : Money.Round4(advertised.Average());
            decimal? mean = stats.Count == 0 ? null : stats.Mean;

            var half = settled.Count / 2;
            var first = SampleStatistics.Compute(settled.Take(half).Select(f => f.Observation.Commission!.Value).ToList());
            var second = SampleStatistics.Compute(settled.Skip(half).Select(f => f.Observation.Commission!.Value).ToList());
            var changed = first.Count >= MinPerHalf && second.Count >= MinPerHalf && SampleStatistics.MeansDiffer(first, second);

            return new Arm(
                modelName,
                Decided: observations.Count,
                Routed: observations.Count(o => o.RoutedAt is not null && o.ErrorCode is null),
                Settled: counters.Settled,
                Rejected: counters.Rejected,
                Expired: counters.Expired,
                Pending: counters.AwaitingDisclosure,
                TotalCommission: counters.TotalCommission,
                mean,
                stats.StandardError,
                stats.CiLow,
                stats.CiHigh,
                meanAdvertised,
                ClaimGap: mean is null || meanAdvertised is null ? null : Money.Round4(mean.Value - meanAdvertised.Value),
                FirstHalfMean: first.Count == 0 ? null : first.Mean,
                SecondHalfMean: second.Count == 0 ? null : second.Mean,
                ConditionsChanged: changed);
        }
    }
}
