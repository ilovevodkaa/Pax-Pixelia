using System;
using System.Collections.Generic;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>Result of a visibility pass for one nation.</summary>
public readonly struct FogDelta
{
    public static readonly FogDelta None = new(-1, Array.Empty<int>(), 0, Array.Empty<int>());
    public readonly int Nation;          // whose map changed
    public readonly int[] Changed;       // provinces whose Fog state changed
    public readonly int NewlyExplored;   // how many of them were unexplored before
    public readonly int[] NewlyMet;      // nations met for the first time (never the nation itself)
    public FogDelta(int nation, int[] changed, int newlyExplored, int[] newlyMet) { Nation = nation; Changed = changed; NewlyExplored = newlyExplored; NewlyMet = newlyMet; }
    public bool Any => Changed.Length > 0 || NewlyMet.Length > 0;
}

/// <summary>
/// Fog of war per nation (pure C#, port of the logic half of docs/mockups/js/fog.js). Only nations with a map
/// (<see cref="NationState.Fog"/>, the human ones) are tracked; bots are not limited by fog.
/// Vision sources of a nation: its provinces (range 2, capital 3), its capital's trade routes (range 1 + neighbours),
/// its scouts (range 2). Entering a sea zone costs 2 range, so coasts do not reveal whole oceans.
/// The fog is kept in observer mode (FogEnabled = false) so switching back is instant.
/// </summary>
public static class FogOfWar
{
    public const int OwnRange = 2, CapitalRange = 3, RouteRange = 1, ScoutRange = 2, InitialRange = 6;
    public const int MaxRange = InitialRange;
    public const int SeaCost = 2;
    /// <summary>Unexplored islands of at most this many provinces, fully enclosed by explored ones, are filled in.</summary>
    public const int PocketMax = 3;

    /// <summary>Fresh maps for the human nations: everything within InitialRange of their land is explored.</summary>
    public static void Init(WorldData w, GameState s)
    {
        int P = w.P, nN = s.Nat.Length;
        s.Scouts.Clear();
        var sc = SimScratch.For(w, s);
        for (int n = 0; n < nN; n++)
        {
            if (!s.Nat[n].Human) { s.Nat[n].Fog = null; continue; }
            var f = s.Nat[n].Fog = new NationFog { Fog = new byte[P], Explored = new bool[P], KnownOwner = new short[P], Met = new bool[nN] };
            Array.Fill(f.KnownOwner, (short)-1);
            var seeds = sc.Sources; seeds.Clear();
            for (int p = 0; p < P; p++) if (s.Owner[p] == n) seeds.Add((p, InitialRange));
            if (s.Nat[n].Camp >= 0) seeds.Add((s.Nat[n].Camp, Nomads.InitialSight));   // a tribe knows its own hunting grounds
            Reach(w, sc, seeds);
            for (int p = 0; p < P; p++) if (sc.Rem[p] >= 0) { f.Explored[p] = true; f.KnownOwner[p] = s.Owner[p]; }
            Recompute(w, s, n);   // initial vision + met nations; nothing is announced for what is known at the start
        }
    }

    /// <summary>Recompute nation n's visibility from its current sources. Mutates its map and reports what changed.</summary>
    public static FogDelta Recompute(WorldData w, GameState s, int n)
    {
        var f = s.Nat[n].Fog;
        if (f == null) return FogDelta.None;
        int P = w.P;
        var sc = SimScratch.For(w, s);
        Reach(w, sc, VisionSources(w, s, n, sc));
        var rem = sc.Rem;

        for (int p = 0; p < P; p++) if (rem[p] >= 0) f.Explored[p] = true;
        FillPockets(w, s, f, sc);

        List<int> changed = null; int fresh = 0;
        for (int p = 0; p < P; p++)
        {
            byte v = rem[p] >= 0 ? (byte)2 : f.Explored[p] ? (byte)1 : (byte)0;
            if (v == f.Fog[p]) continue;
            if (f.Fog[p] == 0) fresh++;
            f.Fog[p] = v;
            (changed ??= new List<int>()).Add(p);
        }

        // what the nation sees now refreshes its memory of owners; stale provinces keep the old one
        var known = f.KnownOwner;
        for (int p = 0; p < P; p++) if (rem[p] >= 0) known[p] = s.Owner[p];

        List<int> met = null;
        for (int p = 0; p < P; p++)
        {
            int o = known[p];
            if (o < 0 || o >= f.Met.Length || f.Met[o] || !f.Explored[p]) continue;
            f.Met[o] = true;
            if (o != n) (met ??= new List<int>()).Add(o);
        }
        if (changed == null && met == null) return FogDelta.None;
        return new FogDelta(n, changed?.ToArray() ?? Array.Empty<int>(), fresh, met?.ToArray() ?? Array.Empty<int>());
    }

    /// <summary>Recompute every tracked nation and report through the sink (fog event + «new nation met» chronicle).</summary>
    public static void Refresh(WorldData w, GameState s, ISimSink sink)
    {
        for (int n = 0; n < s.Nat.Length; n++) if (s.Nat[n].Fog != null) RefreshNation(w, s, n, sink);
    }

    /// <summary>Recompute nation n and report through the sink. Returns how many provinces it newly explored.</summary>
    public static int RefreshNation(WorldData w, GameState s, int n, ISimSink sink)
    {
        var d = Recompute(w, s, n);
        if (d.Changed.Length > 0) sink?.RaiseFogChanged(d.Changed);
        NotifyMet(s, d, sink);
        return d.NewlyExplored;
    }

    public static void NotifyMet(GameState s, in FogDelta d, ISimSink sink)
    {
        if (sink == null) return;
        foreach (int m in d.NewlyMet)
        {
            var nat = s.Nations[m];
            sink.Notify("affiliate", $"Встречена новая держава: {nat.Name} ({nat.Gov.ToLowerInvariant()})");
        }
    }

    /// <summary>Is province p on nation n's map (visible or remembered)? Observer mode shows everything.</summary>
    public static bool IsKnown(GameState s, int n, int p) => !s.FogEnabled || s.Nat[n].Fog is not { } f || f.Fog[p] != 0;

    // ---------------------------------------------------------------------------------------------------------

    static List<(int p, int r)> VisionSources(WorldData w, GameState s, int n, SimScratch sc)
    {
        var src = sc.Sources; src.Clear();
        int P = w.P, cap = Scouts.Capital(s, n);
        for (int p = 0; p < P; p++) if (s.Owner[p] == n) src.Add((p, p == cap ? CapitalRange : OwnRange));
        if (s.Nat[n].Camp >= 0) src.Add((s.Nat[n].Camp, Nomads.CampSight));
        if (cap >= 0)
            foreach (var rt in s.Routes)
            {
                if (rt == null || rt.Length == 0 || (rt[0] != cap && rt[^1] != cap)) continue;
                foreach (int p in rt)
                {
                    src.Add((p, RouteRange));
                    foreach (int q in w.Adj[p]) src.Add((q, 0));
                }
            }
        foreach (var scout in s.Scouts)
            if (scout.Nation == n && scout.Path != null && scout.Path.Length > 0) src.Add((Scouts.Current(scout), ScoutRange + Nomads.MythScoutRange(s, n) + Techs.Sum(s.Nat[n], TechFx.ScoutRange)));
        return src;
    }

    /// <summary>
    /// Multi-source BFS where every source has its own range: fills sc.Rem with the remaining range (-1 = unreached).
    /// Buckets by remaining range, highest first, so each province is expanded with its best range.
    /// </summary>
    static void Reach(WorldData w, SimScratch sc, List<(int p, int r)> sources)
    {
        var rem = sc.Rem; var B = sc.Buckets;
        Array.Fill(rem, (sbyte)-1);
        foreach (var b in B) b.Clear();
        foreach (var (p, r0) in sources)
        {
            int r = Math.Min(r0, MaxRange);
            if (r > rem[p]) { rem[p] = (sbyte)r; B[r].Add(p); }
        }
        for (int r = MaxRange; r > 0; r--)
        {
            var bucket = B[r];
            for (int k = 0; k < bucket.Count; k++)
            {
                int p = bucket[k];
                if (rem[p] != r) continue;   // improved later through a better path — already expanded there
                foreach (int q in w.Adj[p])
                {
                    int m = r - (w.PLand[q] == 1 ? 1 : SeaCost);
                    if (m >= 0 && rem[q] < m) { rem[q] = (sbyte)m; B[m].Add(q); }
                }
            }
        }
    }

    /// <summary>Unexplored components of ≤ PocketMax provinces enclosed by explored land/sea: mark them explored.</summary>
    static void FillPockets(WorldData w, GameState s, NationFog f, SimScratch sc)
    {
        int P = w.P; var ex = f.Explored; var stack = sc.Stack;
        int stamp = sc.NextStamp();
        Span<int> comp = stackalloc int[PocketMax];
        for (int p0 = 0; p0 < P; p0++)
        {
            if (ex[p0] || sc.Mark[p0] == stamp) continue;
            // cheap reject: a pocket province has at least one explored neighbour
            bool touches = false;
            foreach (int q in w.Adj[p0]) if (ex[q]) { touches = true; break; }
            if (!touches) continue;

            int sp = 0, size = 0; bool small = true;
            stack[sp++] = p0; sc.Mark[p0] = stamp;
            while (sp > 0)
            {
                int p = stack[--sp];
                if (size < PocketMax) comp[size] = p;
                if (++size > PocketMax) small = false;
                foreach (int q in w.Adj[p])
                    if (!ex[q] && sc.Mark[q] != stamp) { sc.Mark[q] = stamp; stack[sp++] = q; }
            }
            if (!small) continue;
            for (int k = 0; k < size; k++) { ex[comp[k]] = true; f.KnownOwner[comp[k]] = s.Owner[comp[k]]; }
        }
    }
}
