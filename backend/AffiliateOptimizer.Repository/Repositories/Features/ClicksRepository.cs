using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Features.Clicks.GetCounters;
using AffiliateOptimizer.Domain.Features.Clicks.IssueAndRoute;
using AffiliateOptimizer.Domain.Features.Clicks.List;
using AffiliateOptimizer.Domain.Features.Clicks.Route;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Repository.Entities;
using Common.Domain.Exceptions;
using Common.Domain.Lists;
using Common.Repository;
using Microsoft.EntityFrameworkCore;
using ClickEntity = AffiliateOptimizer.Repository.Entities.Click;

namespace AffiliateOptimizer.Repository.Repositories.Features;

/// <summary>Clicks area: the loop's writes and the operator's reads (ArchitectureSimple.md §7.2, one class per area).</summary>
public sealed class ClicksRepository(IAppUnitOfWork unitOfWork) :
    ClicksIssueAndRoute.IRepository,
    ClicksRoute.IRepository,
    ClicksList.IRepository,
    ClicksGetCounters.IRepository
{
    // ---- ClicksList / ClicksGetCounters ----

    public async Task<PagedList<ClicksList.Model>> GetClicks(IDynamicListParams listParams, ClicksList.IFilter filter, DateTime now, CancellationToken ct)
    {
        var clicks = Clicks.AsNoTracking();
        if (filter.RunId is { } runId) clicks = clicks.Where(c => c.RunId == runId);

        // Project into the Model first (state as a CASE expression at server time), filter on the Model second (§7.3).
        var query = clicks.Select(c => new ClicksList.Model
        {
            ClickId = c.ClickId,
            RunId = c.RunId,
            Ordinal = c.Ordinal,
            MerchantId = c.MerchantId,
            MerchantName = c.MerchantName,
            IssuedAt = c.IssuedAt,
            ExpiresAt = c.ExpiresAt,
            ModelName = c.ModelName,
            DecidedNetworkId = c.DecidedNetworkId,
            Rationale = c.Rationale,
            ScoresJson = c.ScoresJson,
            RoutedAt = c.RoutedAt,
            RoutedNetworkId = c.RoutedNetworkId,
            RoutedNetworkName = c.RoutedNetworkName,
            Commission = c.Commission,
            DisclosedAt = c.DisclosedAt,
            ErrorCode = c.ErrorCode,
            RouteUnconfirmed = c.RouteUnconfirmed,
            State = c.ErrorCode != null
                ? ClickState.Rejected
                : c.RoutedAt != null
                    ? (c.DisclosedAt != null && c.DisclosedAt <= now ? ClickState.Settled : ClickState.Routed)
                    : c.ExpiresAt <= now ? ClickState.Expired : ClickState.Issued,
        });

        if (!string.IsNullOrWhiteSpace(filter.NetworkId)) query = query.Where(m => m.RoutedNetworkId == filter.NetworkId);
        if (filter.State is { } state) query = query.Where(m => m.State == state);
        if (filter.ErrorsOnly == true) query = query.Where(m => m.State == ClickState.Rejected || m.State == ClickState.Expired);
        if (!string.IsNullOrWhiteSpace(filter.ModelName)) query = query.Where(m => m.ModelName == filter.ModelName);
        if (!string.IsNullOrWhiteSpace(filter.Merchant))
        {
            var term = filter.Merchant.Trim().ToLowerInvariant();
            query = int.TryParse(term, out var merchantId)
                ? query.Where(m => m.MerchantId == merchantId || m.MerchantName.ToLower().Contains(term))
                : query.Where(m => m.MerchantName.ToLower().Contains(term));
        }

        return await query.ToSortedPagedList(listParams, nameof(ClicksList.Model.IssuedAt), nameof(ClicksList.Model.ClickId), ct);
    }

    public async Task<IReadOnlyList<ClickFact>> GetClickFacts(int? runId, CancellationToken ct)
    {
        var clicks = Clicks.AsNoTracking();
        if (runId is { } id) clicks = clicks.Where(c => c.RunId == id);
        return await clicks.Select(c => new ClickFact(c.ExpiresAt, c.RoutedAt, c.DisclosedAt, c.ErrorCode, c.Commission)).ToListAsync(ct);
    }

    public Task<DateTime?> GetRunStartedAt(int? runId, CancellationToken ct)
    {
        var runs = Runs.AsNoTracking();
        runs = runId is { } id ? runs.Where(r => r.Id == id) : runs.Where(r => r.Status != RunStatus.Stopped).OrderByDescending(r => r.Id);
        return runs.Select(r => (DateTime?)r.StartedAt).FirstOrDefaultAsync(ct);
    }

    public Task<int> CountOrphanDisclosures(CancellationToken ct)
        => unitOfWork.Repository<OrphanDisclosure>().GetQuery().CountAsync(ct);

    private IQueryable<ClickEntity> Clicks => unitOfWork.Repository<ClickEntity>().GetQuery();
    private IQueryable<Run> Runs => unitOfWork.Repository<Run>().GetQuery();
    private IQueryable<RunNetworkSnapshot> Networks => unitOfWork.Repository<RunNetworkSnapshot>().GetQuery();

    // ---- ClicksIssueAndRoute ----

    public async Task<ActiveRun?> GetActiveRun(CancellationToken ct)
    {
        var run = await Runs.AsNoTracking()
            .Where(r => r.Status != RunStatus.Stopped)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);
        return run?.ToActiveRun();
    }

    public async Task<ClicksIssueAndRoute.IRepository.IssuedClick> IssueClick(ClicksIssueAndRoute.IRepository.IssueClickModel model, CancellationToken ct)
    {
        var run = await unitOfWork.Repository<Run>().FindAsync(ct, model.RunId)
                  ?? throw new NotFoundException($"Run {model.RunId} does not exist.");

        var ordinal = run.AllocateOrdinal();
        run.RecordClickIssued(model.Now);

        var merchantName = await unitOfWork.Repository<RunMerchantSnapshot>().GetQuery()
                               .Where(m => m.RunId == model.RunId && m.MerchantId == model.Click.MerchantId)
                               .Select(m => m.Name)
                               .FirstOrDefaultAsync(ct)
                           ?? $"Merchant {model.Click.MerchantId}";

        await unitOfWork.Repository<ClickEntity>().AddAsync(ClickEntity.Issue(model.RunId, ordinal, model.Click, merchantName), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new ClicksIssueAndRoute.IRepository.IssuedClick(ordinal, merchantName);
    }

    public async Task<IReadOnlyList<EligibleNetwork>> GetEligibleNetworks(int runId, int merchantId, CancellationToken ct)
    {
        var snapshots = await Networks.AsNoTracking().Where(n => n.RunId == runId).ToListAsync(ct);
        return Eligibility.Of(merchantId, snapshots.Select(s => s.ToNetwork()));
    }

    public async Task<IReadOnlyDictionary<string, NetworkEvidence>> GetEvidence(int runId, string modelName, DateTime windowStart, DateTime now, CancellationToken ct)
    {
        // The arm's own rows only (run + model): each model learns from what it decided, which is what an honest comparison needs.
        var rows = await Clicks.AsNoTracking()
            .Where(c => c.RunId == runId && c.ModelName == modelName && c.RoutedNetworkId != null)
            .Select(c => new { NetworkId = c.RoutedNetworkId!, c.ErrorCode, c.DisclosedAt, c.Commission })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.NetworkId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g =>
            {
                var settled = g.Where(r => r.ErrorCode == null && r.DisclosedAt != null && r.DisclosedAt <= now && r.Commission != null).ToList();
                var recent = settled.Where(r => r.DisclosedAt >= windowStart).ToList();
                return new NetworkEvidence(
                    g.Key,
                    settled.Count,
                    settled.Count == 0 ? null : Money.Round4(settled.Average(r => r.Commission!.Value)),
                    recent.Count,
                    recent.Count == 0 ? null : Money.Round4(recent.Average(r => r.Commission!.Value)),
                    settled.Count == 0 ? null : settled.Max(r => r.DisclosedAt),
                    g.Count(r => r.ErrorCode == null && (r.DisclosedAt == null || r.DisclosedAt > now)),
                    g.Count(r => r.ErrorCode != null));
            }, StringComparer.Ordinal);
    }

    public async Task RecordDecision(string clickId, ClicksIssueAndRoute.IRepository.DecisionModel decision, CancellationToken ct)
    {
        var click = await Find(clickId, ct);
        click.RecordDecision(decision.ModelName, decision.ModelVersion, decision.Decision, decision.ScoresJson, decision.Rationale, decision.DecidedAt);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<int> RecordClickFailure(int runId, CancellationToken ct)
    {
        var run = await unitOfWork.Repository<Run>().FindAsync(ct, runId) ?? throw new NotFoundException($"Run {runId} does not exist.");
        var failures = run.RecordClickFailure();
        await unitOfWork.SaveChangesAsync(ct);
        return failures;
    }

    public async Task ResetClickFailures(int runId, CancellationToken ct)
    {
        var run = await unitOfWork.Repository<Run>().FindAsync(ct, runId) ?? throw new NotFoundException($"Run {runId} does not exist.");
        if (run.ConsecutiveClickFailures == 0) return;
        run.ResetClickFailures();
        await unitOfWork.SaveChangesAsync(ct);
    }

    // ---- ClicksRoute ----

    public async Task<ClicksRoute.IRepository.RouteTarget?> GetRouteTarget(string clickId, string networkId, CancellationToken ct)
    {
        var click = await Clicks.AsNoTracking().Where(c => c.ClickId == clickId).Select(c => new { c.RunId }).FirstOrDefaultAsync(ct);
        if (click is null) return null;

        var networkName = await Networks.AsNoTracking()
                              .Where(n => n.RunId == click.RunId && n.NetworkId == networkId)
                              .Select(n => n.Name)
                              .FirstOrDefaultAsync(ct)
                          ?? networkId;

        return new ClicksRoute.IRepository.RouteTarget(click.RunId, networkName, await IsRunStopped(click.RunId, ct));
    }

    public async Task RecordRoute(string clickId, RouteAcknowledgment acknowledgment, string networkName, int attempts, CancellationToken ct)
    {
        var click = await Find(clickId, ct);
        click.RecordRoute(acknowledgment.NetworkId, networkName, acknowledgment.RoutedAt, attempts);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task RecordRejection(string clickId, string networkId, string networkName, DateTime at, string errorCode, int attempts, CancellationToken ct)
    {
        var click = await Find(clickId, ct);
        click.RecordRejection(networkId, networkName, at, errorCode, attempts);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task MarkUnconfirmed(string clickId, int attempts, CancellationToken ct)
    {
        var click = await Find(clickId, ct);
        click.MarkUnconfirmed(attempts);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public Task<bool> IsRunStopped(int runId, CancellationToken ct)
        => Runs.AsNoTracking().Where(r => r.Id == runId).Select(r => r.Status == RunStatus.Stopped).FirstAsync(ct);

    private async Task<ClickEntity> Find(string clickId, CancellationToken ct)
        => await unitOfWork.Repository<ClickEntity>().FindAsync(ct, clickId)
           ?? throw new NotFoundException($"Click {clickId} is not in the store.");
}
