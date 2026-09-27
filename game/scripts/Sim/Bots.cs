using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>
/// Bots of the peaceful MVP «просто развиваются»: every few years each AI nation persuades one neighbouring tribe
/// to join (the same scoring as the initial borders in the mockup's genNations). Bigger states expand more slowly.
/// </summary>
public static class Bots
{
    /// <summary>Mean years between two claims of a nation with k provinces.</summary>
    public static double Interval(int provinces) => 9 + provinces / 3.0;

    public static void Expand(WorldData w, GameState s, ISimSink sink, ref List<int> changed)
    {
        int nN = s.NationCapital.Length;
        Span<int> count = stackalloc int[nN];
        for (int p = 0; p < w.P; p++) { int o = s.Owner[p]; if (o >= 0 && o < nN) count[o]++; }

        for (int n = 0; n < nN; n++)
        {
            if (n == GameState.LocalPlayer || count[n] == 0) continue;
            if (SimRng.U(w.Seed, 21, n, s.Year) >= 1 / Interval(count[n])) continue;
            int q = BestClaim(w, s, n);
            if (q < 0) continue;
            Rules.Claim(w, s, q, n);
            (changed ??= new List<int>()).Add(q);
            if (Rules.Borders(w, s, q, GameState.LocalPlayer) && sink != null)
                sink.Notify("flag", $"Провинция {w.PName[q]} у наших границ вошла в состав {Ru.Genitive(Data.Nations[n].Name)}");
        }
    }

    /// <summary>The unowned land province next to nation n that it wants most, or -1.</summary>
    public static int BestClaim(WorldData w, GameState s, int n)
    {
        int cap = s.NationCapital[n], best = -1; double bs = double.MinValue;
        if (cap < 0) return -1;
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n) continue;
            foreach (int q in w.Adj[p])
            {
                if (s.Owner[q] >= 0 || w.PLand[q] != 1) continue;
                int nb = 0;
                foreach (int r in w.Adj[q]) if (s.Owner[r] == n) nb++;
                double sc = w.PFert[q] * .6 + SimRng.U(w.Seed, 22, q, n) * .2 + nb * .35
                          - Simulation.Distance(w, q, cap) / 55 + (w.PSize[q] > 30 ? .1 : 0);
                if (sc > bs || (sc == bs && q < best)) { bs = sc; best = q; }
            }
        }
        return best;
    }
}
