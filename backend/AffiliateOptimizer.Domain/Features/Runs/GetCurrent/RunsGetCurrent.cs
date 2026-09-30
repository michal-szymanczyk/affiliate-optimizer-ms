using AffiliateOptimizer.Domain.Features.Runs.Common;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Domain.Features.Runs.GetCurrent;

/// <summary>The active run (running or paused), or null. Carries serverNow so the UI never consults its own clock.</summary>
public static partial class RunsGetCurrent
{
    public sealed record Query : IRequest<Result>;

    public sealed record Result(ActiveRunModel? Run, DateTime ServerNow);

    public sealed record ActiveRunModel(
        int RunId,
        RunStatus Status,
        IReadOnlyList<string> ModelNames,
        bool Interleaved,
        DateTime StartedAt,
        DateTime? PausedAt,
        int EvidenceWindowMinutes,
        string CredentialFingerprint,
        string AppVersion)
    {
        /// <summary>Mapping constructor from the shared read model.</summary>
        public ActiveRunModel(ActiveRun run) : this(
            run.Id,
            run.Status,
            run.Assignment.Names,
            run.Assignment.Interleaved,
            run.StartedAt,
            run.PausedAt,
            run.EvidenceWindowMinutes,
            run.CredentialFingerprint,
            run.AppVersion) { }
    }

    public interface IRepository : IFeatureRepository
    {
        Task<ActiveRun?> GetActiveRun(CancellationToken ct);
    }

    public sealed class QueryHandler(IRepository repository, ISystemTime time) : IRequestHandler<Query, Result>
    {
        public async Task<Result> Handle(Query query, CancellationToken ct)
        {
            var run = await repository.GetActiveRun(ct);
            return new Result(run is null ? null : new ActiveRunModel(run), time.Now());
        }
    }
}
