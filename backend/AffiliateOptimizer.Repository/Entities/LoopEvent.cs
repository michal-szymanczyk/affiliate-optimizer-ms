using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using Common.Domain.Exceptions;

namespace AffiliateOptimizer.Repository.Entities;

/// <summary>A loop-level event (see <see cref="LoopEventModel"/>). Open = neither dismissed nor resolved.</summary>
public class LoopEvent
{
    public long Id { get; private set; }
    public int? RunId { get; private set; }
    public LoopKind Loop { get; private set; }
    public LoopEventKind Kind { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? ClickId { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public DateTime? RetryAt { get; private set; }
    public bool Dismissible { get; private set; }
    public DateTime? DismissedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    private LoopEvent() { }

    public static LoopEvent Create(LoopEventModel model) => new()
    {
        RunId = model.RunId,
        Loop = model.Loop,
        Kind = model.Kind,
        Code = model.Code,
        Message = model.Message,
        ClickId = model.ClickId,
        OccurredAt = model.OccurredAt,
        RetryAt = model.RetryAt,
        Dismissible = model.Dismissible,
    };

    public bool IsOpen => DismissedAt is null && ResolvedAt is null;

    public void Dismiss(DateTime now)
    {
        if (!Dismissible)
            throw new LogicErrorException($"{Code} cannot be dismissed; it clears only when the cause is fixed.");
        DismissedAt ??= now;
    }

    public void Resolve(DateTime now) => ResolvedAt ??= now;
}
