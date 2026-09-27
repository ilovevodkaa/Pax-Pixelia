using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

public enum ScoutError { None, Max, Sea, Here, Far, NoTargets }

/// <summary>What one Scouts.Advance call did (the UI refreshes its scout box only when something happened).</summary>
public readonly record struct ScoutTick(int Steps, int Finished)
{
    public bool Any => Steps > 0 || Finished > 0;
}

/// <summary>
/// Scouts (pure C#, port of the scout half of docs/mockups/js/fog.js). Up to Max parties walk BFS paths over land
/// from the capital, revealing ScoutRange around the province they stand in; they vanish on arrival («вернулись с картами»).
/// Auto parties keep re-targeting the nearest unexplored land, away from the other party, for AutoSteps provinces
/// after reaching the first frontier (the march there is free, so auto scouting stays useful once the frontier is far).
/// Movement is counted in integer sub-steps, SubStepsPerYear of game time, so scouting costs the same game time at every
/// speed, and a run depends only on the number of sub-steps, never on how they were batched (lockstep-friendly).
/// </summary>
public static class Scouts
{
    public const int Max = 2, AutoSteps = 36, SubSteps = 40;
    /// <summary>Sub-steps per game year: SubSteps / 64 ≈ 0.6 years per province, the mockup's 600 ms at speed 2.</summary>
    public const int SubStepsPerYear = 64;

    public static int Capital(GameState s) => s.NationCapital != null && s.NationCapital.Length > GameState.LocalPlayer ? s.NationCapital[GameState.LocalPlayer] : -1;

    public static int Target(GameState.Scout sc) => sc.Path[^1];
    public static int Current(GameState.Scout sc) => sc.Path[Math.Clamp(sc.Step, 0, sc.Path.Length - 1)];
    /// <summary>Provinces left to walk on the current path.</summary>
    public static int Left(GameState.Scout sc) => Math.Max(0, sc.Path.Length - 1 - sc.Step);

    /// <summary>Why a party cannot go to target (-1 = auto) right now, without sending it.</summary>
    public static ScoutError Check(WorldData w, GameState s, int target, out int[] path)
    {
        path = null;
        if (s.Scouts.Count >= Max) return ScoutError.Max;
        int cap = Capital(s);
        if (cap < 0) return ScoutError.Far;
        if (target < 0)
        {
            path = AutoPath(w, s, cap, null);
            return path == null || path.Length < 2 ? ScoutError.NoTargets : ScoutError.None;
        }
        if (w.PLand[target] != 1) return ScoutError.Sea;
        if (target == cap) return ScoutError.Here;
        var bfs = SimScratch.For(w, s).A;
        bfs.RunLand(w, stackalloc int[] { cap });
        path = bfs.Trace(target);
        return path == null ? ScoutError.Far : ScoutError.None;
    }

    /// <summary>Send a party from the capital to target (-1 = auto). On success the new scout is returned.</summary>
    public static ScoutError Send(WorldData w, GameState s, int target, ISimSink sink, out GameState.Scout scout)
    {
        scout = null;
        var err = Check(w, s, target, out var path);
        if (err != ScoutError.None) return err;
        scout = new GameState.Scout
        {
            Id = ++s.ScoutSeq, Path = path, Auto = target < 0,
            MaxSteps = target < 0 ? path.Length - 1 + AutoSteps : int.MaxValue,
        };
        s.Scouts.Add(scout);
        scout.Found += FogOfWar.Refresh(w, s, sink);
        return ScoutError.None;
    }

    /// <summary>
    /// Move every party by n sub-steps, in time order: each sub-step moves all parties before the next one starts, so the
    /// result is the same whether n arrives in one call or n calls. Fog changes of the batch are raised as one FogChanged.
    /// </summary>
    public static ScoutTick Advance(WorldData w, GameState s, int n, ISimSink sink)
    {
        if (n <= 0 || s.Scouts.Count == 0) return default;
        var batch = new FogBatch(w, s);
        int steps = 0, finished = 0;
        for (int k = 0; k < n && s.Scouts.Count > 0; k++)
            for (int i = 0; i < s.Scouts.Count; i++)
            {
                var sc = s.Scouts[i];
                bool done = sc.Step >= sc.Path.Length - 1;          // a degenerate path: the party goes home at once
                if (!done && ++sc.Sub >= SubSteps)
                {
                    sc.Sub = 0; sc.Step++; sc.Steps++; steps++;
                    sc.Found += batch.Add(FogOfWar.Recompute(w, s), sink);
                    if (sc.Auto)
                    {
                        if (sc.Steps >= sc.MaxSteps) done = true;
                        else if (sc.Step >= sc.Path.Length - 1 || s.Explored[Target(sc)])
                        {
                            var np = AutoPath(w, s, Current(sc), sc);
                            if (np == null || np.Length < 2) done = true;
                            else { sc.Path = np; sc.Step = 0; }
                        }
                    }
                    else done = sc.Step >= sc.Path.Length - 1;      // arrived
                }
                if (!done) continue;
                s.Scouts.RemoveAt(i--); finished++;
                batch.Add(FogOfWar.Recompute(w, s), sink);   // its vision leaves with it
                sink?.Notify("map-2", ReturnText(w, sc));
            }
        foreach (var sc in s.Scouts) sc.Progress = sc.Sub / (float)SubSteps;
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
    /// Path to the nearest unexplored land province reachable over land from `from`, pushed away from the other
    /// parties' positions and destinations (score = distance − 0.75·min(8, distance to the others)). Null if none.
    /// </summary>
    public static int[] AutoPath(WorldData w, GameState s, int from, GameState.Scout self)
    {
        var sc = SimScratch.For(w, s);
        var A = sc.A;
        A.RunLand(w, stackalloc int[] { from });

        Span<int> others = stackalloc int[2 * Max];
        int no = 0;
        foreach (var o in s.Scouts)
            if (o != self && no + 2 <= others.Length) { others[no++] = Current(o); others[no++] = Target(o); }
        SimScratch.Bfs B = null;
        if (no > 0) { B = sc.B; B.RunLand(w, others[..no]); }

        int best = -1; double bs = double.MaxValue;
        for (int k = 0; k < A.Count; k++)
        {
            int p = A.Queue[k];
            if (s.Explored[p]) continue;
            int od = B == null || B.Dist[p] < 0 ? 8 : Math.Min(8, B.Dist[p]);
            double score = A.Dist[p] - .75 * od;
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

        public int Add(in FogDelta d, ISimSink sink)
        {
            foreach (int p in d.Changed)
                if (_sc.Seen[p] != _stamp) { _sc.Seen[p] = _stamp; (_changed ??= new List<int>()).Add(p); }
            FogOfWar.NotifyMet(d, sink);
            return d.NewlyExplored;
        }

        public void Flush(ISimSink sink)
        {
            if (_changed != null) sink?.RaiseFogChanged(_changed.ToArray());
        }
    }
}
