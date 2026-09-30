using Common.Domain.Exceptions;
using FluentValidation;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;
using Common.Domain.Validation;

namespace AffiliateOptimizer.Domain.Features.Runs.Pause;

/// <summary>Stops issuing clicks. Commission polling continues, so clicks already routed still resolve.</summary>
public static partial class RunsPause
{
    public sealed record Command(int RunId) : IRequest;

    public sealed class CommandValidator : ValidatorBase<Command>
    {
        public CommandValidator() => RuleFor(c => c.RunId).GreaterThan(0);
    }

    public interface IRepository : IFeatureRepository
    {
        /// <summary>False when the run does not exist. The entity throws LogicErrorException for an invalid transition.</summary>
        Task<bool> Pause(int runId, DateTime now, CancellationToken ct);
    }

    public sealed class CommandHandler(IRepository repository, ISystemTime time) : CommandHandler<Command>
    {
        public override async Task Handle(Command command, CancellationToken ct)
        {
            if (!await repository.Pause(command.RunId, time.Now(), ct))
                throw new NotFoundException($"Run {command.RunId} does not exist.");
        }
    }
}
