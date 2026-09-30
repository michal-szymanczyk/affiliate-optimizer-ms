using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using Common.Domain.Exceptions;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Clicks.Route;

/// <summary>
/// Commits one click to one network, with recovery for an ambiguous transport failure: the identical request is
/// repeated up to three times (1 s, then 2 s apart) because the server replays the original outcome. A committed
/// rejection (422 / 410) is final and recorded; any other server error leaves the row unrouted for the feed to
/// repair; three lost responses mark the click unconfirmed. A stopped run abandons the recovery quietly.
/// </summary>
public static partial class ClicksRoute
{
    public sealed record Command(string ClickId, string NetworkId) : IRequest<Result>;

    public enum RouteOutcome
    {
        Accepted,
        Rejected,
        Failed,
        Unconfirmed,
        Stopped,
    }

    public sealed record Result(RouteOutcome Outcome, string? ErrorCode);

    public interface IRepository : IFeatureRepository
    {
        /// <summary>The click's run, the network's display name from the run snapshot, and whether the run is already stopped. Null for an unknown click.</summary>
        Task<RouteTarget?> GetRouteTarget(string clickId, string networkId, CancellationToken ct);

        Task RecordRoute(string clickId, RouteAcknowledgment acknowledgment, string networkName, int attempts, CancellationToken ct);

        Task RecordRejection(string clickId, string networkId, string networkName, DateTime at, string errorCode, int attempts, CancellationToken ct);

        Task MarkUnconfirmed(string clickId, int attempts, CancellationToken ct);

        Task<bool> IsRunStopped(int runId, CancellationToken ct);

        public sealed record RouteTarget(int RunId, string NetworkName, bool RunStopped);
    }

    public sealed class CommandHandler(
        IRepository repository,
        ILoopEventWriter events,
        IAffiliateServiceClient client,
        TimeProvider timeProvider,
        ISystemTime time) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command command, CancellationToken ct)
        {
            var target = await repository.GetRouteTarget(command.ClickId, command.NetworkId, ct)
                         ?? throw new NotFoundException($"Click {command.ClickId} is not in the store.");

            for (var attempt = 1; attempt <= LoopPacing.MaxRouteAttempts; attempt++)
            {
                try
                {
                    var acknowledgment = await client.Route(command.ClickId, command.NetworkId, ct);
                    await repository.RecordRoute(command.ClickId, acknowledgment, target.NetworkName, attempt, ct);
                    return new Result(RouteOutcome.Accepted, null);
                }
                catch (AffiliateServiceException exception) when (exception.IsRoutingRejection)
                {
                    // The server committed a rejection: the click is spent. Recorded against the click, never retried.
                    await repository.RecordRejection(command.ClickId, command.NetworkId, target.NetworkName, time.Now(), exception.Code, attempt, ct);
                    return new Result(RouteOutcome.Rejected, exception.Code);
                }
                catch (AffiliateServiceException exception)
                {
                    // A request error is not a committed route: the row stays unrouted and the feed may still repair it.
                    var now = time.Now();
                    await events.Add(LoopEventModel.Api(target.RunId, LoopKind.Click, exception.Code, exception.Message, now,
                        now + LoopPacing.BackoffAfterFailure(1, exception.RetryAfterSeconds), command.ClickId), ct);
                    return new Result(RouteOutcome.Failed, exception.Code);
                }
                catch (AffiliateServiceTransportException)
                {
                    if (await repository.IsRunStopped(target.RunId, ct))
                        return new Result(RouteOutcome.Stopped, null);

                    if (attempt == LoopPacing.MaxRouteAttempts)
                    {
                        await repository.MarkUnconfirmed(command.ClickId, attempt, ct);
                        await events.Add(LoopEventModel.Unconfirmed(target.RunId, command.ClickId, time.Now()), ct);
                        return new Result(RouteOutcome.Unconfirmed, null);
                    }

                    await Task.Delay(LoopPacing.BackoffAfterFailure(attempt, null), timeProvider, ct);

                    if (await repository.IsRunStopped(target.RunId, ct))   // the run may have been stopped while the backoff was pending
                        return new Result(RouteOutcome.Stopped, null);
                }
            }

            throw new InvalidOperationException("Route recovery exhausted without returning.");
        }
    }
}
