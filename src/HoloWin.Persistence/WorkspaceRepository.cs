using System.Text.Json;
using System.Text.Json.Serialization;
using HoloWin.Core;

namespace HoloWin.Persistence;

public sealed class WorkspaceSettings
{
    public bool SetupComplete { get; set; }
    public bool MicrophoneEnabled { get; set; }
    public string? MicrophoneId { get; set; }
    public int CameraIndex { get; set; }
    public FusionMode FusionMode { get; set; } = FusionMode.AudioOnly;
    public bool ActiveProbeEnabled { get; set; }
    public double ProbeAmplitude { get; set; } = ActiveProbe.DefaultAmplitude;
    public bool DebugCaptureEnabled { get; set; }
    public bool MotionEnabled { get; set; }
    public string Theme { get; set; } = "Default";
    public Dictionary<string, string> Hotkeys { get; set; } = new();
}

public sealed class ActionLibrary
{
    public List<HoloAction> Actions { get; set; } = new();
    public List<HoloMacro> Macros { get; set; } = new();
}

public sealed class WorkspaceRepository
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _root;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    public WorkspaceRepository(string root) { _root = root; Directory.CreateDirectory(root); }
    public Task<WorkspaceSettings> LoadSettingsAsync(CancellationToken ct = default) => ReadAsync<WorkspaceSettings>("settings.json", ct);
    public Task<ActionLibrary> LoadActionsAsync(CancellationToken ct = default) => ReadAsync<ActionLibrary>("actions.json", ct);
    public Task SaveSettingsAsync(WorkspaceSettings value, CancellationToken ct = default) => WriteAsync("settings.json", value, ct);
    public Task SaveActionsAsync(ActionLibrary value, CancellationToken ct = default) => WriteAsync("actions.json", value, ct);
    private async Task<T> ReadAsync<T>(string name, CancellationToken ct) where T : new()
    {
        var path = Path.Combine(_root, name);
        if (!File.Exists(path)) return new T();
        await using var file = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(file, JsonOptions, ct) ?? throw new InvalidDataException($"{name} is empty.");
    }
    private async Task WriteAsync<T>(string name, T value, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        var path = Path.Combine(_root, name);
        var temp = path + ".tmp";
        try
        {
            await using (var file = File.Create(temp))
                await JsonSerializer.SerializeAsync(file, value, JsonOptions, ct);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); _writeLock.Release(); }
    }
}
