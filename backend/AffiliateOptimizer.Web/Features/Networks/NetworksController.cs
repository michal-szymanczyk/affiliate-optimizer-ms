using AffiliateOptimizer.Domain.Features.Networks.GetInsight;
using AffiliateOptimizer.Web.Configuration;
using Common.Domain.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AffiliateOptimizer.Web.Features.Networks;

[Route("api/networks/[action]")]
[Authorize(Policy = Policies.Operators)]
public sealed class NetworksController(IDispatcher dispatcher) : Controller
{
    [HttpGet]
    public Task<NetworksGetInsight.Result> GetInsight(NetworksGetInsight.Query query, CancellationToken ct) => dispatcher.Send(query, ct);
}
