using System.Text.Json;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Features.Evaluation.Compare;
using AffiliateOptimizer.Domain.Features.Networks.GetInsight;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using ClickEntity = AffiliateOptimizer.Repository.Entities.Click;

namespace AffiliateOptimizer.Repository.Repositories.Features;

public sealed class NetworksRepository(IAppUnitOfWork unitOfWork) : NetworksGetInsight.IRepository
{
    public Task<int?> ResolveRunId(int? runId, CancellationToken ct) => RunResolution.ResolveRunId(unitOfWork, runId, ct);

    public async Task<IReadOnlyList<Network>> GetNetworkSnapshot(int runId, CancellationToken ct)
    {
        var snapshots = await unitOfWork.Repository<RunNetworkSnapshot>().GetQuery().AsNoTracking()
            .Where(n => n.RunId == runId)
            .ToListAsync(ct);
        return snapshots.Select(s => s.ToNetwork()).ToList();
    }

    public async Task<IReadOnlyList<NetworksGetInsight.IRepository.Observation>> GetObservations(int? runId, CancellationToken ct)
    {
        var clicks = unitOfWork.Repository<ClickEntity>().GetQuery().AsNoTracking().Where(c => c.RoutedNetworkId != null);
        if (runId is { } id) clicks = clicks.Where(c => c.RunId == id);

        return await clicks
            .Select(c => new NetworksGetInsight.IRepository.Observation(c.RoutedNetworkId!, c.DisclosedAt, c.Commission, c.ErrorCode))
            .ToListAsync(ct);
    }
}

public sealed class EvaluationRepository(IAppUnitOfWork unitOfWork) : EvaluationCompare.IRepository
{
    public async Task<EvaluationCompare.IRepository.RunScope?> ResolveRun(int? runId, CancellationToken ct)
    {
        var id = await RunResolution.ResolveRunId(unitOfWork, runId, ct);
        if (id is null) return null;

        var run = await unitOfWork.Repository<Run>().GetQuery().AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.Id, r.ModelNamesJson, r.Interleaved, r.StartedAt, r.StoppedAt })
            .SingleAsync(ct);

        return new EvaluationCompare.IRepository.RunScope(run.Id, JsonSerializer.Deserialize<List<string>>(run.ModelNamesJson) ?? [], run.Interleaved, run.StartedAt, run.StoppedAt);
    }

    public async Task<IReadOnlyList<EvaluationCompare.IRepository.ArmObservation>> GetArmObservations(int runId, CancellationToken ct)
    {
        var rates = await unitOfWork.Repository<RunNetworkSnapshot>().GetQuery().AsNoTracking()
            .Where(n => n.RunId == runId)
            .ToDictionaryAsync(n => n.NetworkId, n => n.AdvertisedRate, StringComparer.Ordinal, ct);

        var clicks = await unitOfWork.Repository<ClickEntity>().GetQuery().AsNoTracking()
            .Where(c => c.RunId == runId)
            .Select(c => new { c.ModelName, c.RoutedNetworkId, c.DecidedNetworkId, c.ExpiresAt, c.RoutedAt, c.DisclosedAt, c.Commission, c.ErrorCode })
            .ToListAsync(ct);

        return clicks.Select(c =>
        {
            var chosen = c.RoutedNetworkId ?? c.DecidedNetworkId;
            decimal? advertised = chosen is not null && rates.TryGetValue(chosen, out var rate) ? rate : null;
            return new EvaluationCompare.IRepository.ArmObservation(c.ModelName, advertised, c.ExpiresAt, c.RoutedAt, c.DisclosedAt, c.Commission, c.ErrorCode);
        }).ToList();
    }
}

/// <summary>Which run an insight query means: the given one, else the active one, else the latest. Shared by the read slices.</summary>
internal static class RunResolution
{
    public static async Task<int?> ResolveRunId(IAppUnitOfWork unitOfWork, int? runId, CancellationToken ct)
    {
        var runs = unitOfWork.Repository<Run>().GetQuery().AsNoTracking();
        if (runId is { } id)
            return await runs.AnyAsync(r => r.Id == id, ct) ? id : null;

        var active = await runs.Where(r => r.Status != RunStatus.Stopped).OrderByDescending(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        return active ?? await runs.OrderByDescending(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
    }
}
