using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Loop;
using Common.Domain.Persistence;

namespace AffiliateOptimizer.Domain.Features.LoopEvents.Common;

/// <summary>
/// A loop-level event: the persisted replacement for the client's single "feed error" slot. Open events are
/// what the operator sees; a later success of the same loop resolves them, and the operator may dismiss the
/// dismissible ones. UNAUTHORIZED is never dismissible: only working credentials make it go away.
/// </summary>
public sealed record LoopEventModel(
    int? RunId,
    LoopKind Loop,
    LoopEventKind Kind,
    string Code,
    string Message,
    string? ClickId,
    DateTime OccurredAt,
    DateTime? RetryAt,
    bool Dismissible)
{
    public static LoopEventModel Api(int? runId, LoopKind loop, string code, string message, DateTime occurredAt, DateTime? retryAt, string? clickId = null)
        => new(runId, loop, LoopEventKind.Api, code, message, clickId, occurredAt, retryAt, Dismissible: code != AffiliateServiceErrorCodes.Unauthorized);

    public static LoopEventModel Transport(int? runId, LoopKind loop, string message, DateTime occurredAt, DateTime? retryAt, string? clickId = null)
        => new(runId, loop, LoopEventKind.Transport, LoopEventCodes.Transport, message, clickId, occurredAt, retryAt, Dismissible: true);

    public static LoopEventModel Unconfirmed(int runId, string clickId, DateTime occurredAt)
        => new(runId, LoopKind.Click, LoopEventKind.Unconfirmed, LoopEventCodes.RouteUnconfirmed,
            "The route response was lost after three attempts. Commission polling will reconcile the click if the server accepted it.",
            clickId, occurredAt, null, Dismissible: true);

    public static LoopEventModel ClientError(int? runId, LoopKind loop, string message, DateTime occurredAt, DateTime? retryAt)
        => new(runId, loop, LoopEventKind.ClientError, LoopEventCodes.ClientError, message, null, occurredAt, retryAt, Dismissible: true);

    public static LoopEventModel ModelError(int runId, string clickId, string modelName, string message, DateTime occurredAt)
        => new(runId, LoopKind.Click, LoopEventKind.ModelError, LoopEventCodes.ModelError, $"{modelName} threw while choosing: {message}", clickId, occurredAt, null, Dismissible: true);

    public static LoopEventModel Info(int? runId, LoopKind loop, string code, string message, DateTime occurredAt)
        => new(runId, loop, LoopEventKind.Info, code, message, null, occurredAt, null, Dismissible: true);
}

/// <summary>Shared by the three loop slices (ArchitectureSimple.md §3.1: shared repository interfaces live in Area/Common).</summary>
public interface ILoopEventWriter : IFeatureRepository
{
    Task Add(LoopEventModel loopEvent, CancellationToken ct);

    /// <summary>A success of the loop proves its open faults are gone. Returns how many events were resolved.</summary>
    Task<int> ResolveOpen(LoopKind loop, DateTime now, CancellationToken ct);
}
