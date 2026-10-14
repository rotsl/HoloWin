using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using HoloWin.Core;

namespace HoloWin.Persistence;

public interface IProfileRepository
{
    Task<IReadOnlyList<ProfileSummary>> ListAsync(CancellationToken ct);
    Task<HoloProfile?> LoadAsync(Guid id, CancellationToken ct);
    Task SaveAsync(HoloProfile profile, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<HoloProfile?> GetCurrentAsync(CancellationToken ct);
    Task SetCurrentAsync(Guid id, CancellationToken ct);
}

public interface IEvaluationReportRepository
{
    Task SaveAsync(EvaluationReport report, CancellationToken ct);
    Task<IReadOnlyList<EvaluationReport>> ListAsync(Guid? profileIdFilter, CancellationToken ct);
    Task DeleteAsync(string reportId, CancellationToken ct);
}

public sealed class EvaluationReport
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required Guid ProfileId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required SensingStrategy Strategy { get; init; }
    public required int TotalTaps { get; init; }
    public required int CorrectTaps { get; init; }
    public required int RejectedTaps { get; init; }
    public required double OverallAccuracy { get; init; }
    public required double MedianLatencyMs { get; init; }
    public required IReadOnlyDictionary<DeskZone, int> PerZoneCorrect { get; init; }
    public required IReadOnlyDictionary<DeskZone, int> PerZoneTotal { get; init; }
    public required int[][] ConfusionMatrix { get; init; }
}
