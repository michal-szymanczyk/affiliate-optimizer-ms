using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.Loop;
using AffiliateOptimizer.Domain.Features.Clicks.IssueAndRoute;
using AffiliateOptimizer.Domain.Features.Commissions.Poll;
using AffiliateOptimizer.Domain.Features.LoopEvents.Common;
using AffiliateOptimizer.Domain.Features.Runs.GetCurrent;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Domain.Features.Runs.Stop;
using Common.Domain.Requests;
using Common.Domain.Time;

namespace AffiliateOptimizer.Web.Infrastructure.Loops;

/// <summary>
/// A loop owns scheduling only. Every turn runs in its own DI scope as one Domain command (ArchitectureSimple.md
/// §4.7: background jobs depend on IDispatcher only); the command's result says how long to wait before the next
/// turn. Waiting happens after the turn completes, so pacing is a fixed delay, as in the original client. A turn
/// that throws is logged, backed off exponentially, and never kills the loop.
/// </summary>
public abstract class LoopService(
    IServiceScopeFactory scopes,
    ILoopHeartbeat heartbeat,
    TimeProvider timeProvider,
    ISystemTime time,
    ILogger logger) : BackgroundService
{
    protected abstract LoopKind Kind { get; }

    protected abstract Task<(TimeSpan NextDelay, string Outcome)> Turn(IDispatcher dispatcher, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();   // let the host finish starting before the first turn
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                using var scope = scopes.CreateScope();
                var (nextDelay, outcome) = await Turn(scope.ServiceProvider.GetRequiredService<IDispatcher>(), stoppingToken);
                failures = 0;
                delay = nextDelay;
                heartbeat.Beat(Kind, outcome, time.Now());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failures++;
                delay = LoopPacing.BackoffAfterFailure(failures, null);
                logger.LogError(exception, "{Loop} loop turn failed ({Failures} in a row); next attempt in {Delay}", Kind, failures, delay);
                heartbeat.Beat(Kind, "error", time.Now());
            }

            try
            {
                if (delay == TimeSpan.Zero) await Task.Yield();
                else await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public sealed class ClickLoopService(IServiceScopeFactory scopes, ILoopHeartbeat heartbeat, TimeProvider timeProvider, ISystemTime time, ILogger<ClickLoopService> logger)
    : LoopService(scopes, heartbeat, timeProvider, time, logger)
{
    protected override LoopKind Kind => LoopKind.Click;

    protected override async Task<(TimeSpan NextDelay, string Outcome)> Turn(IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Send(new ClicksIssueAndRoute.Command(), ct);
        return (result.NextDelay, result.Outcome.ToString());
    }
}

public sealed class CommissionPollLoopService(IServiceScopeFactory scopes, ILoopHeartbeat heartbeat, TimeProvider timeProvider, ISystemTime time, ILogger<CommissionPollLoopService> logger)
    : LoopService(scopes, heartbeat, timeProvider, time, logger)
{
    protected override LoopKind Kind => LoopKind.Poll;

    protected override async Task<(TimeSpan NextDelay, string Outcome)> Turn(IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Send(new CommissionsPoll.Command(), ct);
        return (result.NextDelay, result.Outcome);
    }
}

/// <summary>
/// Runs once before the loops start: a run that was active when the host last stopped is continued (or stopped,
/// per Loop:ResumeActiveRunOnStartup) with an informational event either way, and Loop:AutoStartModel starts a run
/// for unattended operation. Failures are logged, not fatal: the operator can always start a run by hand.
/// </summary>
public sealed class LoopStartupService(IServiceScopeFactory scopes, LoopOptions options, ISystemTime time, ILogger<LoopStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var events = scope.ServiceProvider.GetRequiredService<ILoopEventWriter>();

            var current = await dispatcher.Send(new RunsGetCurrent.Query(), cancellationToken);
            if (current.Run is { } run)
            {
                if (options.ResumeActiveRunOnStartup)
                {
                    await events.Add(LoopEventModel.Info(run.RunId, LoopKind.Click, LoopEventCodes.HostRestarted,
                        $"Host restarted; run {run.RunId} continues ({run.Status.ToString().ToLowerInvariant()}). Clicks issued before the restart resolve through the feed as usual.", time.Now()), cancellationToken);
                    logger.LogInformation("Continuing run {RunId} ({Status}) after restart", run.RunId, run.Status);
                }
                else
                {
                    await dispatcher.Send(new RunsStop.Command(run.RunId), cancellationToken);
                    await events.Add(LoopEventModel.Info(run.RunId, LoopKind.Click, LoopEventCodes.HostRestarted,
                        $"Host restarted; run {run.RunId} was stopped because Loop:ResumeActiveRunOnStartup is off.", time.Now()), cancellationToken);
                    logger.LogInformation("Stopped run {RunId} after restart (ResumeActiveRunOnStartup is off)", run.RunId);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(options.AutoStartModel))
            {
                var started = await dispatcher.Send(new RunsStart.Command([options.AutoStartModel], Interleaved: false, EvidenceWindowMinutes: null), cancellationToken);
                logger.LogInformation("Auto-started run {RunId} with model {Model}", started.RunId, options.AutoStartModel);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Start-up run policy failed; the loops start anyway and the operator can start a run by hand");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
