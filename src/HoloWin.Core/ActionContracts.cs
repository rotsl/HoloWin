using System.Collections.ObjectModel;
using System.Text.Json;

namespace HoloWin.Core;

public enum ActionType
{
    VisualOnly,
    PlaySound,
    CopyText,
    TypeText,
    SpeakText,
    OpenApplication,
    FocusApplication,
    OpenFileOrFolder,
    OpenWebsite,
    TriggerKeyboardShortcut,
    SendMediaKey,
    MuteAudio,
    SetVolume,
    TakeScreenshot,
    SwitchVirtualDesktop,
    RunPowerShell,
    RunExecutable,
    InvokeHttpEndpoint,
    RunMacro,
    SmartHome,
    FunctionKey,
    SwitchTab,
    IncreaseVolume,
    DecreaseVolume,
    IncreaseBrightness,
    DecreaseBrightness
}

public sealed class HoloAction
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? DisplayName { get; set; }
    public ActionType Type { get; set; } = ActionType.VisualOnly;
    public IReadOnlyDictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool Enabled { get; set; } = true;
    public int CooldownMilliseconds { get; set; } = 200;
    public double ConfidenceThreshold { get; set; } = ClassifierDefaults.MinimumConfidence;
    public bool RequireConfirmation { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 5;
    public bool RequiresApproval { get; set; } = false;
    public string? Notes { get; set; }
}

public sealed class HoloMacro
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? DisplayName { get; set; }
    public List<MacroStep> Steps { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public int CooldownMilliseconds { get; set; } = 500;
}

public sealed class MacroStep
{
    public int Order { get; set; }
    public required string ActionId { get; init; }
    public int DelayMilliseconds { get; set; } = 0;
}

public sealed record ActionResult(
    string ActionId,
    bool Succeeded,
    string? Error,
    long ElapsedMicros,
    DateTimeOffset AtUtc);

public interface IActionDispatcher
{
    /// <summary>
    /// Synchronously execute an action. Must be safe to call from any thread;
    /// actions are dispatched on a worker.
    /// </summary>
    Task<ActionResult> ExecuteAsync(HoloAction action, CancellationToken ct);

    /// <summary>
    /// Whether an action requires explicit user approval before it can ever be
    /// auto-dispatched. Shell commands, scripts, executables, and HTTP endpoints
    /// default to requiring approval.
    /// </summary>
    bool RequiresApproval(HoloAction action);
}

public static class ActionParameterKeys
{
    public const string Text       = "text";
    public const string Url        = "url";
    public const string Path       = "path";
    public const string AppPath    = "app_path";
    public const string Script     = "script";
    public const string Executable = "executable";
    public const string Arguments  = "arguments";
    public const string Endpoint   = "endpoint";
    public const string Method     = "method";
    public const string Body       = "body";
    public const string Shortcut   = "shortcut";
    public const string MediaKey   = "media_key";
    public const string VolumePct  = "volume_percent";
    public const string SoundName  = "sound_name";
    public const string VoiceName = "voice_name";
    public const string Rate       = "rate";
}
