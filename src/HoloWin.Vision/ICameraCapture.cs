using HoloWin.Core;
using HoloWin.Gestures;

namespace HoloWin.Vision;

/// <summary>
/// Camera capture contract. Mirrors <c>IAudioCapture</c> in <c>HoloWin.Audio</c>
/// but for vision: implementations deliver frames on a dedicated capture thread
/// and must not block the caller's thread during capture. If the consumer can't
/// keep up, the implementation drops the oldest pending frame rather than
/// back-pressuring the camera thread (camera frames are time-sensitive: a
/// stale frame is useless to the fusion engine).
/// </summary>
public interface ICameraCapture : IAsyncDisposable
{
    /// <summary>Stable device identifier.</summary>
    string Id { get; }

    /// <summary>Human-readable device name suitable for UI display.</summary>
    string DisplayName { get; }

    /// <summary>True while the device is actively capturing and delivering frames.</summary>
    bool IsRunning { get; }

    /// <summary>Frame width in pixels.</summary>
    int Width { get; }

    /// <summary>Frame height in pixels.</summary>
    int Height { get; }

    /// <summary>
    /// Raised on the capture thread whenever a new frame is decoded and ready
    /// for downstream processing. Handlers must not block — long-running work
    /// should be offloaded to a separate task to avoid frame drops.
    /// </summary>
    event EventHandler<Frame>? FrameAvailable;

    /// <summary>
    /// Raised when the device enters or leaves a running state, or when an
    /// error is detected (with a non-null <see cref="SensorStatus.Detail"/>).
    /// </summary>
    event EventHandler<SensorStatus>? StatusChanged;

    /// <summary>
    /// Opens the device without blocking the caller's thread and waits for its
    /// first frame. Frames are delivered via <see cref="FrameAvailable"/>.
    /// </summary>
    /// <param name="ct">Cancellation token for aborting the open handshake.</param>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stops the capture loop and releases the device handle. Safe to call
    /// when not running; idempotent.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Releases all native resources owned by this capture. Idempotent;
    /// safe to call multiple times.
    /// </summary>
    new ValueTask DisposeAsync();
}
