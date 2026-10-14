using HoloWin.Core;
using HoloWin.Audio;
using HoloWin.SensorFusion;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var duration = TimeSpan.FromSeconds(60);
        foreach (var arg in args)
        {
            if (arg.StartsWith("--duration=") && int.TryParse(arg.Substring(11), out var secs))
                duration = TimeSpan.FromSeconds(secs);
        }
        Console.WriteLine($"HoloWin PerformanceBenchmark: running {duration.TotalSeconds:F0}s synthetic stress");
        var eng = new FusionEngine { CurrentMode = FusionMode.AudioOnly };
        var fired = 0;
        eng.DecisionAvailable += (_, _) => Interlocked.Increment(ref fired);
        await eng.StartAsync(default);

        var rng = new Random(7);
        var deadline = DateTime.UtcNow + duration;
        long pushed = 0;
        while (DateTime.UtcNow < deadline)
        {
            var zone = (DeskZone)rng.Next(4);
            eng.Push(new AcousticImpactObservation(
                MonotonicClock.Now, "synthetic-mic", 0.85,
                zone, 0.9,
                new[] { 0.2, 0.3, 0.4, 0.5 }, null));
            pushed++;
            if (pushed % 1000 == 0)
                Thread.Sleep(1);
        }
        await eng.StopAsync();
        Console.WriteLine($"Pushed {pushed} observations; engine emitted {fired} fused decisions.");
        return 0;
    }
}
