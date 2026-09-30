using AffiliateOptimizer.Domain.Features.Evaluation.Compare;
using AffiliateOptimizer.Web.Configuration;
using Common.Domain.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AffiliateOptimizer.Web.Features.Evaluation;

[Route("api/evaluation/[action]")]
[Authorize(Policy = Policies.Operators)]
public sealed class EvaluationController(IDispatcher dispatcher) : Controller
{
    [HttpGet]
    public Task<EvaluationCompare.Result> Compare(EvaluationCompare.Query query, CancellationToken ct) => dispatcher.Send(query, ct);
}
