using System.Collections.Concurrent;
using AffiliateOptimizer.Domain.Common.Loop;

namespace AffiliateOptimizer.Web.Infrastructure.Loops;

/// <summary>Process-local liveness of the two loops. Everything else about a loop is persisted.</summary>
public sealed class LoopHeartbeat : ILoopHeartbeat
{
    private readonly ConcurrentDictionary<LoopKind, LoopBeat> _beats = new();

    public void Beat(LoopKind loop, string outcome, DateTime at) => _beats[loop] = new LoopBeat(at, outcome);

    public LoopBeat? Last(LoopKind loop) => _beats.TryGetValue(loop, out var beat) ? beat : null;
}
