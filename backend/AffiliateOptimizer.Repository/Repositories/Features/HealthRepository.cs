using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.Health.GetStatus;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using ClickEntity = AffiliateOptimizer.Repository.Entities.Click;

namespace AffiliateOptimizer.Repository.Repositories.Features;

public sealed class HealthRepository(IAppUnitOfWork unitOfWork) : HealthGetStatus.IRepository
{
    public async Task<HealthGetStatus.IRepository.HealthFacts> GetHealthFacts(string credentialFingerprint, DateTime recentSince, DateTime overdueBefore, DateTime now, CancellationToken ct)
    {
        var runs = unitOfWork.Repository<Run>().GetQuery().AsNoTracking();
        var clicks = unitOfWork.Repository<ClickEntity>().GetQuery().AsNoTracking();
        var events = unitOfWork.Repository<LoopEvent>().GetQuery().AsNoTracking();

        var run = await runs
            .Where(r => r.Status != RunStatus.Stopped)
            .OrderByDescending(r => r.Id)
            .Select(r => new { r.Id, r.Status, r.StartedAt, r.ConsecutiveClickFailures, r.LastClickIssuedAt })
            .FirstOrDefaultAsync(ct);

        var scoped = run is null ? clicks : clicks.Where(c => c.RunId == run.Id);
        var pending = scoped.Where(c => c.RoutedAt != null && c.ErrorCode == null && (c.DisclosedAt == null || c.DisclosedAt > now));

        var lastRouteAt = await scoped.MaxAsync(c => c.RoutedAt, ct);
        var lastDisclosureAt = await clicks.MaxAsync(c => c.DisclosedAt, ct);
        var awaiting = await pending.CountAsync(ct);
        var overdue = await pending.CountAsync(c => c.RoutedAt < overdueBefore, ct);
        var rejectionsRecently = await scoped.CountAsync(c => c.ErrorCode != null && c.RoutedAt >= recentSince, ct);
        var orphans = await unitOfWork.Repository<OrphanDisclosure>().GetQuery().CountAsync(ct);

        var cursor = await unitOfWork.Repository<FeedCursor>().GetQuery().AsNoTracking()
            .FirstOrDefaultAsync(f => f.CredentialFingerprint == credentialFingerprint, ct);

        var recentEvents = await events
            .Where(e => e.OccurredAt >= recentSince)
            .Select(e => new { e.Kind, e.Code })
            .ToListAsync(ct);

        var openEvents = await events
            .Where(e => e.DismissedAt == null && e.ResolvedAt == null)
            .OrderByDescending(e => e.OccurredAt)
            .Take(20)
            .Select(e => new HealthGetStatus.OpenEvent(e.Id, e.RunId, e.Loop, e.Kind, e.Code, e.Message, e.ClickId, e.OccurredAt, e.RetryAt, e.Dismissible))
            .ToListAsync(ct);

        var recent = new HealthGetStatus.RecentCounts(
            RateLimited: recentEvents.Count(e => e.Code == AffiliateServiceErrorCodes.RateLimited),
            Transport: recentEvents.Count(e => e.Kind == LoopEventKind.Transport),
            Unconfirmed: recentEvents.Count(e => e.Kind == LoopEventKind.Unconfirmed),
            ModelErrors: recentEvents.Count(e => e.Kind == LoopEventKind.ModelError),
            Rejections: rejectionsRecently,
            ClientErrors: recentEvents.Count(e => e.Kind == LoopEventKind.ClientError));

        return new HealthGetStatus.IRepository.HealthFacts(
            run?.Id,
            run?.Status,
            run?.StartedAt,
            run?.ConsecutiveClickFailures ?? 0,
            run?.LastClickIssuedAt,
            lastRouteAt,
            lastDisclosureAt,
            cursor?.LastPolledAt,
            cursor?.LastSuccessAt,
            cursor?.ConsecutiveFailures ?? 0,
            cursor?.Cursor is not null,
            awaiting,
            overdue,
            orphans,
            recent,
            openEvents);
    }
}
