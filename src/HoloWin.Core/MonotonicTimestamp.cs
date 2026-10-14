using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoloWin.Core;

/// <summary>
/// Monotonic high-resolution clock used to timestamp observations across all
/// sensors so the fusion engine can correlate them. The clock is independent of
/// wall-clock time (it does not jump on NTP corrections or user changes) and
/// its origin is process start. Use <see cref="Microseconds"/> for fusion;
/// <see cref="WallClockUtc"/> is for human-readable logs only.
/// </summary>
public readonly struct MonotonicTimestamp : IEquatable<MonotonicTimestamp>, IComparable<MonotonicTimestamp>
{
    public long Microseconds { get; }
    public DateTimeOffset WallClockUtc { get; }

    public MonotonicTimestamp(long microseconds, DateTimeOffset wallClockUtc)
    {
        Microseconds = microseconds;
        WallClockUtc = wallClockUtc;
    }

    public double Milliseconds => Microseconds / 1000.0;
    public double Seconds => Microseconds / 1_000_000.0;

    public static MonotonicTimestamp FromUtcNow(long microseconds) =>
        new(microseconds, DateTimeOffset.UtcNow);

    public int CompareTo(MonotonicTimestamp other) => Microseconds.CompareTo(other.Microseconds);
    public bool Equals(MonotonicTimestamp other) => Microseconds == other.Microseconds;
    public override bool Equals(object? obj) => obj is MonotonicTimestamp t && Equals(t);
    public override int GetHashCode() => Microseconds.GetHashCode();
    public static bool operator ==(MonotonicTimestamp a, MonotonicTimestamp b) => a.Microseconds == b.Microseconds;
    public static bool operator !=(MonotonicTimestamp a, MonotonicTimestamp b) => a.Microseconds != b.Microseconds;
    public static bool operator <(MonotonicTimestamp a, MonotonicTimestamp b)  => a.Microseconds < b.Microseconds;
    public static bool operator >(MonotonicTimestamp a, MonotonicTimestamp b)  => a.Microseconds > b.Microseconds;
    public static TimeSpan operator -(MonotonicTimestamp a, MonotonicTimestamp b) =>
        TimeSpan.FromTicks((a.Microseconds - b.Microseconds) * 10);

    public override string ToString() =>
        $"{Microseconds / 1000.0:0.000} ms ({WallClockUtc:O})";
}

public static class MonotonicClock
{
    private static readonly long BootMicros = 0;
    private static readonly DateTimeOffset BootWall = DateTimeOffset.UtcNow;
    private static long _startTicks = 0;
    private static readonly double TicksPerMicrosecond = Stopwatch.Frequency / 1_000_000.0;

    static MonotonicClock()
    {
        _startTicks = Stopwatch.GetTimestamp();
    }

    public static MonotonicTimestamp Now
    {
        get
        {
            var elapsedMicros = (long)((Stopwatch.GetTimestamp() - _startTicks) / TicksPerMicrosecond);
            return new MonotonicTimestamp(BootMicros + elapsedMicros, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Returns true if a candidate timestamp is stale: more than
    /// <paramref name="maxAgeMicros"/> older than the current clock.
    /// </summary>
    public static bool IsStale(MonotonicTimestamp t, long maxAgeMicros) =>
        (Now.Microseconds - t.Microseconds) > maxAgeMicros;
}
