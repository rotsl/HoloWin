using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using HoloWin.Core;
using HoloWin.Gestures;
using OpenCvSharp;

namespace HoloWin.Vision;

/// <summary>Owns camera open, read and release on one background thread.</summary>
[SupportedOSPlatform("windows")]
public sealed class OpenCvCameraCapture : ICameraCapture
{
    private readonly int _deviceIndex;
    private readonly double _fps;
    private readonly Channel<Frame> _frames = Channel.CreateBounded<Frame>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cts;
    private Task? _delivery;
    private volatile bool _running;
    private int _started, _disposed;
    private long _totalFrames;
    public OpenCvCameraCapture(int deviceIndex = 0, int width = 640, int height = 480, double fps = 30)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Camera capture requires Windows.");
        if (deviceIndex < 0 || width <= 0 || height <= 0 || fps <= 0) throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        _deviceIndex = deviceIndex; Width = width; Height = height; _fps = fps;
    }
    public string Id => $"opencv://camera/{_deviceIndex}";
    public string DisplayName => $"Camera {_deviceIndex}";
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsRunning => _running;
    public long TotalFrames => Interlocked.Read(ref _totalFrames);
    public event EventHandler<Frame>? FrameAvailable;
    public event EventHandler<SensorStatus>? StatusChanged;
    public async Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Create a new capture to restart the camera.");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        _delivery = Task.Run(() => DeliverAsync(token));
        var thread = new Thread(() => Capture(token)) { IsBackground = true, Name = "HoloWin camera" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        try { await _firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(8), ct); }
        catch (TimeoutException)
        {
            _cts.Cancel();
            throw new TimeoutException("Camera did not deliver its first frame within eight seconds. Check its index, privacy settings and other apps using it.");
        }
        catch { _cts.Cancel(); throw; }
    }
    private void Capture(CancellationToken ct)
    {
        try
        {
            // DirectShow avoids the cross-thread Media Foundation open/read path.
            using var camera = new VideoCapture();
            var backend = "DirectShow";
            if (!camera.Open(_deviceIndex, VideoCaptureAPIs.DSHOW))
            {
                ct.ThrowIfCancellationRequested();
                backend = "Media Foundation";
                camera.Open(_deviceIndex, VideoCaptureAPIs.MSMF);
            }
            if (!camera.IsOpened()) throw new InvalidOperationException($"Cannot open camera {_deviceIndex}. Check camera privacy, index and other apps using it.");
            try
            {
                camera.Set(VideoCaptureProperties.FrameWidth, Width);
                camera.Set(VideoCaptureProperties.FrameHeight, Height);
                camera.Set(VideoCaptureProperties.Fps, _fps);
            }
            catch { /* Use the camera defaults if format negotiation is unsupported. */ }
            var lastFrame = System.Diagnostics.Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                using var src = new Mat();
                if (!camera.Read(src) || src.Empty())
                {
                    if (lastFrame.Elapsed > TimeSpan.FromSeconds(3)) throw new IOException("Camera opened but delivered no frames. Check its shutter and restart capture.");
                    Thread.Sleep(10); continue;
                }
                ct.ThrowIfCancellationRequested();
                lastFrame.Restart();
                using var rgb = new Mat();
                Cv2.CvtColor(src, rgb, ColorConversionCodes.BGR2RGB);
                using var contiguous = rgb.Clone();
                Width = contiguous.Width; Height = contiguous.Height;
                var bytes = new byte[Width * Height * 3];
                Marshal.Copy(contiguous.Data, bytes, 0, bytes.Length);
                _frames.Writer.TryWrite(new Frame { Width = Width, Height = Height, Stride = Width * 3,
                    RgbPixels = bytes, Timestamp = MonotonicClock.Now, SourceId = Id });
                if (Interlocked.Increment(ref _totalFrames) == 1)
                {
                    _running = true;
                    StatusChanged?.Invoke(this, new SensorStatus(Id, true, $"{backend} receiving {Width} x {Height} frames."));
                    _firstFrame.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException) { _firstFrame.TrySetCanceled(ct); }
        catch (Exception ex)
        {
            _firstFrame.TrySetException(ex);
            StatusChanged?.Invoke(this, new SensorStatus(Id, false, ex.Message));
        }
        finally
        {
            _running = false; _frames.Writer.TryComplete(); _finished.TrySetResult();
            _firstFrame.TrySetCanceled();
            StatusChanged?.Invoke(this, new SensorStatus(Id, false, null));
        }
    }
    private async Task DeliverAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync(ct))
            {
                try { FrameAvailable?.Invoke(this, frame); }
                catch (Exception ex) { StatusChanged?.Invoke(this, new SensorStatus(Id, _running, "Preview delivery: " + ex.Message)); }
            }
        }
        catch (OperationCanceledException) { }
    }
    public async Task StopAsync()
    {
        _running = false; _cts?.Cancel();
        if (_started == 0) return;
        // A stuck native read must not block the UI or free a handle still in use.
        try { await _finished.Task.WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
        if (_delivery is not null) await _delivery;
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync();
        if (_finished.Task.IsCompleted || _started == 0) _cts?.Dispose();
        else _ = _finished.Task.ContinueWith(_ => _cts?.Dispose(), TaskScheduler.Default);
    }
}
