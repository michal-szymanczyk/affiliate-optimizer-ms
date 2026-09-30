using AffiliateOptimizer.Domain.Features.LoopEvents.Dismiss;
using AffiliateOptimizer.Web.Configuration;
using Common.Domain.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AffiliateOptimizer.Web.Features.LoopEvents;

[Route("api/loop-events/[action]")]
[Authorize(Policy = Policies.Operators)]
public sealed class LoopEventsController(IDispatcher dispatcher) : Controller
{
    [HttpPost]
    public Task Dismiss([FromBody] LoopEventsDismiss.Command command, CancellationToken ct) => dispatcher.Send(command, ct);
}
