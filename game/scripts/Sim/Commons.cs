// pax-allow-file: river polylines are floats; they become province lists once per world (as WorldFacts does)
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>What the shared land suffers in one province: the flood from cut forests upstream and the overgrazing
/// around it, ‰ of fertility lost, with their causes (for the card and the chronicle).</summary>
public readonly record struct CommonsHit(int Flood, int Mills, int Grazing, int Pastures)
{
    public int Total => Flood + Grazing;
}

/// <summary>
/// «Последствия общие для всех»: the land does not care whose it is. Lumber mills along a river (on it or beside it)
/// thin the forests that held its water: from <see cref="MillsFree"/> mills on, every province downstream of the first
/// of them floods (−90 ‰ fertility per mill beyond, at most −450 ‰, and a sullen mood). Pastures crowded on the
/// steppe wear it out: <see cref="PasturesFree"/> or more in a province and its neighbours cost each of those grassy
/// provinces −80 ‰ per pasture beyond (twice beside a desert, at most −400 ‰). Whoever built them — a neighbour, a bot,
/// the player — everybody downstream pays. A pure function of the buildings (nothing stored); the rules apply it on
/// top of the climate where the land feeds people (<see cref="FertNow"/>).
/// </summary>
public static class Commons
{
    public const int MillsFree = 2, FloodPerMill = 90, FloodMax = 450;
    public const int PasturesFree = 2, GrazePerPasture = 80, GrazeMax = 400;

    sealed class Basins
    {
        public int[][] Course;      // per river: its land provinces from the head down, each once
        public int[][] Touch;       // per province: (river, position) pairs packed as river * 4096 + position where it lies on or beside the river
    }

    static readonly ConditionalWeakTable<WorldData, Basins> Cache = new();

    /// <summary>Work arrays per game state, refilled by every call (the rules call this every cycle: no garbage per tick).</summary>
    sealed class Buffers { public CommonsHit[] Hit; public int[] Fert; public int[][] Mills; public bool[] Any; }
    static readonly ConditionalWeakTable<GameState, Buffers> Pool = new();

    static Basins Of(WorldData w) => Cache.GetValue(w, Build);

    static Basins Build(WorldData w)
    {
        var course = new int[w.Rivers.Count][];
        var touch = new List<int>[w.P];
        for (int r = 0; r < w.Rivers.Count; r++)
        {
            var path = w.Rivers[r];
            var list = new List<int>();
            for (int k = 0; k < path.Xs.Length; k++)
            {
                int x = ((int)MathF.Floor(path.Xs[k]) % w.W + w.W) % w.W, y = Math.Clamp((int)MathF.Floor(path.Ys[k]), 0, w.H - 1);
                int p = w.Prov[y * w.W + x];
                if (w.PLand[p] != 1 || (list.Count > 0 && list.Contains(p))) continue;
                list.Add(p);
            }
            course[r] = list.ToArray();
            for (int i = 0; i < list.Count && i < 4096; i++)
            {
                Add(list[i], r, i);
                foreach (int q in w.Adj[list[i]]) if (w.PLand[q] == 1) Add(q, r, i);
            }
        }
        var t = new int[w.P][];
        for (int p = 0; p < w.P; p++) t[p] = touch[p]?.ToArray() ?? Array.Empty<int>();
        return new Basins { Course = course, Touch = t };

        void Add(int q, int r, int i)
        {
            var l = touch[q] ??= new List<int>(2);
            int key = r * 4096 + i;
            for (int j = 0; j < l.Count; j++)
                if (l[j] / 4096 == r) { if (l[j] % 4096 > i) l[j] = key; return; }   // the highest point of the river it touches
            l.Add(key);
        }
    }

    /// <summary>Is province p on a river course (it floods, as opposed to only feeding the flood)?</summary>
    public static bool OnRiver(WorldData w, int p)
    {
        foreach (var c in Of(w).Course) if (Array.IndexOf(c, p) >= 0) return true;
        return false;
    }

    /// <summary>The harm to every province now (fresh from the buildings: call once per pass). The array belongs to the
    /// state's work buffers and is refilled by the next call: read it at once, do not keep it.</summary>
    public static CommonsHit[] Hits(WorldData w, GameState s)
    {
        var b = Of(w);
        var buf = Pool.GetValue(s, _ => new Buffers());
        if (buf.Hit == null || buf.Hit.Length != w.P || buf.Mills == null || buf.Mills.Length != b.Course.Length)
        {
            buf.Hit = new CommonsHit[w.P];
            buf.Mills = new int[b.Course.Length][];
            for (int r = 0; r < b.Course.Length; r++) buf.Mills[r] = new int[b.Course[r].Length];
            buf.Any = new bool[b.Course.Length];
        }
        var hit = buf.Hit;
        Array.Clear(hit);
        // floods: the mills by the river and the place they touch it, then counted from the head down
        var mills = buf.Mills;
        for (int r = 0; r < mills.Length; r++) if (buf.Any[r]) { Array.Clear(mills[r]); buf.Any[r] = false; }
        for (int q = 0; q < w.P; q++)
        {
            if (b.Touch[q].Length == 0 || !s.Buildings[q].Contains(Bld.Lumber)) continue;
            foreach (int key in b.Touch[q]) { mills[key / 4096][key % 4096]++; buf.Any[key / 4096] = true; }
        }
        for (int r = 0; r < b.Course.Length; r++)
        {
            var c = b.Course[r];
            var m = mills[r];
            if (!buf.Any[r]) continue;
            int up = 0;
            for (int i = 0; i < c.Length; i++)
            {
                up += m[i];
                int flood = Math.Min(FloodMax, Math.Max(0, up - MillsFree) * FloodPerMill);
                int p = c[i];
                if (flood > hit[p].Flood) hit[p] = hit[p] with { Flood = flood, Mills = up };
            }
        }
        // overgrazing: pastures in each grassy province and around it
        var kinds = Climate.KindsOf(w);
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] != 1 || w.PBiome[p] is not (4 or 6 or 9 or 10 or 11 or 13)) continue;
            int n = s.Buildings[p].Contains(Bld.Pasture) ? 1 : 0;
            foreach (int q in w.Adj[p]) if (s.Buildings[q].Contains(Bld.Pasture)) n++;
            if (n <= PasturesFree) continue;
            int graze = (n - PasturesFree) * GrazePerPasture * (kinds[p] == ClimateKind.DesertEdge ? 2 : 1);
            hit[p] = hit[p] with { Grazing = Math.Min(GrazeMax, graze), Pastures = n };
        }
        return hit;
    }

    /// <summary>Fertility the land gives now: the climate's (<see cref="Climate.FertNow"/>) less the shared harm, worked out
    /// afresh by every call (buildings change between ticks) into the state's work buffer: read it at once, do not keep it.</summary>
    public static int[] FertNow(WorldData w, GameState s) => FertNow(w, s, Hits(w, s));

    public static int[] FertNow(WorldData w, GameState s, CommonsHit[] hits)
    {
        var climate = Climate.FertNow(w, s);
        var buf = Pool.GetValue(s, _ => new Buffers());
        var f = buf.Fert is { Length: var len } && len == w.P ? buf.Fert : buf.Fert = new int[w.P];
        for (int p = 0; p < w.P; p++) f[p] = Math.Max(0, climate[p] - climate[p] * Math.Min(800, hits[p].Total) / 1000);
        return f;
    }

    /// <summary>Mood target lost to a flood (a point per 75 ‰, at most 6).</summary>
    public static int FloodMood(in CommonsHit h) => h.Flood / 75;

    /// <summary>Card lines for p, or null: «Паводки: выше по реке 5 лесопилок · плодородие −27 %».</summary>
    public static string Describe(in CommonsHit h)
    {
        if (h.Total == 0) return null;
        var parts = new List<string>(2);
        if (h.Flood > 0) parts.Add($"Паводки: у реки выше {Ru.Count(h.Mills, "лесопилка", "лесопилки", "лесопилок")} · плодородие −{h.Flood / 10} %");
        if (h.Grazing > 0) parts.Add($"Перевыпас: {Ru.Count(h.Pastures, "пастбище", "пастбища", "пастбищ")} рядом · плодородие −{h.Grazing / 10} %");
        return string.Join("\n", parts);
    }
}
