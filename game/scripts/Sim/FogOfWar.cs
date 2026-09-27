using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>Result of a visibility pass.</summary>
public readonly struct FogDelta
{
    public static readonly FogDelta None = new(Array.Empty<int>(), 0, Array.Empty<int>());
    public readonly int[] Changed;       // provinces whose Fog state changed
    public readonly int NewlyExplored;   // how many of them were unexplored before
    public readonly int[] NewlyMet;      // nations met for the first time (never the local player)
    public FogDelta(int[] changed, int newlyExplored, int[] newlyMet) { Changed = changed; NewlyExplored = newlyExplored; NewlyMet = newlyMet; }
    public bool Any => Changed.Length > 0 || NewlyMet.Length > 0;
}

/// <summary>
/// Fog of war (pure C#, port of the logic half of docs/mockups/js/fog.js).
/// GameState.Fog: 0 unexplored, 1 explored but not seen now (stale), 2 visible. GameState.Explored never reverts.
/// GameState.KnownOwner remembers who owned a province when it was last seen; nations are met through it.
/// Vision sources: own provinces (range 2, capital 3), the capital's trade routes (range 1 + neighbours), scouts (range 2).
/// Entering a sea zone costs 2 range, so coasts do not reveal whole oceans.
/// The real fog state is kept even in observer mode (FogEnabled = false) so switching back is instant.
/// </summary>
public static class FogOfWar
{
    public const int OwnRange = 2, CapitalRange = 3, RouteRange = 1, ScoutRange = 2, InitialRange = 6;
    public const int MaxRange = InitialRange;
    public const int SeaCost = 2;
    /// <summary>Unexplored islands of at most this many provinces, fully enclosed by explored ones, are filled in.</summary>
    public const int PocketMax = 3;

    public static void Init(WorldData w, GameState s)
    {
        int P = w.P, nN = Math.Max(s.NationCapital?.Length ?? 0, Data.Nations.Length);
        if (s.Fog == null || s.Fog.Length != P) s.Fog = new byte[P]; else Array.Clear(s.Fog);
        if (s.Explored == null || s.Explored.Length != P) s.Explored = new bool[P]; else Array.Clear(s.Explored);
        if (s.KnownOwner == null || s.KnownOwner.Length != P) s.KnownOwner = new short[P];
        Array.Fill(s.KnownOwner, (short)-1);
        s.Met = new bool[nN];
        s.Scouts.Clear();

        // explored at the start: everything within InitialRange of the player's land
        var sc = SimScratch.For(w, s);
        var seeds = sc.Sources; seeds.Clear();
        for (int p = 0; p < P; p++) if (s.Owner[p] == GameState.LocalPlayer) seeds.Add((p, InitialRange));
        Reach(w, sc, seeds);
        for (int p = 0; p < P; p++) if (sc.Rem[p] >= 0) { s.Explored[p] = true; s.KnownOwner[p] = s.Owner[p]; }

        Recompute(w, s);   // initial vision + met nations; nothing is announced for what is known at the start
    }

    /// <summary>Recompute visibility from the current sources. Mutates Fog/Explored/Met and reports what changed.</summary>
    public static FogDelta Recompute(WorldData w, GameState s)
    {
        int P = w.P;
        var sc = SimScratch.For(w, s);
        Reach(w, sc, VisionSources(w, s, sc));
        var rem = sc.Rem;

        for (int p = 0; p < P; p++) if (rem[p] >= 0) s.Explored[p] = true;
        FillPockets(w, s, sc);

        List<int> changed = null; int fresh = 0;
        for (int p = 0; p < P; p++)
        {
            byte v = rem[p] >= 0 ? (byte)2 : s.Explored[p] ? (byte)1 : (byte)0;
            if (v == s.Fog[p]) continue;
            if (s.Fog[p] == 0) fresh++;
            s.Fog[p] = v;
            (changed ??= new List<int>()).Add(p);
        }

        // what the player sees now refreshes the map's memory of owners; stale provinces keep the old one
        var known = s.KnownOwner;
        for (int p = 0; p < P; p++) if (rem[p] >= 0) known[p] = s.Owner[p];

        List<int> met = null;
        for (int p = 0; p < P; p++)
        {
            int o = known[p];
            if (o < 0 || o >= s.Met.Length || s.Met[o] || !s.Explored[p]) continue;
            s.Met[o] = true;
            if (o != GameState.LocalPlayer) (met ??= new List<int>()).Add(o);
        }
        if (changed == null && met == null) return FogDelta.None;
        return new FogDelta(changed?.ToArray() ?? Array.Empty<int>(), fresh, met?.ToArray() ?? Array.Empty<int>());
    }

    /// <summary>Recompute and report through the sink (fog event + «new nation met» chronicle). Returns newly explored count.</summary>
    public static int Refresh(WorldData w, GameState s, ISimSink sink)
    {
        var d = Recompute(w, s);
        if (d.Changed.Length > 0) sink?.RaiseFogChanged(d.Changed);
        NotifyMet(d, sink);
        return d.NewlyExplored;
    }

    public static void NotifyMet(in FogDelta d, ISimSink sink)
    {
        if (sink == null) return;
        foreach (int n in d.NewlyMet)
        {
            var nat = Data.Nations[n];
            sink.Notify("affiliate", $"Встречена новая держава: {nat.Name} ({nat.Gov.ToLowerInvariant()})");
        }
    }

    /// <summary>Is province p shown to the local player (visible or remembered)? Observer mode shows everything.</summary>
    public static bool IsKnown(GameState s, int p) => !s.FogEnabled || s.Fog[p] != 0;

    // ---------------------------------------------------------------------------------------------------------

    static List<(int p, int r)> VisionSources(WorldData w, GameState s, SimScratch sc)
    {
        var src = sc.Sources; src.Clear();
        int P = w.P, me = GameState.LocalPlayer, cap = s.NationCapital != null && s.NationCapital.Length > me ? s.NationCapital[me] : -1;
        for (int p = 0; p < P; p++) if (s.Owner[p] == me) src.Add((p, p == cap ? CapitalRange : OwnRange));
        if (cap >= 0)
            foreach (var rt in s.Routes)
            {
                if (rt == null || rt.Length == 0 || (rt[0] != cap && rt[^1] != cap)) continue;
                foreach (int p in rt)
                {
                    src.Add((p, RouteRange));
                    foreach (int n in w.Adj[p]) src.Add((n, 0));
                }
            }
        foreach (var scout in s.Scouts)
            if (scout.Path != null && scout.Path.Length > 0) src.Add((Scouts.Current(scout), ScoutRange));
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
                foreach (int n in w.Adj[p])
                {
                    int m = r - (w.PLand[n] == 1 ? 1 : SeaCost);
                    if (m >= 0 && rem[n] < m) { rem[n] = (sbyte)m; B[m].Add(n); }
                }
            }
        }
    }

    /// <summary>Unexplored components of ≤ PocketMax provinces are enclosed by explored land/sea: mark them explored.</summary>
    static void FillPockets(WorldData w, GameState s, SimScratch sc)
    {
        int P = w.P; var ex = s.Explored; var stack = sc.Stack;
        int stamp = sc.NextStamp();
        Span<int> comp = stackalloc int[PocketMax];
        for (int p0 = 0; p0 < P; p0++)
        {
            if (ex[p0] || sc.Mark[p0] == stamp) continue;
            // cheap reject: a pocket province has at least one explored neighbour
            bool touches = false;
            foreach (int n in w.Adj[p0]) if (ex[n]) { touches = true; break; }
            if (!touches) continue;

            int sp = 0, size = 0; bool small = true;
            stack[sp++] = p0; sc.Mark[p0] = stamp;
            while (sp > 0)
            {
                int p = stack[--sp];
                if (size < PocketMax) comp[size] = p;
                if (++size > PocketMax) { small = false; }
                foreach (int n in w.Adj[p])
                    if (!ex[n] && sc.Mark[n] != stamp) { sc.Mark[n] = stamp; stack[sp++] = n; }
            }
            if (!small) continue;
            for (int k = 0; k < size; k++) { ex[comp[k]] = true; s.KnownOwner[comp[k]] = s.Owner[comp[k]]; }
        }
    }
}
