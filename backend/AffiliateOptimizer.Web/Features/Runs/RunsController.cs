using AffiliateOptimizer.Domain.Features.Runs.GetAll;
using AffiliateOptimizer.Domain.Features.Runs.GetCurrent;
using AffiliateOptimizer.Domain.Features.Runs.Pause;
using AffiliateOptimizer.Domain.Features.Runs.Resume;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Domain.Features.Runs.Stop;
using AffiliateOptimizer.Web.Configuration;
using Common.Domain.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AffiliateOptimizer.Web.Features.Runs;

[Route("api/runs/[action]")]
[Authorize(Policy = Policies.Operators)]
public sealed class RunsController(IDispatcher dispatcher) : Controller
{
    [HttpPost]
    public Task<RunsStart.Result> Start([FromBody] RunsStart.Command command, CancellationToken ct) => dispatcher.Send(command, ct);

    [HttpPost]
    public Task Pause([FromBody] RunsPause.Command command, CancellationToken ct) => dispatcher.Send(command, ct);

    [HttpPost]
    public Task Resume([FromBody] RunsResume.Command command, CancellationToken ct) => dispatcher.Send(command, ct);

    [HttpPost]
    public Task Stop([FromBody] RunsStop.Command command, CancellationToken ct) => dispatcher.Send(command, ct);

    [HttpGet]
    public Task<RunsGetCurrent.Result> GetCurrent(CancellationToken ct) => dispatcher.Send(new RunsGetCurrent.Query(), ct);

    [HttpGet]
    public Task<RunsGetAll.Result> GetAll(RunsGetAll.Query query, CancellationToken ct) => dispatcher.Send(query, ct);
}
