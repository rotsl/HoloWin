using System.Threading;
using System.Threading.Channels;

namespace HoloWin.Core;

/// <summary>
/// A monotonically-increasing queue of events with explicit backpressure. Used
/// between the audio capture thread and the classifier thread, and between
/// any sensor thread and the fusion engine. Old events are discarded when the
/// queue exceeds its budget. No sensor may stall another by holding the queue.
/// </summary>
public sealed class BoundedAsyncQueue<T> : IDisposable where T : class
{
    private readonly Channel<T> _channel;
    private readonly int _capacity;
    private long _droppedCount;
    private long _enqueuedCount;
    private long _dequeuedCount;
    private bool _disposed;

    public BoundedAsyncQueue(int capacity = 64)
    {
        _capacity = Math.Max(1, capacity);
        var opts = new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        };
        _channel = Channel.CreateBounded<T>(opts, _ => Interlocked.Increment(ref _droppedCount));
    }

    public long DroppedCount => Interlocked.Read(ref _droppedCount);
    public long EnqueuedCount => Interlocked.Read(ref _enqueuedCount);
    public long DequeuedCount => Interlocked.Read(ref _dequeuedCount);

    public bool TryWrite(T item)
    {
        if (_disposed) return false;
        var ok = _channel.Writer.TryWrite(item);
        if (ok)
        {
            Interlocked.Increment(ref _enqueuedCount);
        }
        return ok;
    }

    public async ValueTask<T> ReadAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var item = await _channel.Reader.ReadAsync(ct).ConfigureAwait(false);
        Interlocked.Increment(ref _dequeuedCount);
        return item;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
    }
}
