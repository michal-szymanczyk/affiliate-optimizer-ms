using Common.Domain.Exceptions;
using FluentValidation;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;
using Common.Domain.Validation;

namespace AffiliateOptimizer.Domain.Features.Runs.Resume;

/// <summary>Resumes issuing clicks for a paused run. Exactly one click chain exists per host, because the loop reads the run's status each turn.</summary>
public static partial class RunsResume
{
    public sealed record Command(int RunId) : IRequest;

    public sealed class CommandValidator : ValidatorBase<Command>
    {
        public CommandValidator() => RuleFor(c => c.RunId).GreaterThan(0);
    }

    public interface IRepository : IFeatureRepository
    {
        Task<bool> Resume(int runId, DateTime now, CancellationToken ct);
    }

    public sealed class CommandHandler(IRepository repository, ISystemTime time) : CommandHandler<Command>
    {
        public override async Task Handle(Command command, CancellationToken ct)
        {
            if (!await repository.Resume(command.RunId, time.Now(), ct))
                throw new NotFoundException($"Run {command.RunId} does not exist.");
        }
    }
}
