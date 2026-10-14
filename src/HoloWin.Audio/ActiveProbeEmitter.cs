using System.Runtime.Versioning;
using HoloWin.Core;
using NAudio.Wave;

namespace HoloWin.Audio;

/// <summary>
/// Emits the active ultrasonic chirp through the system's default output. The
/// active probe is OPT-IN: it must be enabled explicitly by the user and stops
/// immediately if the device is lost. The probe is never assumed to improve
/// accuracy. Hardware may filter it out or reproduce it audibly.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ActiveProbeEmitter : IAsyncDisposable
{
    private readonly object _lock = new();
    private WaveOutEvent? _output;
    private ChirpWaveProvider? _provider;
    private bool _enabled;
    private bool _running;
    private double _amplitude = ActiveProbe.DefaultAmplitude;

    public double Amplitude
    {
        get { lock (_lock) return _amplitude; }
        set
        {
            value = Math.Clamp(value, 0, 0.10);
            lock (_lock)
            {
                _amplitude = value;
                if (_provider is not null) _provider.Amplitude = value;
            }
        }
    }

    public bool IsEnabled { get => _enabled; set { _enabled = value; if (!value) Stop(); } }

    public void Start(double sampleRate = 48000)
    {
        if (!_enabled) return;
        lock (_lock)
        {
            if (_running) return;
            _provider = new ChirpWaveProvider(sampleRate, ActiveProbe.DefaultStartFrequency,
                                              ActiveProbe.DefaultEndFrequency, ActiveProbe.DefaultDuration,
                                              _amplitude);
            _output = new WaveOutEvent { DeviceNumber = -1, DesiredLatency = 50 };
            _output.Init(_provider);
            _output.Play();
            _running = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
            try { _output?.Stop(); } catch { /* ignore */ }
            try { _output?.Dispose(); } catch { /* ignore */ }
            _output = null;
            _provider = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await ValueTask.CompletedTask;
    }

    private sealed class ChirpWaveProvider : ISampleProvider
    {
        private readonly double _sampleRate;
        private readonly float[] _chirp;
        private int _pos;
        public WaveFormat WaveFormat { get; }
        public double Amplitude { get; set; }

        public ChirpWaveProvider(double sampleRate, double startHz, double endHz, double duration, double amplitude)
        {
            _sampleRate = sampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)sampleRate, 1);
            _chirp = ActiveProbe.Chirp(sampleRate, duration, startHz, endHz, 1.0);
            Amplitude = amplitude;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var amp = (float)Amplitude;
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = _chirp[_pos] * amp;
                _pos = (_pos + 1) % _chirp.Length;
            }
            return count;
        }
    }
}
