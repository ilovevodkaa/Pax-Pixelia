using System;
using System.Collections.Generic;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

public enum ScoutError { None, Max, Sea, Here, Far, NoTargets }

/// <summary>What one tick of scout movement did (the UI refreshes its scout box only when something happened).</summary>
public readonly record struct ScoutTick(int Steps, int Finished)
{
    public bool Any => Steps > 0 || Finished > 0;
}

/// <summary>
/// Scouts (pure C#, port of the scout half of docs/mockups/js/fog.js). Every nation with a fog map may have up to Max
/// parties walking BFS paths over land from its capital, revealing ScoutRange around the province they stand in;
/// they vanish on arrival («вернулись с картами»). Auto parties keep re-targeting the nearest unexplored land, away
/// from the nation's other party, for AutoSteps provinces after reaching the first frontier.
/// Movement is SubPerTick integer sub-steps a tick (SubSteps to a province: 2.5 ticks, 0.31 s at speed 3), so a walk
/// costs the same game time at every speed and depends only on the tick count, never on frame batching.
/// </summary>
public static class Scouts
{
    public const int Max = 2, AutoSteps = 36, SubSteps = 40, SubPerTick = 16;

    public static int Capital(GameState s, int n) => s.NationCapital != null && (uint)n < (uint)s.NationCapital.Length ? s.NationCapital[n] : -1;

    public static int Target(GameState.Scout sc) => sc.Path[^1];
    public static int Current(GameState.Scout sc) => sc.Path[Math.Clamp(sc.Step, 0, sc.Path.Length - 1)];
    /// <summary>Provinces left to walk on the current path.</summary>
    public static int Left(GameState.Scout sc) => Math.Max(0, sc.Path.Length - 1 - sc.Step);

    public static int Count(GameState s, int n)
    {
        int k = 0;
        foreach (var sc in s.Scouts) if (sc.Nation == n) k++;
        return k;
    }

    public static int Free(GameState s, int n) => Math.Max(0, Max - Count(s, n));

    /// <summary>Why nation n's party cannot go to target (-1 = auto) right now, without sending it.</summary>
    public static ScoutError Check(WorldData w, GameState s, int n, int target, out int[] path)
    {
        path = null;
        if (Count(s, n) >= Max) return ScoutError.Max;
        int cap = Capital(s, n);
        if (cap < 0) return ScoutError.Far;
        if (target < 0)
        {
            if (s.Nat[n].Fog == null) return ScoutError.NoTargets;   // a nation without a map has nothing to discover
            path = AutoPath(w, s, n, cap, null);
            return path == null || path.Length < 2 ? ScoutError.NoTargets : ScoutError.None;
        }
        if (target >= w.P || w.PLand[target] != 1) return ScoutError.Sea;
        if (target == cap) return ScoutError.Here;
        var bfs = SimScratch.For(w, s).A;
        bfs.RunLand(w, stackalloc int[] { cap });
        path = bfs.Trace(target);
        return path == null ? ScoutError.Far : ScoutError.None;
    }

    /// <summary>Send a party of nation n from its capital to target (-1 = auto). On success the new scout is returned.</summary>
    public static ScoutError Send(WorldData w, GameState s, int n, int target, ISimSink sink, out GameState.Scout scout)
    {
        scout = null;
        var err = Check(w, s, n, target, out var path);
        if (err != ScoutError.None) return err;
        scout = new GameState.Scout
        {
            Id = ++s.ScoutSeq, Nation = n, Path = path, Auto = target < 0,
            MaxSteps = target < 0 ? path.Length - 1 + AutoSteps : int.MaxValue,
        };
        s.Scouts.Add(scout);
        scout.Found += FogOfWar.RefreshNation(w, s, n, sink);
        return ScoutError.None;
    }

    /// <summary>
    /// Move every party by one tick, in list order. Fog changes of the tick are raised as one FogChanged.
    /// </summary>
    public static ScoutTick Tick(WorldData w, GameState s, ISimSink sink)
    {
        if (s.Scouts.Count == 0) return default;
        var batch = new FogBatch(w, s);
        int steps = 0, finished = 0;
        for (int i = 0; i < s.Scouts.Count; i++)
        {
            var sc = s.Scouts[i];
            bool done = sc.Step >= sc.Path.Length - 1;          // a degenerate path: the party goes home at once
            if (!done && (sc.Sub += SubPerTick) >= SubSteps)
            {
                sc.Sub -= SubSteps; sc.Step++; sc.Steps++; steps++;
                sc.Found += batch.Add(s, FogOfWar.Recompute(w, s, sc.Nation), sink);
                if (sc.Auto)
                {
                    if (sc.Steps >= sc.MaxSteps) done = true;
                    else if (sc.Step >= sc.Path.Length - 1 || s.Nat[sc.Nation].Fog.Explored[Target(sc)])
                    {
                        var np = AutoPath(w, s, sc.Nation, Current(sc), sc);
                        if (np == null || np.Length < 2) done = true;
                        else { sc.Path = np; sc.Step = 0; }
                    }
                }
                else done = sc.Step >= sc.Path.Length - 1;      // arrived
            }
            sc.Progress = sc.Sub / (float)SubSteps; // pax-allow: render-only
            if (!done) continue;
            s.Scouts.RemoveAt(i--); finished++;
            batch.Add(s, FogOfWar.Recompute(w, s, sc.Nation), sink);   // its vision leaves with it
            if (s.Nat[sc.Nation].Human) sink?.Notify("map-2", ReturnText(w, sc));
        }
        batch.Flush(sink);
        return new ScoutTick(steps, finished);
    }

    public static string ReturnText(WorldData w, GameState.Scout sc)
    {
        int f = sc.Found;
        if (sc.Auto)
            return f > 0 ? $"Разведчики исследовали {Ru.Count(f, "провинцию", "провинции", "провинций")} и вернулись с картами"
                         : "Разведчики вернулись: поблизости нет неизведанных земель";
        return $"Разведчики достигли провинции {w.PName[Target(sc)]} и вернулись с картами"
             + (f > 0 ? $" (+{Ru.Count(f, "провинция", "провинции", "провинций")} на карте)" : "");
    }

    /// <summary>
    /// Path to the nearest land province nation n has not explored, reachable over land from `from`, pushed away from
    /// its other parties' positions and destinations (score = 4·distance − 3·min(8, distance to the others)). Null if none.
    /// </summary>
    public static int[] AutoPath(WorldData w, GameState s, int n, int from, GameState.Scout self)
    {
        var explored = s.Nat[n].Fog.Explored;
        var sc = SimScratch.For(w, s);
        var A = sc.A;
        A.RunLand(w, stackalloc int[] { from });

        Span<int> others = stackalloc int[2 * Max];
        int no = 0;
        foreach (var o in s.Scouts)
            if (o != self && o.Nation == n && no + 2 <= others.Length) { others[no++] = Current(o); others[no++] = Target(o); }
        SimScratch.Bfs B = null;
        if (no > 0) { B = sc.B; B.RunLand(w, others[..no]); }

        int best = -1, bs = int.MaxValue;
        for (int k = 0; k < A.Count; k++)
        {
            int p = A.Queue[k];
            if (explored[p]) continue;
            int od = B == null || B.Dist[p] < 0 ? 8 : Math.Min(8, B.Dist[p]);
            int score = 4 * A.Dist[p] - 3 * od;
            if (score < bs) { bs = score; best = p; }
        }
        return best < 0 ? null : A.Trace(best);
    }

    /// <summary>Accumulates the fog deltas of several steps into one de-duplicated change list.</summary>
    struct FogBatch
    {
        readonly SimScratch _sc;
        readonly int _stamp;
        List<int> _changed;

        public FogBatch(WorldData w, GameState s) { _sc = SimScratch.For(w, s); _stamp = _sc.NextSeenStamp(); _changed = null; }

        public int Add(GameState s, in FogDelta d, ISimSink sink)
        {
            foreach (int p in d.Changed)
                if (_sc.Seen[p] != _stamp) { _sc.Seen[p] = _stamp; (_changed ??= new List<int>()).Add(p); }
            FogOfWar.NotifyMet(s, d, sink);
            return d.NewlyExplored;
        }

        public void Flush(ISimSink sink)
        {
            if (_changed != null) sink?.RaiseFogChanged(_changed.ToArray());
        }
    }
}
