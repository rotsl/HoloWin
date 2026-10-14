using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class BoundedAsyncQueueTests
{
    [Fact]
    public async Task TryWrite_DropsOldest_WhenCapacityExceeded()
    {
        var q = new BoundedAsyncQueue<string>(2);
        q.TryWrite("a");
        q.TryWrite("b");
        // Wait briefly so the consumer can pull "a" and let "c" in;
        // we test DropOldest behavior separately.
        var cts = new CancellationTokenSource(200);
        var first = await q.ReadAsync(cts.Token);
        Assert.Equal("a", first);
        q.TryWrite("c");
        Assert.Equal("b", await q.ReadAsync(cts.Token));
        Assert.Equal("c", await q.ReadAsync(cts.Token));
    }

    [Fact]
    public async Task TryWrite_DropOldestWhenFull()
    {
        var q = new BoundedAsyncQueue<string>(2);
        q.TryWrite("a");
        q.TryWrite("b");
        q.TryWrite("c"); // should drop "a"
        var cts = new CancellationTokenSource(500);
        Assert.Equal("b", await q.ReadAsync(cts.Token));
        Assert.Equal("c", await q.ReadAsync(cts.Token));
    }
}

public class MonotonicClockTests
{
    [Fact]
    public void Subtract_UsesMonotonicElapsedTimeDespiteWallClockChanges()
    {
        var earlier = new MonotonicTimestamp(100, DateTimeOffset.UnixEpoch.AddHours(1));
        var later = new MonotonicTimestamp(1350, DateTimeOffset.UnixEpoch);

        Assert.Equal(TimeSpan.FromTicks(12500), later - earlier);
        Assert.Equal(TimeSpan.FromTicks(-12500), earlier - later);
        Assert.Equal(TimeSpan.Zero, earlier - earlier);
    }

    [Fact]
    public void Now_NeverDecreases()
    {
        var t1 = MonotonicClock.Now.Microseconds;
        Thread.Sleep(1);
        var t2 = MonotonicClock.Now.Microseconds;
        Assert.True(t2 >= t1);
    }

    [Fact]
    public void IsStale_TrueForVeryOldTimestamp()
    {
        var ancient = new MonotonicTimestamp(-10_000_000, DateTimeOffset.UtcNow);
        Assert.True(MonotonicClock.IsStale(ancient, 1000));
    }
}
