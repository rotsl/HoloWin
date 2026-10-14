using HoloWin.Core;
using HoloWin.Audio;
using Xunit;

namespace HoloWin.Audio.Tests;

public class AcousticPipelineTests
{
    private sealed class FakeAudioCapture : IAudioCapture
    {
        public string Id { get; set; } = "fake";
        public string DisplayName { get; set; } = "Fake";
        public double SampleRate { get; set; } = 16000;
        public int ChannelCount { get; set; } = 1;
        public bool IsRunning { get; private set; }
        public event EventHandler<AudioBuffer>? BufferAvailable;
        public event EventHandler<SensorStatus>? StatusChanged { add { } remove { } }
        public Task StartAsync(CancellationToken ct) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) { IsRunning = false; return Task.CompletedTask; }
        public void Emit(float[][] channels, MonotonicTimestamp ts)
        {
            BufferAvailable?.Invoke(this, new AudioBuffer
            {
                Channels = channels,
                SampleRate = SampleRate,
                Timestamp = ts,
                SourceId = Id
            });
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Pipeline_WithNoClassifier_RejectsAsPaused()
    {
        var cap = new FakeAudioCapture();
        await using var pipe = new AcousticPipeline(cap, SensingStrategy.Passive);
        await pipe.StartAsync(CancellationToken.None);
        // Emit a brief quiet buffer to advance warmup but no impulse.
        var buf = new float[1][];
        buf[0] = new float[1024];
        for (int i = 0; i < 1024; i++) buf[0][i] = 0.0001f;
        for (int i = 0; i < 50; i++) cap.Emit(buf, MonotonicClock.Now);
        await Task.Delay(50);
        // No events should be accepted without a classifier.
        Assert.Equal(0, pipe.TotalAccepted);
        await pipe.StopAsync();
    }

    [Fact]
    public async Task Pipeline_StartStop_IsIdempotent()
    {
        var cap = new FakeAudioCapture();
        await using var pipe = new AcousticPipeline(cap, SensingStrategy.Passive);
        await pipe.StartAsync(CancellationToken.None);
        await pipe.StartAsync(CancellationToken.None);
        Assert.True(pipe.IsRunning);
        await pipe.StopAsync();
        await pipe.StopAsync();
        Assert.False(pipe.IsRunning);
    }
}
