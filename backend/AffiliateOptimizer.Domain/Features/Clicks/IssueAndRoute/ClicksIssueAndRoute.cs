using System.Text.Json;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Features.Clicks.Route;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Clicks.IssueAndRoute;

/// <summary>
/// One turn of the click loop: pull a click, let the run's model choose, commit the route. The hosted service
/// only schedules turns; this handler owns the policy (what to do on each failure, how long to wait next),
/// mirroring the original client's issue-and-route loop with its defects removed: a model exception no longer
/// kills the loop, a transport failure is visible, and exactly one chain exists because the run's status is read here.
/// </summary>
public static partial class ClicksIssueAndRoute
{
    private static readonly JsonSerializerOptions ScoresJson = new(JsonSerializerDefaults.Web);

    public sealed record Command : IRequest<Result>;

    public enum Outcome
    {
        Idle,
        Routed,
        Rejected,
        Lapsed,
        IssueFailed,
        RouteFailed,
        Unconfirmed,
        ModelFailed,
        Stopped,
    }

    public sealed record Result(Outcome Outcome, TimeSpan NextDelay, string? ClickId);

    public interface IRepository : IFeatureRepository
    {
        Task<ActiveRun?> GetActiveRun(CancellationToken ct);

        /// <summary>Stores the row before any route attempt, allocating its ordinal and naming its merchant from the run's snapshot.</summary>
        Task<IssuedClick> IssueClick(IssueClickModel model, CancellationToken ct);

        Task<IReadOnlyList<EligibleNetwork>> GetEligibleNetworks(int runId, int merchantId, CancellationToken ct);

        /// <summary>What this arm (run + model) has observed per network, rebuilt from the store. Absent networks were never observed.</summary>
        Task<IReadOnlyDictionary<string, NetworkEvidence>> GetEvidence(int runId, string modelName, DateTime windowStart, DateTime now, CancellationToken ct);

        Task RecordDecision(string clickId, DecisionModel decision, CancellationToken ct);

        /// <summary>Returns the new count of consecutive failures.</summary>
        Task<int> RecordClickFailure(int runId, CancellationToken ct);

        Task ResetClickFailures(int runId, CancellationToken ct);

        public sealed record IssueClickModel(int RunId, Click Click, DateTime Now);

        public sealed record IssuedClick(long Ordinal, string MerchantName);

        /// <summary>The decision as provenance: which model, which version, what it chose, why, and what every candidate scored.</summary>
        public sealed record DecisionModel(string ModelName, string ModelVersion, Decision? Decision, string? ScoresJson, string Rationale, DateTime DecidedAt)
        {
            public DecisionModel(IOptimizationModel model, Decision? decision, DateTime now) : this(
                model.Name,
                model.Version,
                decision,
                decision is null ? null : JsonSerializer.Serialize(decision.Scores, ClicksIssueAndRoute.ScoresJson),
                decision?.Rationale ?? "no eligible network covers this merchant: the click is left to lapse",
                now) { }

            public static DecisionModel Failed(IOptimizationModel model, string error, DateTime now)
                => new(model.Name, model.Version, null, null, $"model threw while choosing: {error}", now);
        }
    }

    public sealed class CommandHandler(
        IRepository repository,
        ILoopEventWriter events,
        IAffiliateServiceClient client,
        IOptimizationModelCatalog catalog,
        IRandomSource random,
        IDispatcher dispatcher,
        ISystemTime time) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command command, CancellationToken ct)
        {
            var run = await repository.GetActiveRun(ct);
            if (run is null || run.Status != RunStatus.Running)
                return new Result(Outcome.Idle, LoopPacing.IdleInterval, null);

            Click click;
            try
            {
                click = await client.NextClick(ct);
            }
            catch (AffiliateServiceException exception)
            {
                return await IssueFailed(run, exception.Code, exception.Message, exception.RetryAfterSeconds, LoopEventKind.Api, ct);
            }
            catch (AffiliateServiceTransportException exception)
            {
                return await IssueFailed(run, LoopEventCodes.Transport, exception.Message, null, LoopEventKind.Transport, ct);
            }

            await repository.ResetClickFailures(run.Id, ct);
            await events.ResolveOpen(LoopKind.Click, time.Now(), ct);

            var issued = await repository.IssueClick(new IRepository.IssueClickModel(run.Id, click, time.Now()), ct);
            var model = catalog.Get(run.Assignment.NameForOrdinal(issued.Ordinal));

            var now = time.Now();
            var context = new DecisionContext(
                click.MerchantId,
                await repository.GetEligibleNetworks(run.Id, click.MerchantId, ct),
                await repository.GetEvidence(run.Id, model.Name, now - run.EvidenceWindow, now, ct),
                now,
                random);

            Decision? decision;
            try
            {
                decision = model.Choose(context);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The loop survives a model bug: the click lapses, the fault is visible, the next turn goes on.
                await events.Add(LoopEventModel.ModelError(run.Id, click.ClickId, model.Name, exception.Message, now), ct);
                await repository.RecordDecision(click.ClickId, IRepository.DecisionModel.Failed(model, exception.Message, now), ct);
                return new Result(Outcome.ModelFailed, LoopPacing.ClickInterval, click.ClickId);
            }

            await repository.RecordDecision(click.ClickId, new IRepository.DecisionModel(model, decision, now), ct);
            if (decision is null)
                return new Result(Outcome.Lapsed, LoopPacing.ClickInterval, click.ClickId);

            var route = await dispatcher.Send(new ClicksRoute.Command(click.ClickId, decision.NetworkId), ct);   // same scope, same unit of work
            return new Result(ToTurnOutcome(route.Outcome), LoopPacing.ClickInterval, click.ClickId);
        }

        private async Task<Result> IssueFailed(ActiveRun run, string code, string message, double? retryAfterSeconds, LoopEventKind kind, CancellationToken ct)
        {
            var failures = await repository.RecordClickFailure(run.Id, ct);
            var delay = LoopPacing.BackoffAfterFailure(failures, retryAfterSeconds);
            var now = time.Now();
            var loopEvent = kind == LoopEventKind.Transport
                ? LoopEventModel.Transport(run.Id, LoopKind.Click, message, now, now + delay)
                : LoopEventModel.Api(run.Id, LoopKind.Click, code, message, now, now + delay);
            await events.Add(loopEvent, ct);
            return new Result(Outcome.IssueFailed, delay, null);
        }

        private static Outcome ToTurnOutcome(ClicksRoute.RouteOutcome outcome) => outcome switch
        {
            ClicksRoute.RouteOutcome.Accepted => Outcome.Routed,
            ClicksRoute.RouteOutcome.Rejected => Outcome.Rejected,
            ClicksRoute.RouteOutcome.Failed => Outcome.RouteFailed,
            ClicksRoute.RouteOutcome.Unconfirmed => Outcome.Unconfirmed,
            _ => Outcome.Stopped,
        };
    }
}
