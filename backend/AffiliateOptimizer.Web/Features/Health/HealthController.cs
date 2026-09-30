using AffiliateOptimizer.Domain.Features.Health.GetStatus;
using AffiliateOptimizer.Web.Configuration;
using Common.Domain.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AffiliateOptimizer.Web.Features.Health;

[Route("api/health/[action]")]
[Authorize(Policy = Policies.Operators)]
public sealed class HealthController(IDispatcher dispatcher) : Controller
{
    [HttpGet]
    public Task<HealthGetStatus.Result> GetStatus(CancellationToken ct) => dispatcher.Send(new HealthGetStatus.Query(), ct);
}
