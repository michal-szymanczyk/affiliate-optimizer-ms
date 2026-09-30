using System.Text.Json;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Clicks;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Domain.Features.Runs.GetAll;
using AffiliateOptimizer.Domain.Features.Runs.GetCurrent;
using AffiliateOptimizer.Domain.Features.Runs.Pause;
using AffiliateOptimizer.Domain.Features.Runs.Resume;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Domain.Features.Runs.Stop;
using AffiliateOptimizer.Repository.Entities;
using Common.Domain.Lists;
using Common.Repository;
using Microsoft.EntityFrameworkCore;
using ClickEntity = AffiliateOptimizer.Repository.Entities.Click;

namespace AffiliateOptimizer.Repository.Repositories.Features;

/// <summary>Run control and lookup. One class for the area, implementing every run slice's contract (ArchitectureSimple.md §7.2).</summary>
public sealed class RunsRepository(IAppUnitOfWork unitOfWork) :
    RunsStart.IRepository,
    RunsPause.IRepository,
    RunsResume.IRepository,
    RunsStop.IRepository,
    RunsGetCurrent.IRepository,
    RunsGetAll.IRepository
{
    private IQueryable<Run> Runs => unitOfWork.Repository<Run>().GetQuery();

    /// <summary>
    /// Sortable scalar view of a run; the totals are added in memory for the page only. An object initializer,
    /// not a positional constructor: EF Core can sort and filter on members of an initializer projection but
    /// not on members of a constructor call.
    /// </summary>
    private sealed record RunRow
    {
        public required int Id { get; init; }
        public required RunStatus Status { get; init; }
        public required DateTime StartedAt { get; init; }
        public required DateTime? StoppedAt { get; init; }
        public required string ModelNamesJson { get; init; }
        public required bool Interleaved { get; init; }
        public required int EvidenceWindowMinutes { get; init; }
        public required string CredentialFingerprint { get; init; }
        public required string AppVersion { get; init; }
    }

    public async Task<PagedList<RunsGetAll.Model>> GetRuns(IDynamicListParams listParams, DateTime now, CancellationToken ct)
    {
        var page = await Runs.AsNoTracking()
            .Select(r => new RunRow
            {
                Id = r.Id,
                Status = r.Status,
                StartedAt = r.StartedAt,
                StoppedAt = r.StoppedAt,
                ModelNamesJson = r.ModelNamesJson,
                Interleaved = r.Interleaved,
                EvidenceWindowMinutes = r.EvidenceWindowMinutes,
                CredentialFingerprint = r.CredentialFingerprint,
                AppVersion = r.AppVersion,
            })
            .ToSortedPagedList(listParams, nameof(RunRow.Id), null, ct);

        var ids = page.Items.Select(r => r.Id).ToList();
        var facts = await unitOfWork.Repository<ClickEntity>().GetQuery().AsNoTracking()
            .Where(c => ids.Contains(c.RunId))
            .Select(c => new { c.RunId, Fact = new ClickFact(c.ExpiresAt, c.RoutedAt, c.DisclosedAt, c.ErrorCode, c.Commission) })
            .ToListAsync(ct);
        var totals = facts.GroupBy(f => f.RunId).ToDictionary(g => g.Key, g => RunCounters.Compute(g.Select(f => f.Fact), now));

        var items = page.Items.Select(r =>
        {
            var counters = totals.GetValueOrDefault(r.Id);
            return new RunsGetAll.Model(
                r.Id, r.Status, r.StartedAt, r.StoppedAt,
                JsonSerializer.Deserialize<List<string>>(r.ModelNamesJson) ?? [],
                r.Interleaved, r.EvidenceWindowMinutes, r.CredentialFingerprint, r.AppVersion,
                counters?.Processed ?? 0, counters?.Settled ?? 0, counters?.TotalCommission ?? 0m, counters?.PerSettledClick);
        }).ToList();

        return new PagedList<RunsGetAll.Model>(items, page.PageIndex, page.PageSize, page.TotalItems);
    }

    public Task<bool> HasActiveRun(CancellationToken ct)
        => Runs.AnyAsync(r => r.Status != RunStatus.Stopped, ct);

    public async Task<int> CreateRun(RunsStart.IRepository.RunWriteModel model, IReadOnlyList<Network> networks, IReadOnlyList<Merchant> merchants, CancellationToken ct)
    {
        var run = Run.Start(model);
        foreach (var network in networks) run.Networks.Add(RunNetworkSnapshot.From(network));
        foreach (var merchant in merchants) run.Merchants.Add(RunMerchantSnapshot.From(merchant));

        await unitOfWork.Repository<Run>().AddAsync(run, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return run.Id;
    }

    public Task<bool> Pause(int runId, DateTime now, CancellationToken ct) => Transition(runId, run => run.Pause(now), ct);

    public Task<bool> Resume(int runId, DateTime now, CancellationToken ct) => Transition(runId, run => run.Resume(now), ct);

    public Task<bool> Stop(int runId, DateTime now, CancellationToken ct) => Transition(runId, run => run.Stop(now), ct);

    public async Task<ActiveRun?> GetActiveRun(CancellationToken ct)
    {
        var run = await Runs.AsNoTracking()
            .Where(r => r.Status != RunStatus.Stopped)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        return run?.ToActiveRun();
    }

    private async Task<bool> Transition(int runId, Action<Run> transition, CancellationToken ct)
    {
        var run = await unitOfWork.Repository<Run>().FindAsync(ct, runId);
        if (run is null) return false;

        transition(run);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
