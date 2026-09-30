using Common.Domain.Exceptions;
using FluentValidation;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;
using Common.Domain.Validation;

namespace AffiliateOptimizer.Domain.Features.Runs.Stop;

/// <summary>Ends a run for good. Its clicks keep settling through the feed; a new run must be started to route again.</summary>
public static partial class RunsStop
{
    public sealed record Command(int RunId) : IRequest;

    public sealed class CommandValidator : ValidatorBase<Command>
    {
        public CommandValidator() => RuleFor(c => c.RunId).GreaterThan(0);
    }

    public interface IRepository : IFeatureRepository
    {
        Task<bool> Stop(int runId, DateTime now, CancellationToken ct);
    }

    public sealed class CommandHandler(IRepository repository, ISystemTime time) : CommandHandler<Command>
    {
        public override async Task Handle(Command command, CancellationToken ct)
        {
            if (!await repository.Stop(command.RunId, time.Now(), ct))
                throw new NotFoundException($"Run {command.RunId} does not exist.");
        }
    }
}
