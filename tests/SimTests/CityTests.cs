using System.Collections.Generic;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>Cities: every owned province has a city, borders grow inside the sphere up to the cap, gold cannot beat the
/// cap, founding a town frees room and runs through the command journal, bots found towns, all deterministic.</summary>
public static class CityTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("cities: assignment");
        var s = Fresh(w);
        Check(Enumerable.Range(0, w.P).Where(p => s.Owner[p] >= 0).All(p => s.City[p] >= 0 && s.Owner[s.City[p]] == s.Owner[p] && Cities.IsCity(s, s.City[p])),
            "every owned province belongs to a city of its owner");
        Check(Enumerable.Range(0, w.P).Where(p => Cities.IsCity(s, p) && s.Owner[p] >= 0).All(p => s.City[p] == p), "a city belongs to itself");

        Section("cities: growth inside the sphere, up to the cap");
        var init = Cities.Counts(w, s).ToArray();   // starting borders may already exceed a cap
        int start = s.Owner.Count(o => o == Me);
        var cycles = new List<int>();
        int last = start;
        for (int k = 0; k < 6000; k++)
        {
            Simulation.Step(w, s, null);
            int now = s.Owner.Count(o => o == Me);
            if (now != last) { cycles.Add(Clock.CycleOf(s.Tick)); last = now; }
        }
        int grown = last - start;
        Check(grown > 0, $"the player's cities grew by themselves: {start} → {last} provinces");
        if (cycles.Count > 1)
        {
            double avgSec = (cycles[^1] - cycles[0]) / (double)(cycles.Count - 1) * Clock.CycleTicks / Clock.TicksPerSecond[Clock.ReferenceSpeed];
            Check(avgSec > 5 && avgSec < 200, $"≈ {avgSec:0} s at speed 3 between the player's new provinces");
        }
        var cnt = Cities.Counts(w, s);
        Check(Enumerable.Range(0, w.P).Where(c => Cities.IsCity(s, c) && s.Owner[c] >= 0).All(c => (init[c] == 0 || cnt[c] <= System.Math.Max(Cities.Cap(s, c), init[c]))),
            "no starting city grew past its cap (new towns take their nearest land on founding)");
        Check(Enumerable.Range(0, w.P).Where(p => s.Owner[p] >= 0).All(p => s.City[p] >= 0), "grown provinces have a city");
        foreach (int c in Enumerable.Range(0, w.P).Where(c => Cities.IsCity(s, c) && s.Owner[c] >= 0 && init[c] > 0 && cnt[c] > System.Math.Max(Cities.Cap(s, c), init[c])).Take(5))
            System.Console.WriteLine($"      over: city {c} owner {s.Owner[c]} cap {Cities.Cap(s, c)} init {init[c]} now {cnt[c]} era {s.Nat[s.Owner[c]].Era} capital {s.CapitalOf[c] >= 0}");

        Section("cities: the bots' one-pass frontier equals the per-province rule");
        int agree = 0, differ = 0;
        for (int n = 0; n < s.Nat.Length; n++)
        {
            int stamp = Cities.MarkAbsorbable(w, s, n);
            var mark = SimScratch.For(w, s).Mark.ToArray();
            for (int q = 0; q < w.P; q++)
            {
                if (s.Owner[q] >= 0 || w.PLand[q] != 1 || !w.Adj[q].Any(r => s.Owner[r] == n)) continue;
                if ((mark[q] == stamp) == (Cities.Absorber(w, s, q, n) >= 0)) agree++; else differ++;
            }
        }
        Check(differ == 0 && agree > 0, $"MarkAbsorbable = Absorber on {agree} frontier provinces of all nations ({differ} differ)");

        Section("cities: gold cannot beat the limit");
        s.Nat[Me].Treasury += 100_000 * Rules.Cents;
        int full = FullCityFrontier(w, s);
        if (full >= 0) Check(Rules.CheckClaim(w, s, full, Me) == ClaimError.CityFull, $"claim next to a full city is refused ({w.PName[full]})");
        else Check(true, "no full-city frontier on this seed (skip)");

        Section("cities: founding a town (command journal)");
        int site = FoundSite(w, s);
        Check(site >= 0, "a valid site for a new town exists");
        if (site >= 0)
        {
            var q = new CommandQueue();
            var h0 = s.Hash();
            q.Submit(s, Cmd.FoundCity(Me, site));
            int r = q.Flush(w, s, null);
            Check(r == 0 && Cities.IsCity(s, site) && s.Owner[site] == Me && s.City[site] == site, $"town founded at {w.PName[site]}");
            Check(Cities.Check(w, s, site, Me) == FoundError.IsCity, "cannot found twice");
            Check(!s.Hash().Equals(h0), "state changed");
            foreach (int n in w.Adj[site])
                if (w.PLand[n] == 1 && s.Owner[n] < 0) { Check(Cities.Check(w, s, n, Me) == FoundError.TooClose, "a town right next to it is too close"); break; }
        }

        Section("cities: determinism and bots");
        var a = Fresh(w); var b = Fresh(w);
        for (int k = 0; k < 4000; k++) Simulation.Step(w, a, null);
        for (int k = 0; k < 4000; k += 7) for (int j = 0; j < 7 && k + j < 4000; j++) Simulation.Step(w, b, null);
        Check(a.Hash().Equals(b.Hash()), "same world, same ticks → same cities (batching irrelevant)");
        int botTowns = Enumerable.Range(0, w.P).Count(p => a.IsTown[p] && a.Owner[p] > 0);
        int botTowns0 = Enumerable.Range(0, w.P).Count(p => Fresh(w).IsTown[p] && Fresh(w).Owner[p] > 0);
        Check(botTowns >= botTowns0, $"bots keep their towns and may found new ones: {botTowns0} → {botTowns}");
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }

    static int FullCityFrontier(WorldData w, GameState s)
    {
        var cnt = Cities.Counts(w, s);
        for (int q = 0; q < w.P; q++)
        {
            if (s.Owner[q] >= 0 || w.PLand[q] != 1 || !s.Nat[Me].Fog.Explored[q]) continue;
            if (!Rules.Borders(w, s, q, Me)) continue;
            if (Cities.Absorber(w, s, q, Me) < 0) return q;
        }
        return -1;
    }

    static int FoundSite(WorldData w, GameState s)
    {
        for (int p = 0; p < w.P; p++) if (Cities.Check(w, s, p, Me) == FoundError.None) return p;
        return -1;
    }
}
