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
        var again = NationGen.CreateInitialState(w);   // the same stretch with the code already compiled: the real worst tick
        Simulation.Begin(w, again);
        Measure(w, again, "early game again (warm JIT)", (int)Clock.TicksFor(600));
        while (s.Tick < Clock.TicksFor(2 * 3600)) Simulation.Step(w, s, null);
        Measure(w, s, "settled (2 h in)", (int)Clock.TicksFor(600));
    }

    static void Measure(WorldData w, GameState s, string what, int ticks)
    {
        double worst = 0, worstWarm = 0;   // warm: after the first cycle (JIT and lazy caches)
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < ticks; k++)
        {
            long t0 = Stopwatch.GetTimestamp();
            Simulation.Step(w, s, null);
            double us = Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000;
            worst = Math.Max(worst, us);
            if (k >= Clock.CycleTicks) worstWarm = Math.Max(worstWarm, us);
        }
        Console.WriteLine($"bench {what}: {sw.Elapsed.TotalMilliseconds * 1000 / ticks:F1} µs/tick, worst tick {worst:F0} µs ({worstWarm:F0} after the first cycle), " +
                          $"{(GC.GetAllocatedBytesForCurrentThread() - a0) / ticks} B allocated per tick");
    }
}
