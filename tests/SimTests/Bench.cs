using System;
using System.Diagnostics;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Tests;

/// <summary>
/// «bench» argument: the rules alone, 16 nations — early game (first 10 minutes at speed 3: bots claiming, fog
/// refreshes) and settled game (after 2 hours) — µs per tick, worst cycle and bytes allocated per tick.
/// </summary>
public static class Bench
{
    public static void Run(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        Simulation.Begin(w, s);
        Measure(w, s, "early game (0–10 min)", (int)Clock.TicksFor(600));
        while (s.Tick < Clock.TicksFor(2 * 3600)) Simulation.Step(w, s, null);
        Measure(w, s, "settled (2 h in)", (int)Clock.TicksFor(600));
    }

    static void Measure(WorldData w, GameState s, string what, int ticks)
    {
        double worst = 0;
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < ticks; k++)
        {
            long t0 = Stopwatch.GetTimestamp();
            Simulation.Step(w, s, null);
            worst = Math.Max(worst, Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000);
        }
        Console.WriteLine($"bench {what}: {sw.Elapsed.TotalMilliseconds * 1000 / ticks:F1} µs/tick, worst tick {worst:F0} µs, " +
                          $"{(GC.GetAllocatedBytesForCurrentThread() - a0) / ticks} B allocated per tick");
    }
}
