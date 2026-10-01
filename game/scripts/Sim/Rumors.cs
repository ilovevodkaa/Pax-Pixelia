using System;
using System.Collections.Generic;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>A rumor of a nation the viewer has not met: the unexplored province the talk points at and which line is told.</summary>
public readonly record struct Rumor(int Nation, int Province, int Line);

/// <summary>
/// Rumors instead of an empty fog: a nation that has not met another one within <see cref="Reach"/> hears of it — smoke of
/// strange fires in the first era, merchants' tales later. The talk points at a province near the other nation's home
/// (its capital, or the camp of a tribe) and lasts while that home lies under the clouds; meeting the nation or exploring
/// its home ends it. Pure function of the state (fog, homes, the world seed): nothing is stored, so saves and the lockstep
/// are untouched. The map draws the smoke, the tooltip and the panel tell the line, the chronicle announces new ones.
/// </summary>
public static class Rumors
{
    /// <summary>How far talk travels from the viewer's home, world px: a third of the world at the start, more every era.</summary>
    public const int BaseReach = 560, ReachPerEra = 160;
    /// <summary>The talk points up to this many land steps off the other nation's home (rumors are vague).</summary>
    public const int Spread = 2;

    public static int Reach(int era) => BaseReach + ReachPerEra * Math.Max(0, era);

    /// <summary>Where nation n lives: its capital, or the camp of its tribe; -1 when neither.</summary>
    public static int Home(GameState s, int n) => s.NationCapital[n] >= 0 ? s.NationCapital[n] : s.Nat[n].Camp;

    /// <summary>Rumors the viewer hears now (ordered by nation). Empty without a fog map (bots, observer games).</summary>
    public static List<Rumor> Of(WorldData w, GameState s, int viewer, List<Rumor> into = null)
    {
        into ??= new List<Rumor>();
        into.Clear();
        var f = s.Nat[viewer].Fog;
        int from = Home(s, viewer);
        if (f == null || from < 0) return into;
        int reach = Reach(s.Nat[viewer].Era);
        for (int m = 0; m < s.Nat.Length; m++)
        {
            if (m == viewer || (m < f.Met.Length && f.Met[m])) continue;
            int home = Home(s, m);
            if (home < 0 || f.Explored[home] || Simulation.Distance(w, from, home) > reach) continue;
            int p = Spot(w, s, viewer, m, home);
            if (f.Explored[p]) p = home;   // the vague spot has been seen already: the talk now points at the home itself
            into.Add(new Rumor(m, p, SimRng.Pick(w.Seed, 173, viewer, m, 3)));
        }
        return into;
    }

    /// <summary>The rumor that points at province p, or null.</summary>
    public static Rumor? At(IReadOnlyList<Rumor> rumors, int p)
    {
        foreach (var r in rumors) if (r.Province == p) return r;
        return null;
    }

    /// <summary>A land province up to <see cref="Spread"/> steps off home, picked by a salted roll (stable while home is).</summary>
    static int Spot(WorldData w, GameState s, int viewer, int m, int home)
    {
        Span<int> near = stackalloc int[64];
        int k = 0;
        near[k++] = home;
        for (int i = 0; i < k && k < near.Length; i++)
        {
            if (Steps(w, home, near[i]) >= Spread) continue;
            foreach (int q in w.Adj[near[i]])
            {
                if (w.PLand[q] != 1 || near[..k].Contains(q)) continue;
                near[k++] = q;
                if (k == near.Length) break;
            }
        }
        return near[SimRng.Pick(w.Seed, 171, viewer * 256 + m, home, k)];
    }

    /// <summary>Land steps from a to b when b is a neighbour of a's neighbours (0, 1 or 2; the spread is tiny).</summary>
    static int Steps(WorldData w, int a, int b)
    {
        if (a == b) return 0;
        return Array.IndexOf(w.Adj[a], b) >= 0 ? 1 : 2;
    }

    // ------------------------------------------------------------------ what is told (Russian)

    static readonly string[] Ancient =
    {
        "Ночью {0} видели дым чужих костров",
        "Охотники нашли {0} чужие стрелы",
        "Старики говорят: {0} живёт другой род",
    };

    static readonly string[] Later =
    {
        "Купцы рассказывают: {0} живёт незнакомый народ",
        "Странники видели {0} чужие города",
        "Пастухи встретили {0} людей в чужих одеждах",
    };

    /// <summary>The line of a rumor as the viewer hears it: «Ночью на северо-западе видели дым чужих костров».</summary>
    public static string Text(WorldData w, GameState s, int viewer, in Rumor r)
    {
        var lines = s.Nat[viewer].Era == 0 ? Ancient : Later;
        return string.Format(lines[r.Line % lines.Length], Where(w, Home(s, viewer), r.Province));
    }

    static readonly string[] Compass = { "на востоке", "на юго-востоке", "на юге", "на юго-западе", "на западе", "на северо-западе", "на севере", "на северо-востоке" };

    /// <summary>«на северо-западе»: the direction from a to b on the wrapped map (y grows southwards).</summary>
    public static string Where(WorldData w, int a, int b)
    {
        if (a < 0) return "неподалёку";
        long dx = w.PCX[b] - w.PCX[a];
        if (dx > w.W / 2) dx -= w.W; else if (dx < -w.W / 2) dx += w.W;
        long dy = w.PCY[b] - w.PCY[a];
        if (dx == 0 && dy == 0) return "неподалёку";
        // octant by integer comparisons (414/1000 ≈ the tangent of 22½°): a flat |dy| is east/west, a flat |dx| north/south
        long ax = Math.Abs(dx), ay = Math.Abs(dy);
        int o;
        if (ay * 1000 <= ax * 414) o = dx > 0 ? 0 : 4;
        else if (ax * 1000 <= ay * 414) o = dy > 0 ? 2 : 6;
        else o = dx > 0 ? (dy > 0 ? 1 : 7) : (dy > 0 ? 3 : 5);
        return Compass[o];
    }
}
