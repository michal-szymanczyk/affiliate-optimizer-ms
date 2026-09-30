using System.Data;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Features.Commissions.Poll;
using AffiliateOptimizer.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using ClickEntity = AffiliateOptimizer.Repository.Entities.Click;

namespace AffiliateOptimizer.Repository.Repositories.Features;

public sealed class CommissionsRepository(IAppUnitOfWork unitOfWork) : CommissionsPoll.IRepository
{
    public async Task<CommissionsPoll.IRepository.FeedPosition> GetOrCreateCursor(string credentialFingerprint, CancellationToken ct)
    {
        var cursor = await unitOfWork.Repository<FeedCursor>().FindAsync(ct, credentialFingerprint);
        if (cursor is null)
        {
            cursor = FeedCursor.Create(credentialFingerprint);
            await unitOfWork.Repository<FeedCursor>().AddAsync(cursor, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return new CommissionsPoll.IRepository.FeedPosition(cursor.Cursor, cursor.ConsecutiveFailures);
    }

    public async Task<CommissionsPoll.IRepository.FeedPageResult> ApplyFeedPage(string credentialFingerprint, IReadOnlyList<CommissionRow> rows, string? nextCursor, DateTime now, CancellationToken ct)
    {
        int folded = 0, orphans = 0;

        await unitOfWork.RunInTransaction(async () =>
        {
            var cursor = await unitOfWork.Repository<FeedCursor>().FindAsync(ct, credentialFingerprint)
                         ?? throw new InvalidOperationException($"No feed cursor row exists for {credentialFingerprint}.");

            var clickIds = rows.Select(r => r.ClickId).Distinct(StringComparer.Ordinal).ToList();
            var clicks = await unitOfWork.Repository<ClickEntity>().GetQuery()
                .Where(c => clickIds.Contains(c.ClickId))
                .ToDictionaryAsync(c => c.ClickId, StringComparer.Ordinal, ct);
            var knownOrphans = (await unitOfWork.Repository<OrphanDisclosure>().GetQuery()
                    .Where(o => clickIds.Contains(o.ClickId))
                    .Select(o => o.ClickId)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var runIds = clicks.Values.Select(c => c.RunId).Distinct().ToList();
            var networkNames = (await unitOfWork.Repository<RunNetworkSnapshot>().GetQuery()
                    .Where(n => runIds.Contains(n.RunId))
                    .Select(n => new { n.RunId, n.NetworkId, n.Name })
                    .ToListAsync(ct))
                .ToDictionary(n => (n.RunId, n.NetworkId), n => n.Name);

            foreach (var row in rows)
            {
                if (clicks.TryGetValue(row.ClickId, out var click))
                {
                    var name = networkNames.GetValueOrDefault((click.RunId, row.RoutedNetworkId), row.RoutedNetworkId);
                    click.ApplyDisclosure(row.RoutedNetworkId, name, row.RoutedAt, row.Commission, row.ErrorCode, row.DisclosedAt);
                    folded++;
                }
                else if (knownOrphans.Add(row.ClickId))
                {
                    await unitOfWork.Repository<OrphanDisclosure>().AddAsync(OrphanDisclosure.From(row, now), ct);
                    orphans++;
                }
            }

            cursor.RecordPollSuccess(nextCursor, now);
            await unitOfWork.SaveChangesAsync(ct);
        }, IsolationLevel.ReadCommitted, ct);

        return new CommissionsPoll.IRepository.FeedPageResult(folded, orphans);
    }

    public async Task<int> RecordPollFailure(string credentialFingerprint, DateTime now, bool resetCursor, CancellationToken ct)
    {
        var cursor = await unitOfWork.Repository<FeedCursor>().FindAsync(ct, credentialFingerprint);
        if (cursor is null)
        {
            cursor = FeedCursor.Create(credentialFingerprint);
            await unitOfWork.Repository<FeedCursor>().AddAsync(cursor, ct);
        }

        var failures = cursor.RecordPollFailure(now, resetCursor);
        await unitOfWork.SaveChangesAsync(ct);
        return failures;
    }
}
