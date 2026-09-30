using Common.Domain.Exceptions;
using FluentValidation;
using Common.Domain.Persistence;
using Common.Domain.Requests;
using Common.Domain.Time;
using Common.Domain.Validation;

namespace AffiliateOptimizer.Domain.Features.LoopEvents.Dismiss;

public static partial class LoopEventsDismiss
{
    public sealed record Command(long EventId) : IRequest;

    public sealed class CommandValidator : ValidatorBase<Command>
    {
        public CommandValidator() => RuleFor(c => c.EventId).GreaterThan(0);
    }

    public interface IRepository : IFeatureRepository
    {
        /// <summary>False when the event does not exist. Throws LogicErrorException when it is not dismissible.</summary>
        Task<bool> Dismiss(long eventId, DateTime now, CancellationToken ct);
    }

    public sealed class CommandHandler(IRepository repository, ISystemTime time) : CommandHandler<Command>
    {
        public override async Task Handle(Command command, CancellationToken ct)
        {
            if (!await repository.Dismiss(command.EventId, time.Now(), ct))
                throw new NotFoundException($"Loop event {command.EventId} does not exist.");
        }
    }
}
