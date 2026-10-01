using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Tests;

/// <summary>«Последствия общие для всех»: mills along a river flood everything downstream of the first of them — not
/// the land above it — whoever built them; crowded pastures wear out the steppe around them; both cost fertility on
/// top of the climate and floods sour the mood.</summary>
public static class CommonsTests
{
    public static void Run(WorldData w)
    {
        Section("commons: mills upstream flood the land downstream");
        var s = NationGen.CreateInitialState(w);
        Simulation.Begin(w, s);
        foreach (var l in s.Buildings) l.Clear();   // a clean slate: only the mills and pastures of this test
        // the longest river course and the land that touches it
        int[] course = null;
        for (int r = 0; r < w.Rivers.Count; r++)
        {
            var c = Course(w, r);
            if (course == null || c.Length > course.Length) course = c;
        }
        Check(course != null && course.Length >= 6, $"a river through {course?.Length} provinces");
        Check(Commons.Hits(w, s).All(h => h.Total == 0), "no mills, no herds: the land is whole");

        int head = course.Length / 3;
        for (int i = head; i < head + 4; i++) s.Buildings[course[i]].Add(Bld.Lumber);   // four mills by the upper river
        var hits = Commons.Hits(w, s);
        Check(Enumerable.Range(0, head).All(i => hits[course[i]].Flood == 0), "above the first mill nothing floods");
        int low = course[^1];
        Check(hits[low].Flood == (4 - Commons.MillsFree) * Commons.FloodPerMill && hits[low].Mills == 4,
            $"at the mouth: 4 mills upstream → −{hits[low].Flood / 10} % fertility");
        var climate = Climate.FertNow(w, s);
        var land = Commons.FertNow(w, s);
        Check(land[low] < climate[low] || climate[low] == 0, $"the flood is taken off the climate's fertility ({climate[low]} → {land[low]} ‰)");
        Check(Commons.FloodMood(hits[low]) == hits[low].Flood / 75 && Commons.Describe(hits[low]).StartsWith("Паводки"), $"«{Commons.Describe(hits[low])}»");
        for (int i = head + 4; i < course.Length - 1; i++) s.Buildings[course[i]].Add(Bld.Lumber);
        Check(Commons.Hits(w, s)[low].Flood == Commons.FloodMax, "more mills: the flood stops at its cap");

        Section("commons: crowded pastures wear the steppe out");
        foreach (var l in s.Buildings) l.Clear();
        int steppe = Enumerable.Range(0, w.P).First(p => w.PLand[p] == 1 && w.PBiome[p] is 11 or 13
            && w.Adj[p].Count(q => w.PLand[q] == 1) >= 4);
        var around = w.Adj[steppe].Where(q => w.PLand[q] == 1).Take(3).ToArray();
        s.Buildings[steppe].Add(Bld.Pasture);
        foreach (int q in around) s.Buildings[q].Add(Bld.Pasture);
        var g = Commons.Hits(w, s)[steppe];
        Check(g.Pastures == 4 && g.Grazing >= (4 - Commons.PasturesFree) * Commons.GrazePerPasture, $"four pastures in and around {w.PName[steppe]}: −{g.Grazing / 10} %");
        s.Buildings[around[0]].Clear(); s.Buildings[around[1]].Clear();
        Check(Commons.Hits(w, s)[steppe].Grazing == 0, "two pastures are not too many");
    }

    /// <summary>The land provinces a river passes, head first (the same walk Commons makes).</summary>
    static int[] Course(WorldData w, int r)
    {
        var path = w.Rivers[r];
        var list = new System.Collections.Generic.List<int>();
        for (int k = 0; k < path.Xs.Length; k++)
        {
            int x = ((int)System.MathF.Floor(path.Xs[k]) % w.W + w.W) % w.W, y = System.Math.Clamp((int)System.MathF.Floor(path.Ys[k]), 0, w.H - 1);
            int p = w.Prov[y * w.W + x];
            if (w.PLand[p] == 1 && !list.Contains(p)) list.Add(p);
        }
        return list.ToArray();
    }
}
