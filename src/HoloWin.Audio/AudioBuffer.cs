using HoloWin.Core;

namespace HoloWin.Audio;

/// <summary>
/// A captured multichannel audio buffer delivered by the audio capture engine.
/// The frames are interleaved per-channel: Channels[ch][frame] is a normalized
/// float in [-1, 1]. The Timestamp is monotonic and is what the fusion engine
/// uses to align observations across sensors.
/// </summary>
public sealed class AudioBuffer
{
    public required float[][] Channels { get; init; }
    public required double SampleRate { get; init; }
    public required MonotonicTimestamp Timestamp { get; init; }
    public required string SourceId { get; init; }
}

/// <summary>
/// A multichannel low-latency audio capture interface. Implementations MUST be
/// safe to call from a single dedicated capture thread. They must NOT block on
/// other sensors; if the underlying device stalls, the implementation must
/// surface that as a status change rather than freezing the audio thread.
/// </summary>
public interface IAudioCapture : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    bool IsRunning { get; }
    double SampleRate { get; }
    int ChannelCount { get; }
    event EventHandler<AudioBuffer>? BufferAvailable;
    event EventHandler<SensorStatus>? StatusChanged;
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

public enum AudioDevicePolicy
{
    BuiltInOnly,       // Built-in microphone mandatory
    AnyAvailable,      // HoloWin permissive mode
}
