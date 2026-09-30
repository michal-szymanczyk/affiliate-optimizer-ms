using AffiliateOptimizer.Domain.Features.Models.GetAll;
using AffiliateOptimizer.Web.Configuration;
using Common.Domain.Lists;
using Common.Domain.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AffiliateOptimizer.Web.Features.Models;

[Route("api/models/[action]")]
[Authorize(Policy = Policies.Operators)]
public sealed class ModelsController(IDispatcher dispatcher) : Controller
{
    [HttpGet]
    public Task<List<ModelsGetAll.Model>> GetAll(CancellationToken ct) => dispatcher.Send(new ListQuery<ModelsGetAll.Model>(), ct);
}
