using System.Text.Json;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Features.Runs.Common;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using Common.Domain.Exceptions;

namespace AffiliateOptimizer.Repository.Entities;

/// <summary>
/// The unit of provenance: which models decided, with which parameters, against which catalogue, from when.
/// Also the loop's control record: the click loop reads Status every turn, so exactly one click chain exists per host.
/// </summary>
public class Run
{
    public int Id { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? StoppedAt { get; private set; }
    public DateTime? PausedAt { get; private set; }
    public RunStatus Status { get; private set; }
    public string ModelNamesJson { get; private set; } = "[]";
    public bool Interleaved { get; private set; }
    public string ModelParametersJson { get; private set; } = "{}";
    public int EvidenceWindowMinutes { get; private set; }
    public string CredentialFingerprint { get; private set; } = string.Empty;
    public string AppVersion { get; private set; } = string.Empty;

    /// <summary>Next click ordinal to allocate; interleaved runs pick the model from it.</summary>
    public long ClickOrdinal { get; private set; }

    public int ConsecutiveClickFailures { get; private set; }
    public DateTime? LastClickIssuedAt { get; private set; }

    public ICollection<RunNetworkSnapshot> Networks { get; private set; } = new List<RunNetworkSnapshot>();
    public ICollection<RunMerchantSnapshot> Merchants { get; private set; } = new List<RunMerchantSnapshot>();

    private Run() { }

    public static Run Start(RunsStart.IRepository.RunWriteModel model) => new()
    {
        StartedAt = model.StartedAt,
        Status = RunStatus.Running,
        ModelNamesJson = JsonSerializer.Serialize(model.Assignment.Names),
        Interleaved = model.Assignment.Interleaved,
        ModelParametersJson = model.ParametersJson,
        EvidenceWindowMinutes = model.EvidenceWindowMinutes,
        CredentialFingerprint = model.CredentialFingerprint,
        AppVersion = model.AppVersion,
    };

    public ModelAssignment Assignment => new(JsonSerializer.Deserialize<List<string>>(ModelNamesJson) ?? [], Interleaved);

    public void Pause(DateTime now)
    {
        if (Status != RunStatus.Running)
            throw new LogicErrorException($"Run {Id} is {Status.ToString().ToLowerInvariant()}; only a running run can be paused.");
        Status = RunStatus.Paused;
        PausedAt = now;
    }

    public void Resume(DateTime now)
    {
        if (Status != RunStatus.Paused)
            throw new LogicErrorException($"Run {Id} is {Status.ToString().ToLowerInvariant()}; only a paused run can be resumed.");
        Status = RunStatus.Running;
        PausedAt = null;
        _ = now;
    }

    public void Stop(DateTime now)
    {
        if (Status == RunStatus.Stopped)
            throw new LogicErrorException($"Run {Id} is already stopped.");
        Status = RunStatus.Stopped;
        StoppedAt = now;
        PausedAt = null;
    }

    public long AllocateOrdinal() => ClickOrdinal++;

    public int RecordClickFailure() => ++ConsecutiveClickFailures;

    public void ResetClickFailures() => ConsecutiveClickFailures = 0;

    public void RecordClickIssued(DateTime now) => LastClickIssuedAt = now;

    /// <summary>Explicit projection to the shared read model (ArchitectureSimple.md §6.2 a).</summary>
    public ActiveRun ToActiveRun() => new(
        Id,
        Status,
        Assignment,
        EvidenceWindowMinutes,
        ConsecutiveClickFailures,
        StartedAt,
        PausedAt,
        CredentialFingerprint,
        AppVersion);
}
