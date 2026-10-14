using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class WaveFileWriterTests
{
    [Fact]
    public void WriteFrame_ThenDispose_ProducesReadableFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var w = new WaveFileWriter(path, channels: 1, sampleRate: 16000))
            {
                var frames = new float[1][];
                frames[0] = new float[100];
                for (int i = 0; i < 100; i++) frames[0][i] = 0.5f;
                w.WriteFrame(frames, 100);
            }
            var info = new FileInfo(path);
            Assert.True(info.Length > 44, "WAV file should contain at least header + samples.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Constructor_CreatesValidHeader()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var w = new WaveFileWriter(path, channels: 2, sampleRate: 48000)) { /* no writes */ }
            var bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'R', bytes[0]);
            Assert.Equal((byte)'I', bytes[1]);
            Assert.Equal((byte)'F', bytes[2]);
            Assert.Equal((byte)'F', bytes[3]);
            Assert.Equal((byte)'W', bytes[8]);
            Assert.Equal((byte)'A', bytes[9]);
            Assert.Equal((byte)'V', bytes[10]);
            Assert.Equal((byte)'E', bytes[11]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
