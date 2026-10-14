using System.IO;

namespace HoloWin.Core;

/// <summary>
/// Minimal 32-bit IEEE float WAV writer used by HoloWin's opt-in debug
/// capture only. Each windowed tap candidate is saved locally as a .wav
/// when "Retain 90ms debug recordings" is enabled, and is deleted by the
/// user from the Diagnostics page.
/// </summary>
public sealed class WaveFileWriter : IDisposable
{
    private readonly FileStream _fs;
    private readonly BinaryWriter _w;
    private readonly int _channels;
    private readonly double _sampleRate;
    private long _dataBytesWritten;
    private bool _disposed;

    public WaveFileWriter(string path, int channels, double sampleRate, int bitsPerSample = 32)
    {
        _fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        _w = new BinaryWriter(_fs);
        _channels = channels;
        _sampleRate = sampleRate;
        WriteHeader(bitsPerSample, dataSizePlaceholder: 0);
    }

    public void WriteFrame(float[][] channels, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            for (int c = 0; c < _channels; c++)
            {
                _w.Write(channels[c][i]);
                _dataBytesWritten += 4;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _fs.Seek(4, SeekOrigin.Begin);
            _w.Write((int)(_dataBytesWritten + 36));
            _fs.Seek(40, SeekOrigin.Begin);
            _w.Write((int)_dataBytesWritten);
        }
        catch { /* ignore */ }
        _w.Dispose();
        _fs.Dispose();
    }

    private void WriteHeader(int bitsPerSample, int dataSizePlaceholder)
    {
        _w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        _w.Write(0);  // placeholder for file size - 8
        _w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        _w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        _w.Write(16);  // chunk size
        _w.Write((short)3);  // IEEE float
        _w.Write((short)_channels);
        _w.Write((int)_sampleRate);
        _w.Write((int)(_sampleRate * _channels * bitsPerSample / 8));
        _w.Write((short)(_channels * bitsPerSample / 8));
        _w.Write((short)bitsPerSample);
        _w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        _w.Write(dataSizePlaceholder);
    }
}
