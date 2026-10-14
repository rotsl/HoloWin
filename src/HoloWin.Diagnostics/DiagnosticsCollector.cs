using System.Collections.Concurrent;
using HoloWin.Core;

namespace HoloWin.Diagnostics;

public sealed class DiagnosticsCollector
{
    public sealed record ZoneEventLogEntry(
        DateTimeOffset TimestampUtc,
        DeskZone? Zone,
        double Confidence,
        RejectionReason? RejectionReason,
        double ProcessingLatencyMs,
        double SignalStrength,
        string Source);

    public sealed record PerformanceTrace(
        DateTimeOffset AtUtc,
        string Operation,
        double ElapsedMs,
        string? Detail);

    private readonly object _lock = new();
    private readonly Queue<ZoneEventLogEntry> _recentEvents = new();
    private readonly Queue<PerformanceTrace> _traces = new();
    private readonly int _eventCapacity;
    private readonly int _traceCapacity;
    private long _eventCount;
    private long _traceCount;

    public DiagnosticsCollector(int eventCapacity = 500, int traceCapacity = 500)
    {
        _eventCapacity = eventCapacity;
        _traceCapacity = traceCapacity;
    }

    public long EventCount  => Interlocked.Read(ref _eventCount);
    public long TraceCount  => Interlocked.Read(ref _traceCount);

    public void LogZoneEvent(ZoneEventLogEntry entry)
    {
        lock (_lock)
        {
            _recentEvents.Enqueue(entry);
            if (_recentEvents.Count > _eventCapacity) _recentEvents.Dequeue();
            Interlocked.Increment(ref _eventCount);
        }
    }

    public void Trace(PerformanceTrace trace)
    {
        lock (_lock)
        {
            _traces.Enqueue(trace);
            if (_traces.Count > _traceCapacity) _traces.Dequeue();
            Interlocked.Increment(ref _traceCount);
        }
    }

    public IReadOnlyList<ZoneEventLogEntry> SnapshotEvents()
    {
        lock (_lock) { return _recentEvents.ToArray(); }
    }

    public IReadOnlyList<PerformanceTrace> SnapshotTraces()
    {
        lock (_lock) { return _traces.ToArray(); }
    }
}
