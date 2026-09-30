using AffiliateOptimizer.Domain.Features.Clicks.GetCounters;
using AffiliateOptimizer.Domain.Features.Clicks.List;
using AffiliateOptimizer.Web.Configuration;
using Common.Domain.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AffiliateOptimizer.Web.Features.Clicks;

[Route("api/clicks/[action]")]
[Authorize(Policy = Policies.Operators)]
public sealed class ClicksController(IDispatcher dispatcher) : Controller
{
    [HttpGet]
    public Task<ClicksList.Result> GetAll(ClicksList.Query query, CancellationToken ct) => dispatcher.Send(query, ct);

    [HttpGet]
    public Task<ClicksGetCounters.Result> GetCounters(ClicksGetCounters.Query query, CancellationToken ct) => dispatcher.Send(query, ct);
}
