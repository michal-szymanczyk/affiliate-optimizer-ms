using System.Text.Json;
using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Optimization;
using Common.Domain.Exceptions;
using FluentValidation;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;
using Common.Domain.Validation;

namespace AffiliateOptimizer.Domain.Features.Runs.Start;

/// <summary>
/// Starts a run: the unit of provenance. Fetches the merchant and network catalogues (a failure here is the
/// operator's "The client cannot start"), snapshots them, records which models decide with which parameters,
/// and anchors the run in time. Only one run may be active per host.
/// </summary>
public static partial class RunsStart
{
    public sealed record Command(IReadOnlyList<string> ModelNames, bool Interleaved, int? EvidenceWindowMinutes) : IRequest<Result>;

    public sealed record Result(int RunId);

    public sealed class CommandValidator : ValidatorBase<Command>
    {
        public CommandValidator(IOptimizationModelCatalog catalog)
        {
            RuleFor(c => c.ModelNames).NotEmpty().WithMessage("Choose at least one optimization model.");
            RuleFor(c => c.ModelNames)
                .Must(names => names.Distinct(StringComparer.Ordinal).Count() == names.Count)
                .WithMessage("Each optimization model may be assigned once.");
            RuleForEach(c => c.ModelNames)
                .Must(catalog.Contains)
                .WithMessage((_, name) => $"\"{name}\" is not a known optimization model. Known: {string.Join(", ", catalog.All.Select(m => m.Name))}.");
            RuleFor(c => c.EvidenceWindowMinutes)
                .InclusiveBetween(1, 120)
                .When(c => c.EvidenceWindowMinutes.HasValue);
        }
    }

    public interface IRepository : IFeatureRepository
    {
        Task<bool> HasActiveRun(CancellationToken ct);

        /// <summary>Creates the run with its catalogue snapshots in one unit of work and returns its id.</summary>
        Task<int> CreateRun(RunWriteModel run, IReadOnlyList<Network> networks, IReadOnlyList<Merchant> merchants, CancellationToken ct);

        /// <summary>Everything the repository needs to persist a new run. Derived values are decided in the handler, once.</summary>
        public sealed record RunWriteModel(
            ModelAssignment Assignment,
            string ParametersJson,
            int EvidenceWindowMinutes,
            string CredentialFingerprint,
            string AppVersion,
            DateTime StartedAt);
    }

    public sealed class CommandHandler(
        IRepository repository,
        IAffiliateServiceClient client,
        IOptimizationModelCatalog catalog,
        ICredentialContext credentials,
        EvaluationOptions evaluation,
        ISystemTime time) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command command, CancellationToken ct)
        {
            if (await repository.HasActiveRun(ct))
                throw new LogicErrorException("A run is already active. Stop it before starting another one.");

            var merchantsTask = client.GetMerchants(ct);
            var networksTask = client.GetNetworks(ct);
            await Task.WhenAll(merchantsTask, networksTask);      // a failure propagates: the operator sees the server's own code
            var merchants = await merchantsTask;
            var networks = await networksTask;

            var assignment = new ModelAssignment(command.ModelNames, command.Interleaved && command.ModelNames.Count > 1);
            var parameters = command.ModelNames.ToDictionary(
                name => name,
                name => JsonSerializer.Deserialize<JsonElement>(catalog.Get(name).ParametersJson),
                StringComparer.Ordinal);

            var run = new IRepository.RunWriteModel(
                assignment,
                JsonSerializer.Serialize(parameters),
                command.EvidenceWindowMinutes ?? evaluation.RecentWindowMinutes,
                credentials.Fingerprint,
                Consts.AppVersion,
                time.Now());

            return new Result(await repository.CreateRun(run, networks, merchants, ct));
        }
    }
}
