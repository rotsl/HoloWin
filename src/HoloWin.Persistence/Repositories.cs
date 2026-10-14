using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using HoloWin.Core;

namespace HoloWin.Persistence;

/// <summary>
/// JSON-backed profile repository. Stores each profile as a single JSON file
/// under <c>Profiles/</c>. Stores only feature vectors and classifier
/// parameters — never raw audio. Migration is performed on load.
/// </summary>
public sealed class JsonProfileRepository : IProfileRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;
    private readonly string _currentFile;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<Guid, string> _index = new();

    public JsonProfileRepository(string rootDirectory)
    {
        _root = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "Profiles"));
        Directory.CreateDirectory(Path.Combine(_root, "Evaluations"));
        _currentFile = Path.Combine(_root, "current-profile.txt");
    }

    public Task<IReadOnlyList<ProfileSummary>> ListAsync(CancellationToken ct)
    {
        var dir = Path.Combine(_root, "Profiles");
        var summaries = new List<ProfileSummary>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                using var fs = File.OpenRead(f);
                var p = JsonSerializer.Deserialize<HoloProfile>(fs, JsonOpts);
                if (p is null) continue;
                summaries.Add(new ProfileSummary
                {
                    Id = p.Id,
                    Name = p.Name,
                    DeskDescription = p.DeskDescription,
                    ModifiedAt = p.ModifiedAt,
                    PositiveExampleCount = p.PositiveExamples.Count,
                    NegativeExampleCount = p.NegativeExamples.Count,
                    CalibrationIsComplete = p.CalibrationIsComplete
                });
            }
            catch { /* skip malformed */ }
        }
        IReadOnlyList<ProfileSummary> result = summaries;
        return Task.FromResult(result);
    }

    public async Task<HoloProfile?> LoadAsync(Guid id, CancellationToken ct)
    {
        var path = Path.Combine(_root, "Profiles", id + ".json");
        if (!File.Exists(path)) return null;
        using var fs = File.OpenRead(path);
        var p = await JsonSerializer.DeserializeAsync<HoloProfile>(fs, JsonOpts, ct);
        return p is null ? null : MigrateProfile(p);
    }

    public async Task SaveAsync(HoloProfile profile, CancellationToken ct)
    {
        profile.ModifiedAt = DateTimeOffset.UtcNow;
        var path = Path.Combine(_root, "Profiles", profile.Id + ".json");
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, profile, JsonOpts, ct);
        File.Move(tmp, path, overwrite: true);
        _index[profile.Id] = path;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var path = Path.Combine(_root, "Profiles", id + ".json");
        if (File.Exists(path)) File.Delete(path);
        _index.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<HoloProfile?> GetCurrentAsync(CancellationToken ct)
    {
        if (!File.Exists(_currentFile)) return Task.FromResult<HoloProfile?>(null);
        var idStr = File.ReadAllText(_currentFile).Trim();
        if (!Guid.TryParse(idStr, out var id)) return Task.FromResult<HoloProfile?>(null);
        return LoadAsync(id, ct);
    }

    public async Task SetCurrentAsync(Guid id, CancellationToken ct)
    {
        await File.WriteAllTextAsync(_currentFile, id.ToString(), ct);
    }

    public static HoloProfile MigrateProfile(HoloProfile p)
    {
        if (p.SchemaVersion > HoloProfile.CurrentSchemaVersion)
        {
            // Future schema; refuse to load to avoid silent corruption.
            throw new InvalidOperationException(
                $"Profile schema version {p.SchemaVersion} is newer than supported {HoloProfile.CurrentSchemaVersion}.");
        }
        if (p.SchemaVersion < 1) throw new InvalidDataException("Invalid profile schema version.");
        if (p.SchemaVersion == 1)
        {
            p.NegativeExamples ??= Array.Empty<LabeledTap>();
            p.ActionMappings ??= new Dictionary<DeskZone, ZoneActionMapping>();
            p.EnabledTriggers ??= Array.Empty<string>();
            p.SchemaVersion = 2;
        }
        if (string.IsNullOrWhiteSpace(p.Name) || p.Id == Guid.Empty)
            throw new InvalidDataException("Profile must have a name and a nonempty ID.");
        if (p.PositiveExamples is null || p.NegativeExamples is null || p.ActionMappings is null)
            throw new InvalidDataException("Profile collections cannot be null.");
        foreach (var sample in p.PositiveExamples.Concat(p.NegativeExamples))
            if (sample.Feature is null || sample.Feature.Values.Count != sample.Feature.Names.Count
                || sample.Feature.Values.Any(v => !double.IsFinite(v)))
                throw new InvalidDataException("Profile contains invalid feature vectors.");
        p.GestureActions ??= new();
        p.FingerGestures ??= Array.Empty<FingerGesture>();
        p.LightRules ??= Array.Empty<LightRule>();
        p.PresenceRules ??= Array.Empty<PresenceRule>();
        foreach (var rule in p.PresenceRules) rule.Validate();
        if (p.PresenceRules.Select(r => r.Id).Distinct().Count() != p.PresenceRules.Count)
            throw new InvalidDataException("Duplicate presence rule IDs.");
        if (!double.IsFinite(p.CameraMinimumConfidence) || p.CameraMinimumConfidence < .5 || p.CameraMinimumConfidence > 1
            || !double.IsFinite(p.SwipeDistance) || p.SwipeDistance < .08 || p.SwipeDistance > .5
            || p.GestureHoldMilliseconds < 200 || p.GestureHoldMilliseconds > 3000)
            throw new InvalidDataException("Invalid camera gesture settings.");
        if (p.FingerGestures.Any(g => string.IsNullOrWhiteSpace(g.Id) || string.IsNullOrWhiteSpace(g.Name)
            || g.Fingers is null || g.Fingers.Length != 5 || g.Fingers.Any(f => !Enum.IsDefined(f)) || g.Fingers.All(f => f == FingerState.Any))
            || p.FingerGestures.Select(g => g.Id).Distinct().Count() != p.FingerGestures.Count
            || p.LightRules.Select(r => r.Id).Distinct().Count() != p.LightRules.Count)
            throw new InvalidDataException("Invalid gesture or light rule definitions.");
        foreach (var rule in p.LightRules) rule.Validate();
        return p;
    }
}

public sealed class JsonEvaluationReportRepository : IEvaluationReportRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;
    public JsonEvaluationReportRepository(string rootDirectory)
    {
        _root = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        Directory.CreateDirectory(Path.Combine(_root, "Evaluations"));
    }

    public async Task SaveAsync(EvaluationReport report, CancellationToken ct)
    {
        var path = Path.Combine(_root, "Evaluations", report.Id + ".json");
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, report, JsonOpts, ct);
    }

    public Task<IReadOnlyList<EvaluationReport>> ListAsync(Guid? profileIdFilter, CancellationToken ct)
    {
        var dir = Path.Combine(_root, "Evaluations");
        var list = new List<EvaluationReport>();
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<EvaluationReport>>(list);
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                using var fs = File.OpenRead(f);
                var r = JsonSerializer.Deserialize<EvaluationReport>(fs, JsonOpts);
                if (r is null) continue;
                if (profileIdFilter is { } pid && r.ProfileId != pid) continue;
                list.Add(r);
            }
            catch { /* skip */ }
        }
        IReadOnlyList<EvaluationReport> result = list.OrderByDescending(r => r.CreatedAtUtc).ToList();
        return Task.FromResult(result);
    }

    public Task DeleteAsync(string reportId, CancellationToken ct)
    {
        if (!Guid.TryParse(reportId, out _)) throw new ArgumentException("Invalid report ID.", nameof(reportId));
        var path = Path.Combine(_root, "Evaluations", reportId + ".json");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
