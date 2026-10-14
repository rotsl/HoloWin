using System.Collections.Concurrent;
using System.Reflection;
using HoloWin.Core;
using HoloWin.Actions;
using HoloWin.Sensors;

namespace HoloWin.Plugins;

public interface IHoloPlugin
{
    string Id { get; }
    string DisplayName { get; }
    Version? RequiredHostVersion { get; }
    bool IsSupported { get; }
    void Initialize(IServiceProvider services);
}

public sealed class PluginManifest
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string EntryTypeName { get; init; }
    public Version? RequiredHostVersion { get; init; }
    public bool IsSupported { get; init; } = true;
    public string? NotSupportedReason { get; init; }
    public bool Experimental { get; init; }
}

public sealed class PluginRegistry
{
    private readonly object _lock = new();
    private readonly List<PluginManifest> _manifests = new();
    private readonly ConcurrentDictionary<string, IHoloPlugin> _active = new();

    public IReadOnlyList<PluginManifest> AvailableManifests
    {
        get { lock (_lock) return _manifests.ToList(); }
    }

    public void Register(PluginManifest manifest)
    {
        lock (_lock)
        {
            if (_manifests.Any(m => m.Id == manifest.Id))
                throw new InvalidOperationException($"Plugin {manifest.Id} already registered.");
            _manifests.Add(manifest);
        }
    }

    public IHoloPlugin? Activate(PluginManifest manifest, IServiceProvider services)
    {
        if (_active.TryGetValue(manifest.Id, out var existing)) return existing;
        if (!manifest.IsSupported) return null;
        // Discovery: this reference implementation does not dynamically load
        // plugin assemblies — that requires an explicit trust model. Plugins
        // are expected to be compiled into the host directly. The registry
        // surface is here so future work can wire in a managed add-in model.
        return null;
    }
}

public sealed class DefaultPluginCatalog
{
    public static IReadOnlyList<PluginManifest> BuiltInManifests => new[]
    {
        new PluginManifest
        {
            Id = "holowin.builtin.wasapi",
            DisplayName = "WASAPI Audio Capture",
            EntryTypeName = "HoloWin.Audio.WasapiAudioCapture",
            IsSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621),
            NotSupportedReason = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)
                ? null : "WASAPI capture requires Windows 11 22H2 or later."
        },
        new PluginManifest
        {
            Id = "holowin.builtin.opencv",
            DisplayName = "OpenCV Camera Capture",
            EntryTypeName = "HoloWin.Vision.OpenCvCameraCapture",
            IsSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621),
            NotSupportedReason = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)
                ? null : "OpenCV camera capture requires Windows 11 22H2 or later."
        },
        new PluginManifest
        {
            Id = "holowin.builtin.onnxhand",
            DisplayName = "ONNX Hand Landmark Tracker",
            EntryTypeName = "HoloWin.Vision.OnnxHandTracker",
            IsSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621),
            NotSupportedReason = "ONNX Runtime requires Windows 11 22H2 and a hand_landmark.onnx model."
        },
        new PluginManifest
        {
            Id = "holowin.builtin.sendinput",
            DisplayName = "SendInput Input Controller",
            EntryTypeName = "HoloWin.Actions.InputController",
            IsSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621),
            NotSupportedReason = "SendInput requires Windows 11 22H2 or later.",
            Experimental = true
        },
    };
}
