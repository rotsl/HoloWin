using System.Runtime.Versioning;
using HoloWin.Core;
using NAudio.CoreAudioApi;
using DeviceState = HoloWin.Core.DeviceState;
using NAudio.Wave;

namespace HoloWin.Audio;

/// <summary>
/// A real WasapiCapture-based audio input implementation. Uses NAudio as the
/// managed wrapper over the WASAPI COM surface. Captures 32-bit IEEE float
/// samples at the device's native sample rate. Lists devices via MMDeviceEnumerator.
///
/// This implementation is Windows-only. On non-Windows hosts, the constructor
/// throws PlatformNotSupportedException; consumers should resolve
/// <see cref="IAudioCapture"/> only after the capability manager has reported
/// the platform as supported.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioCapture : IAudioCapture
{
    private readonly WasapiCapture _capture;
    private readonly WaveFormat _format;
    private readonly object _lock = new();
    private bool _running;
    private long _totalFrames;
    private long _droppedFrames;
    private int _channelCount;

    public WasapiAudioCapture(string? deviceId = null, bool useEventSync = true, AudioClientShareMode shareMode = AudioClientShareMode.Shared)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        if (!string.IsNullOrEmpty(deviceId))
            device = enumerator.GetDevice(deviceId);
        else
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        Id = device.ID;
        DisplayName = device.FriendlyName;
        _capture = new WasapiCapture(device, useEventSync) { ShareMode = shareMode };
        _format = _capture.WaveFormat;
        _channelCount = _format.Channels;
        SampleRate = _format.SampleRate;
        ChannelCount = _channelCount;
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public double SampleRate { get; }
    public int ChannelCount { get; }
    public bool IsRunning => _running;
    public long TotalFrames => Interlocked.Read(ref _totalFrames);
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

    public event EventHandler<AudioBuffer>? BufferAvailable;
    public event EventHandler<SensorStatus>? StatusChanged;

    public Task StartAsync(CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _running = true;
        _capture.StartRecording();
        StatusChanged?.Invoke(this, new SensorStatus(Id, true, null));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        if (!_running) return Task.CompletedTask;
        _running = false;
        _capture.StopRecording();
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running) return;
        // NAudio WasapiCapture may deliver 16-bit or 32-bit float depending on the
        // device's mix format. We normalize to float[][] mono or per-channel arrays.
        try
        {
            var frames = e.BytesRecorded / (_format.BlockAlign > 0 ? _format.BlockAlign : 1);
            Interlocked.Add(ref _totalFrames, frames);
            float[][] channels;
            if (_format.Encoding == WaveFormatEncoding.IeeeFloat && _format.BitsPerSample == 32)
            {
                channels = ExtractChannelsIeeeFloat(e.Buffer, frames, _channelCount);
            }
            else
            {
                // Fall back to standard WaveFormat conversion via a WaveToSampleProvider chain.
                var bufferDuration = TimeSpan.FromMilliseconds(Math.Ceiling(e.BytesRecorded * 1000.0 / _format.AverageBytesPerSecond));
                var provider = new BufferedWaveProvider(_format, bufferDuration) { DiscardOnBufferOverflow = false };
                provider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                var sampleProvider = provider.ToSampleProvider();
                var floatBuffer = new float[frames * _channelCount];
                sampleProvider.Read(floatBuffer, 0, floatBuffer.Length);
                channels = Deinterleave(floatBuffer, frames, _channelCount);
            }
            var ts = MonotonicClock.Now;
            BufferAvailable?.Invoke(this, new AudioBuffer
            {
                Channels = channels,
                SampleRate = SampleRate,
                Timestamp = ts,
                SourceId = Id
            });
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _droppedFrames);
            StatusChanged?.Invoke(this, new SensorStatus(Id, false, ex.Message));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _running = false;
        if (e.Exception is not null)
            StatusChanged?.Invoke(this, new SensorStatus(Id, false, e.Exception.Message));
        else
            StatusChanged?.Invoke(this, new SensorStatus(Id, false, null));
    }

    private static float[][] ExtractChannelsIeeeFloat(byte[] buffer, int frames, int channels)
    {
        var result = new float[channels][];
        for (int c = 0; c < channels; c++) result[c] = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            int baseByte = i * channels * 4;
            for (int c = 0; c < channels; c++)
            {
                result[c][i] = BitConverter.ToSingle(buffer, baseByte + c * 4);
            }
        }
        return result;
    }

    private static float[][] Deinterleave(float[] interleaved, int frames, int channels)
    {
        var result = new float[channels][];
        for (int c = 0; c < channels; c++) result[c] = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            int idx = i * channels;
            for (int c = 0; c < channels; c++)
                result[c][i] = interleaved[idx + c];
        }
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
    }
}

public static class AudioCaptureFactory
{
    public static bool IsPlatformSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    public static IReadOnlyList<DeviceCapability> EnumerateInputDevices()
    {
        var result = new List<DeviceCapability>();
        if (!IsPlatformSupported) return result;
        try
        {
            var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var defaultId = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications)?.ID;
            foreach (var device in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
            {
                result.Add(new DeviceCapability
                {
                    Id = device.ID,
                    Class = DeviceClass.AudioInput,
                    FriendlyName = device.FriendlyName,
                    State = DeviceState.Available,
                    IsDefault = string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception ex)
        {
            result.Add(new DeviceCapability
            {
                Id = "error",
                Class = DeviceClass.AudioInput,
                FriendlyName = "Enumeration error",
                State = DeviceState.Error,
                StateDetail = ex.Message
            });
        }
        return result;
    }
}
