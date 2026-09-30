using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Commissions.Poll;

/// <summary>
/// One turn of the poll loop: read the feed from the persisted cursor, fold every row into its click (the feed is
/// authoritative), keep rows for unknown clicks as orphans, store the cursor after every call including the empty
/// ones, and drain a full page immediately. INVALID_CURSOR restarts the feed; UNAUTHORIZED stays on screen until
/// credentials work again. Polling never pauses: clicks already routed must still resolve.
/// </summary>
public static partial class CommissionsPoll
{
    public sealed record Command : IRequest<Result>;

    public sealed record Result(TimeSpan NextDelay, int Folded, int Orphans, string Outcome);

    public interface IRepository : IFeatureRepository
    {
        Task<FeedPosition> GetOrCreateCursor(string credentialFingerprint, CancellationToken ct);

        /// <summary>Folds or orphans every row and stores the next cursor in one transaction. Idempotent for replayed rows.</summary>
        Task<FeedPageResult> ApplyFeedPage(string credentialFingerprint, IReadOnlyList<CommissionRow> rows, string? nextCursor, DateTime now, CancellationToken ct);

        /// <summary>Returns the new count of consecutive failures.</summary>
        Task<int> RecordPollFailure(string credentialFingerprint, DateTime now, bool resetCursor, CancellationToken ct);

        public sealed record FeedPosition(string? Cursor, int ConsecutiveFailures);

        public sealed record FeedPageResult(int Folded, int Orphans);
    }

    public sealed class CommandHandler(
        IRepository repository,
        ILoopEventWriter events,
        IAffiliateServiceClient client,
        ICredentialContext credentials,
        ISystemTime time) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command command, CancellationToken ct)
        {
            var fingerprint = credentials.Fingerprint;
            var position = await repository.GetOrCreateCursor(fingerprint, ct);

            CommissionFeed feed;
            try
            {
                feed = await client.GetCommissions(position.Cursor, ct);
            }
            catch (AffiliateServiceException exception)
            {
                var (delay, now) = await Failed(fingerprint, resetCursor: exception.Code == AffiliateServiceErrorCodes.InvalidCursor, exception.RetryAfterSeconds, ct);
                await events.Add(LoopEventModel.Api(null, LoopKind.Poll, exception.Code, exception.Message, now, now + delay), ct);
                return new Result(delay, 0, 0, "api-error");
            }
            catch (AffiliateServiceTransportException exception)
            {
                var (delay, now) = await Failed(fingerprint, resetCursor: false, null, ct);
                await events.Add(LoopEventModel.Transport(null, LoopKind.Poll, exception.Message, now, now + delay), ct);
                return new Result(delay, 0, 0, "transport-error");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Not the server's fault: say so, rather than retrying silently (the original client learned this the hard way).
                var (delay, now) = await Failed(fingerprint, resetCursor: false, null, ct);
                await events.Add(LoopEventModel.ClientError(null, LoopKind.Poll, $"The commission poll failed inside the client: {exception.Message}", now, now + delay), ct);
                return new Result(delay, 0, 0, "client-error");
            }

            var applied = await repository.ApplyFeedPage(fingerprint, feed.Commissions, feed.NextCursor, time.Now(), ct);
            await events.ResolveOpen(LoopKind.Poll, time.Now(), ct);

            var next = feed.Commissions.Count >= LoopPacing.FeedPageLimit ? TimeSpan.Zero : LoopPacing.PollInterval;
            return new Result(next, applied.Folded, applied.Orphans, "polled");
        }

        private async Task<(TimeSpan Delay, DateTime Now)> Failed(string fingerprint, bool resetCursor, double? retryAfterSeconds, CancellationToken ct)
        {
            var now = time.Now();
            var failures = await repository.RecordPollFailure(fingerprint, now, resetCursor, ct);
            return (LoopPacing.BackoffAfterFailure(failures, retryAfterSeconds), now);
        }
    }
}
