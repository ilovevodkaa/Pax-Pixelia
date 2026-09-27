using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>
/// Initial game state of the ancient era (port of the mockup's genNations, docs/mockups/js/worldgen.js):
/// 16 nations with spread-out capitals and compact territories, populations, religions, mood, building slots and
/// buildings from terrain, ores, towns and the first trade routes. Deterministic for the world's seed.
/// </summary>
public static class NationGen
{
    const int PlayerProvinces = 14;             // others: 8..18
    const double PlayerMinLandShare = .1;       // the player's land mass holds at least this share of all land
    const int GrowthRounds = 20;
    static readonly (int A, int B)[] RoutePairs = { (0, 1), (0, 2), (1, 3), (2, 4) };

    public static GameState CreateInitialState(WorldData w)
    {
        int P = w.P;
        var s = new GameState
        {
            Owner = new short[P], Pop = new float[P], Religion = new sbyte[P], Mood = new byte[P], Slots = new byte[P],
            Buildings = new List<Data.Bld>[P], Ore = new sbyte[P], OreFound = new bool[P], CapitalOf = new short[P], IsTown = new bool[P],
            Fog = new byte[P], Explored = new bool[P]
        };
        Array.Fill(s.Owner, (short)-1);
        Array.Fill(s.CapitalOf, (short)-1);
        Array.Fill(s.Religion, (sbyte)-1);
        Array.Fill(s.Ore, (sbyte)-1);
        for (int p = 0; p < P; p++) s.Buildings[p] = new List<Data.Bld>();

        s.NationCapital = PlaceCapitals(w);
        for (int n = 0; n < s.NationCapital.Length; n++) { s.Owner[s.NationCapital[n]] = (short)n; s.CapitalOf[s.NationCapital[n]] = (short)n; }
        GrowTerritories(w, s);
        Populate(w, s);
        PickTowns(s);
        foreach (var (a, b) in RoutePairs)
            if (b < s.NationCapital.Length && TradePath(w, s.NationCapital[a], s.NationCapital[b]) is { } path) s.Routes.Add(path);
        return s;
    }

    /// <summary>Capitals: the player's near the centre of the map, the rest by farthest-point sampling among fertile,
    /// non-polar provinces of big land masses; then ordered by distance from the first (neighbours get low indices).
    /// One deliberate change from the mockup: the player's capital must lie on a land mass with at least
    /// <see cref="PlayerMinLandShare"/> of all land — the mockup could start the player on a tiny island (seed 42),
    /// and scouts can't cross the sea before seafaring. Worlds where the mockup's choice was fine are unaffected.</summary>
    static int[] PlaceCapitals(WorldData w)
    {
        int nNations = Data.Nations.Length;
        double ks = w.W / 1024.0;
        var cand = new List<int>();
        for (int p = 0; p < w.P; p++)
            if (w.PLand[p] != 0 && w.PSize[p] >= 40 && w.PFert[p] >= .5 && w.BodySize[w.PBody[p]] >= 670 * ks * ks && Math.Abs(w.PCY[p] / (double)w.H - .5) < .36)
                cand.Add(p);
        if (cand.Count == 0) // degenerate world: any land will do
            for (int p = 0; p < w.P; p++) if (w.PLand[p] != 0) cand.Add(p);

        long allLand = 0;
        for (int c = 0; c < w.BodySize.Length; c++) if (w.BodyLand[c] != 0) allLand += w.BodySize[c];
        int first = ClosestToCentre(w, cand, p => w.BodySize[w.PBody[p]] >= PlayerMinLandShare * allLand);
        if (first < 0) first = ClosestToCentre(w, cand, _ => true);
        var caps = new List<int> { first };
        var nearest = new double[cand.Count];   // distance from each candidate to its nearest capital so far
        for (int c = 0; c < cand.Count; c++) nearest[c] = Math.Min(1e9, Dist(w, cand[c], first));
        while (caps.Count < nNations && caps.Count < cand.Count)
        {
            int bp = -1; double bd = -1;
            for (int c = 0; c < cand.Count; c++) if (nearest[c] > bd) { bd = nearest[c]; bp = cand[c]; }
            caps.Add(bp);
            for (int c = 0; c < cand.Count; c++) nearest[c] = Math.Min(nearest[c], Dist(w, cand[c], bp));
        }
        return caps.Take(1).Concat(caps.Skip(1).OrderBy(p => Dist(w, p, first))).ToArray(); // OrderBy is stable, like V8's sort
    }

    static int ClosestToCentre(WorldData w, List<int> cand, Func<int, bool> allowed)
    {
        int best = -1;
        double bd = 1e9;
        foreach (int p in cand)
        {
            if (!allowed(p)) continue;
            double dx = Math.Abs(w.PCX[p] - w.W * .5), dy = w.PCY[p] - w.H * .44, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    /// <summary>Compact growth, one province per nation per round: fertile, well-connected, close to the capital.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void GrowTerritories(WorldData w, GameState s)
    {
        int P = w.P, nN = s.NationCapital.Length, seed = w.Seed;
        var own = s.Owner;
        var target = new int[nN];
        var count = new int[nN];
        for (int n = 0; n < nN; n++) { target[n] = n == 0 ? PlayerProvinces : 8 + (int)(Noise.H2(n, 5, seed) * 11); count[n] = 1; }
        for (int round = 0; round < GrowthRounds; round++)
            for (int n = 0; n < nN; n++)
            {
                if (count[n] >= target[n]) continue;
                int bp = -1; double bs = -1;
                for (int p = 0; p < P; p++)
                {
                    if (own[p] != n) continue;
                    foreach (int q in w.Adj[p])
                    {
                        if (own[q] >= 0 || w.PLand[q] == 0) continue;
                        int nb = 0;
                        foreach (int r in w.Adj[q]) if (own[r] == n) nb++;
                        double sc = w.PFert[q] * .6 + Noise.H2(q, n, seed) * .2 + nb * .35 - Dist(w, q, s.NationCapital[n]) / 55 + (w.PSize[q] > 30 ? .1 : 0);
                        if (sc > bs) { bs = sc; bp = q; }
                    }
                }
                if (bp >= 0) { own[bp] = (short)n; count[n]++; }
            }
    }

    static void Populate(WorldData w, GameState s)
    {
        int seed = w.Seed;
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] == 0) continue;
            int o = s.Owner[p];
            bool capital = s.CapitalOf[p] >= 0;
            double r = .7 + .6 * Noise.H2(p, 77, seed);
            s.Pop[p] = (float)(w.PSize[p] * Math.Max(.05, w.PFert[p]) * (o >= 0 ? 55 : 9) * r * (capital ? 2.6 : 1));
            s.Religion[p] = (sbyte)(o >= 0 ? Data.Nations[o].Religion : Noise.H2(p, 88, seed) < .6 ? 2 : -1); // free tribes: old spirits
            s.Mood[p] = (byte)(52 + (int)(Noise.H2(p, 66, seed) * 36));
            s.Slots[p] = (byte)(2 + (w.PSize[p] > 60 ? 1 : 0) + (w.PSize[p] > 120 ? 1 : 0) + (w.PCoast[p] != 0 ? 1 : 0));
            if (w.PH[p] > .34 && Noise.H2(p, 99, seed) < .4)
            {
                s.Ore[p] = (sbyte)(int)(Noise.H2(p, 98, seed) * Data.Ores.Length);
                s.OreFound[p] = o == GameState.LocalPlayer && Noise.H2(p, 97, seed) < .5;
            }
            if (o < 0) continue;
            var opts = BuildOptions(w, p);
            int nb = Math.Min(s.Slots[p] - 1, 1 + (int)(w.PFert[p] * 3)), shift = (int)(Noise.H2(p, 55, seed) * 3);
            for (int j = 0; j < nb && j < opts.Count; j++) s.Buildings[p].Add(opts[(j + shift) % opts.Count]);
            if (capital && !s.Buildings[p].Contains(Data.Bld.Shrine)) s.Buildings[p].Add(Data.Bld.Shrine);
        }
    }

    /// <summary>Most populous non-capital provinces become towns: two for the player, one for everyone else.</summary>
    static void PickTowns(GameState s)
    {
        for (int n = 0; n < s.NationCapital.Length; n++)
        {
            var ps = new List<int>();
            for (int p = 0; p < s.Owner.Length; p++) if (s.Owner[p] == n && s.CapitalOf[p] < 0) ps.Add(p);
            foreach (int p in ps.OrderByDescending(p => s.Pop[p]).Take(n == GameState.LocalPlayer ? 2 : 1)) s.IsTown[p] = true;
        }
    }

    /// <summary>Buildings the terrain of a land province allows, in the mockup's order.</summary>
    public static List<Data.Bld> BuildOptions(WorldData w, int p)
    {
        int b = w.PBiome[p];
        float h = w.PH[p];
        var o = new List<Data.Bld>(7);
        if ((b == 9 || b == 10 || b == 11 || b == 13 || b == 6) && h <= .6) o.Add(Data.Bld.Farm);
        if (b == 5 || b == 8 || b == 12) o.Add(Data.Bld.Lumber);
        if (h > .34) o.Add(Data.Bld.Quarry);
        if (w.PCoast[p] != 0) o.Add(Data.Bld.Fishery);
        if (b == 4 || b == 6 || b == 11 || b == 13) o.Add(Data.Bld.Pasture);
        o.Add(Data.Bld.Granary); o.Add(Data.Bld.Market);
        return o;
    }

    /// <summary>Shortest path in provinces (land and sea) by BFS over adjacency, or null if unreachable.</summary>
    public static int[] TradePath(WorldData w, int a, int b)
    {
        var prev = new int[w.P];
        Array.Fill(prev, -2);
        prev[a] = -1;
        var queue = new List<int> { a };
        for (int head = 0; head < queue.Count; head++)
        {
            int p = queue[head];
            if (p == b) break;
            foreach (int q in w.Adj[p]) if (prev[q] == -2) { prev[q] = p; queue.Add(q); }
        }
        if (prev[b] == -2) return null;
        var path = new List<int>();
        for (int p = b; p != -1; p = prev[p]) path.Add(p);
        path.Reverse();
        return path.ToArray();
    }

    /// <summary>Distance between province centres with x wrap (V8 hypot: keeps ties identical to the mockup).</summary>
    public static double Dist(WorldData w, int a, int b)
    {
        int dx = Math.Abs(w.PCX[a] - w.PCX[b]);
        if (dx > w.W / 2) dx = w.W - dx;
        return JsMath.Hypot(dx, w.PCY[a] - w.PCY[b]);
    }
}
