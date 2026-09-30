using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Domain.Features.LoopEvents.Dismiss;
using AffiliateOptimizer.Repository.Entities;
using Microsoft.EntityFrameworkCore;

namespace AffiliateOptimizer.Repository.Repositories.Features;

public sealed class LoopEventsRepository(IAppUnitOfWork unitOfWork) :
    ILoopEventWriter,
    LoopEventsDismiss.IRepository
{
    public async Task Add(LoopEventModel loopEvent, CancellationToken ct)
    {
        await unitOfWork.Repository<LoopEvent>().AddAsync(LoopEvent.Create(loopEvent), ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<int> ResolveOpen(LoopKind loop, DateTime now, CancellationToken ct)
    {
        var open = await unitOfWork.Repository<LoopEvent>().GetQuery()
            .Where(e => e.Loop == loop && e.DismissedAt == null && e.ResolvedAt == null)
            .ToListAsync(ct);

        if (open.Count == 0) return 0;

        foreach (var loopEvent in open) loopEvent.Resolve(now);
        await unitOfWork.SaveChangesAsync(ct);
        return open.Count;
    }

    public async Task<bool> Dismiss(long eventId, DateTime now, CancellationToken ct)
    {
        var loopEvent = await unitOfWork.Repository<LoopEvent>().FindAsync(ct, eventId);
        if (loopEvent is null) return false;

        loopEvent.Dismiss(now);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
