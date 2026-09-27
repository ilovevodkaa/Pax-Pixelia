using System.Collections.Generic;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>
/// Bots of the peaceful MVP «просто развиваются»: every few cycles each bot nation persuades one neighbouring tribe to
/// join (the same scoring as the initial borders in the mockup's genNations). Bigger states expand more slowly.
/// A bot issues the very command a player would (<see cref="Cmd.Claim"/> through <see cref="Commands.Apply"/>),
/// so it pays the same price and passes the same checks; its commands are not journaled — they follow from the state.
/// </summary>
public static class Bots
{
    /// <summary>A nation with k provinces tries once in 9 + k/3 cycles on average: chance 3 / (27 + k).</summary>
    public static bool WantsToClaim(int seed, int nation, int cycle, int provinces) => SimRng.Chance(seed, 21, nation, cycle, 3, 27 + provinces);

    internal static void Act(WorldData w, GameState s, int cycle, SimScratch tally, ISimSink sink, ref List<int> changed)
    {
        var count = tally.Provinces;
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            if (nat.Control != NationControl.Bot || count[n] == 0) continue;
            if (!WantsToClaim(w.Seed, n, cycle, count[n])) continue;
            if (nat.Treasury < Rules.ClaimCost * Rules.Cents) continue;
            int q = BestClaim(w, s, n);
            if (q < 0) continue;
            changed ??= new List<int>();
            if (Commands.Apply(w, s, Cmd.Claim(n, q), sink, changed) != 0) continue;
            count[n]++;
            if (sink == null) continue;
            for (int h = 0; h < s.Nat.Length; h++)
                if (s.Nat[h].Human && Rules.Borders(w, s, q, h))
                {
                    sink.Notify("flag", $"Провинция {w.PName[q]} у наших границ вошла в состав {Ru.Genitive(s.Nations[n].Name)}");
                    break;
                }
        }
    }

    /// <summary>The unowned land province next to nation n that it wants most, or -1. Score in 1/10000:
    /// fertility ×0.6, a salted roll ×0.2, own neighbours ×0.35, minus distance to the capital / 55 px, big land +0.1.</summary>
    public static int BestClaim(WorldData w, GameState s, int n)
    {
        int cap = s.NationCapital[n], best = -1;
        long bs = long.MinValue;
        if (cap < 0) return -1;
        var fert = WorldFacts.Of(w).FertPm;
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n) continue;
            foreach (int q in w.Adj[p])
            {
                if (s.Owner[q] >= 0 || w.PLand[q] != 1) continue;
                int nb = 0;
                foreach (int r in w.Adj[q]) if (s.Owner[r] == n) nb++;
                long sc = fert[q] * 6L + SimRng.Permille(w.Seed, 22, q, n) * 2L + nb * 3500L
                        - Simulation.Distance(w, q, cap) * 10_000L / 55 + (w.PSize[q] > 30 ? 1000 : 0);
                if (sc > bs || (sc == bs && q < best)) { bs = sc; best = q; }
            }
        }
        return best;
    }
}
