using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Health.GetStatus;

/// <summary>
/// Operational observability in one call: is traffic flowing, are decisions being made, are commissions arriving,
/// are retries succeeding, is the data fresh. The diagnosis localises a fault the way the brief asks: is it the
/// traffic (server), the application, the data feed, or an optimization model.
/// </summary>
public static partial class HealthGetStatus
{
    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromSeconds(90);      // disclosure is one minute after routing
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(5);

    public sealed record Query : IRequest<Result>;

    public enum Diagnosis
    {
        Healthy,
        NoActiveRun,
        Paused,
        Unauthorized,
        AppNotTicking,
        TrafficStalled,
        FeedStalled,
        ModelFaulting,
    }

    public sealed record LoopBeatModel(DateTime At, string Outcome, double AgeSeconds);

    public sealed record OpenEvent(
        long Id,
        int? RunId,
        LoopKind Loop,
        LoopEventKind Kind,
        string Code,
        string Message,
        string? ClickId,
        DateTime OccurredAt,
        DateTime? RetryAt,
        bool Dismissible);

    public sealed record RecentCounts(int RateLimited, int Transport, int Unconfirmed, int ModelErrors, int Rejections, int ClientErrors);

    public sealed record Result
    {
        public required Diagnosis Diagnosis { get; init; }
        public required string Summary { get; init; }
        public required int? RunId { get; init; }
        public required RunStatus? RunStatus { get; init; }
        public required DateTime? RunStartedAt { get; init; }
        public required DateTime? LastClickIssuedAt { get; init; }
        public required DateTime? LastRouteAt { get; init; }
        public required DateTime? LastDisclosureAt { get; init; }
        public required DateTime? LastPollAt { get; init; }
        public required DateTime? LastPollSuccessAt { get; init; }
        public required bool HasCursor { get; init; }
        public required int ClickConsecutiveFailures { get; init; }
        public required int PollConsecutiveFailures { get; init; }
        public required int AwaitingDisclosure { get; init; }
        public required int OverduePending { get; init; }
        public required int OrphanDisclosures { get; init; }
        public required RecentCounts Recent { get; init; }
        public required LoopBeatModel? ClickLoop { get; init; }
        public required LoopBeatModel? PollLoop { get; init; }
        public required IReadOnlyList<OpenEvent> OpenEvents { get; init; }
        public required DateTime ServerNow { get; init; }
    }

    public interface IRepository : IFeatureRepository
    {
        Task<HealthFacts> GetHealthFacts(string credentialFingerprint, DateTime recentSince, DateTime overdueBefore, DateTime now, CancellationToken ct);

        public sealed record HealthFacts(
            int? RunId,
            RunStatus? RunStatus,
            DateTime? RunStartedAt,
            int ClickConsecutiveFailures,
            DateTime? LastClickIssuedAt,
            DateTime? LastRouteAt,
            DateTime? LastDisclosureAt,
            DateTime? LastPollAt,
            DateTime? LastPollSuccessAt,
            int PollConsecutiveFailures,
            bool HasCursor,
            int AwaitingDisclosure,
            int OverduePending,
            int OrphanDisclosures,
            RecentCounts Recent,
            IReadOnlyList<OpenEvent> OpenEvents);
    }

    public sealed class QueryHandler(IRepository repository, ILoopHeartbeat heartbeat, ICredentialContext credentials, ISystemTime time)
        : IRequestHandler<Query, Result>
    {
        public async Task<Result> Handle(Query query, CancellationToken ct)
        {
            var now = time.Now();
            var facts = await repository.GetHealthFacts(credentials.Fingerprint, now - RecentWindow, now - OverdueAfter, now, ct);
            var clickBeat = Beat(heartbeat.Last(LoopKind.Click), now);
            var pollBeat = Beat(heartbeat.Last(LoopKind.Poll), now);
            var (diagnosis, summary) = Diagnose(facts, clickBeat, pollBeat, now);

            return new Result
            {
                Diagnosis = diagnosis,
                Summary = summary,
                RunId = facts.RunId,
                RunStatus = facts.RunStatus,
                RunStartedAt = facts.RunStartedAt,
                LastClickIssuedAt = facts.LastClickIssuedAt,
                LastRouteAt = facts.LastRouteAt,
                LastDisclosureAt = facts.LastDisclosureAt,
                LastPollAt = facts.LastPollAt,
                LastPollSuccessAt = facts.LastPollSuccessAt,
                HasCursor = facts.HasCursor,
                ClickConsecutiveFailures = facts.ClickConsecutiveFailures,
                PollConsecutiveFailures = facts.PollConsecutiveFailures,
                AwaitingDisclosure = facts.AwaitingDisclosure,
                OverduePending = facts.OverduePending,
                OrphanDisclosures = facts.OrphanDisclosures,
                Recent = facts.Recent,
                ClickLoop = clickBeat,
                PollLoop = pollBeat,
                OpenEvents = facts.OpenEvents,
                ServerNow = now,
            };
        }

        private static LoopBeatModel? Beat(LoopBeat? beat, DateTime now)
            => beat is null ? null : new LoopBeatModel(beat.At, beat.Outcome, Math.Max(0, (now - beat.At).TotalSeconds));

        /// <summary>Precedence: credentials → application → traffic → feed → model → run state.</summary>
        internal static (Diagnosis, string) Diagnose(IRepository.HealthFacts facts, LoopBeatModel? clickBeat, LoopBeatModel? pollBeat, DateTime now)
        {
            var running = facts.RunStatus == RunStatus.Running;
            var stall = StallAfter.TotalSeconds;

            if (facts.OpenEvents.Any(e => e.Code == AffiliateServiceErrorCodes.Unauthorized))
                return (Diagnosis.Unauthorized, "The Affiliate Service rejects the credentials. Nothing will flow until AffiliateService:AppKey / AppSecret are fixed and the host restarted.");

            if (running && (clickBeat is null || clickBeat.AgeSeconds > stall) || pollBeat is null || pollBeat.AgeSeconds > stall)
                return (Diagnosis.AppNotTicking, "A loop in this application has not completed a turn recently. This is the application, not the server: check the host log.");

            var runAge = facts.RunStartedAt is { } startedAt ? (now - startedAt).TotalSeconds : 0;
            var sinceLastClick = (now - (facts.LastClickIssuedAt ?? facts.RunStartedAt ?? now)).TotalSeconds;
            var clickLoopFault = facts.OpenEvents.Any(e => e.Loop == LoopKind.Click && e.Code is AffiliateServiceErrorCodes.RateLimited or LoopEventCodes.Transport);
            if (running && runAge > stall && (sinceLastClick > stall || clickLoopFault))
                return (Diagnosis.TrafficStalled, "Clicks are not being issued. The server is throttling or unreachable; the open events say which.");

            var pollLoopFault = facts.OpenEvents.Any(e => e.Loop == LoopKind.Poll);
            var sinceLastPoll = (now - (facts.LastPollSuccessAt ?? DateTime.MinValue)).TotalSeconds;
            if ((facts.LastPollSuccessAt is null && pollBeat is not null && pollBeat.AgeSeconds <= stall && facts.PollConsecutiveFailures > 0) || sinceLastPoll > stall && facts.LastPollSuccessAt is not null || pollLoopFault)
                return (Diagnosis.FeedStalled, "The commission feed is not being read successfully. Routed clicks will keep waiting for disclosure.");

            if (facts.OverduePending > 0)
                return (Diagnosis.FeedStalled, $"{facts.OverduePending} routed click(s) are more than {OverdueAfter.TotalSeconds:0} s old without a disclosure although polling succeeds: the data feed is late.");

            if (facts.Recent.ModelErrors > 0 || facts.Recent.Rejections > 0)
                return (Diagnosis.ModelFaulting, "An optimization model is throwing or routing outside coverage; those clicks are spent without earning.");

            return facts.RunStatus switch
            {
                null => (Diagnosis.NoActiveRun, "No run is active. Start one to begin routing; polling continues regardless."),
                RunStatus.Paused => (Diagnosis.Paused, "The run is paused: no clicks are issued, commissions keep arriving."),
                _ => (Diagnosis.Healthy, "Clicks flow, decisions are made, commissions arrive, the feed is fresh."),
            };
        }
    }
}
