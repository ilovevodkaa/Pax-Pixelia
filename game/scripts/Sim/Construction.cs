using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

public enum CancelBuildError { None, NoJob }

/// <summary>
/// Buildings take time (IDEAS E-7, ART_BIBLE §10.2). An ordered building is paid at once and goes up as a job in
/// GameState.Builds for <see cref="Cycles"/> rules cycles (scaled by the game's pace); only then does it join the
/// province's Buildings and start to work. A province may have several jobs at once, but each one already under way
/// makes the next <see cref="ParallelPct"/>% dearer, and they share the builders: with k jobs each gets 2/(k+1) of a
/// lone job's work (⅔ each for two, ½ for three…), so building side by side costs more and is slower. A strike stops the
/// work; a province that changes hands loses its jobs; a job called off gives half of what was paid back. The era a job
/// was ordered in picks its scaffold on the map (Map/SiteOverlay).
/// </summary>
public static class Construction
{
    /// <summary>Rules cycles a lone job takes at «Обычная», by Bld (farm, lumber, quarry, fishery, pasture, shrine, market,
    /// granary): 30 s to a minute at speed 3.</summary>
    static readonly int[] BaseCycles = { 80, 60, 80, 80, 60, 120, 120, 100 };

    /// <summary>Work a lone job gets a cycle.</summary>
    public const int Work = 1000;
    public const int ParallelPct = 50, RefundPct = 50;

    public static int Cycles(Bld b, int pace) => Math.Max(1, BaseCycles[(int)b] * Eras.ClampPace(pace) / 1000);

    public static int JobsIn(GameState s, int p)
    {
        int k = 0;
        foreach (var j in s.Builds) if (j.Province == p) k++;
        return k;
    }

    public static GameState.BuildJob Find(GameState s, int p, Bld b)
    {
        foreach (var j in s.Builds) if (j.Province == p && j.Building == b) return j;
        return null;
    }

    public static bool Has(GameState s, int p, Bld b) => Find(s, p, b) != null;

    /// <summary>Plots taken in p: standing buildings and jobs.</summary>
    public static int Occupied(GameState s, int p) => s.Buildings[p].Count + JobsIn(s, p);

    /// <summary>How much dearer (%) a new job in p is because of the jobs already under way there.</summary>
    public static int SurchargePct(GameState s, int p) => ParallelPct * JobsIn(s, p);

    /// <summary>Work each of k jobs in one province gets a cycle.</summary>
    public static int RateOf(int k) => k <= 1 ? Work : Work * 2 / (k + 1);

    public static int PermilleDone(GameState.BuildJob j) => (int)Math.Clamp((long)j.Work * 1000 / Math.Max(1, j.Total), 0, 1000);

    /// <summary>Rules cycles job j still needs at the province's current sharing (strikes aside).</summary>
    public static int CyclesLeft(GameState s, GameState.BuildJob j)
    {
        int rate = RateOf(JobsIn(s, j.Province));
        return Math.Max(0, (j.Total - j.Work + rate - 1) / rate);
    }

    /// <summary>Cycles a new job of b in p would take, counting the jobs already there.</summary>
    public static int CyclesFor(GameState s, int p, Bld b) =>
        (int)(((long)Cycles(b, s.Pace) * Work + RateOf(JobsIn(s, p) + 1) - 1) / RateOf(JobsIn(s, p) + 1));

    /// <summary>Lay the foundation: the price is already taken by Rules.Build.</summary>
    internal static void Start(GameState s, int p, Bld b, int n, long gold, int materials) =>
        s.Builds.Add(new GameState.BuildJob
        {
            Province = p, Building = b, Nation = n, Era = s.Nat[n].Era,
            Total = Cycles(b, s.Pace) * Work, Gold = gold, Materials = materials,
        });

    public static CancelBuildError CheckCancel(GameState s, int p, Bld b, int n)
    {
        var j = (uint)p < (uint)s.Owner.Length ? Find(s, p, b) : null;
        return j == null || j.Nation != n ? CancelBuildError.NoJob : CancelBuildError.None;
    }

    /// <summary>Call off nation n's job of b in p: half of what was paid comes back.</summary>
    internal static void Cancel(GameState s, int p, Bld b, int n)
    {
        var j = Find(s, p, b);
        s.Builds.Remove(j);
        s.Nat[n].Treasury += j.Gold * RefundPct / 100;
        s.Nat[n].Materials += j.Materials * RefundPct / 100;
    }

    /// <summary>A rules cycle of building: every job not on strike gets its share of work; finished ones stand.</summary>
    internal static void Cycle(WorldData w, GameState s, ISimSink sink, ref List<int> changed)
    {
        var list = s.Builds;
        if (list.Count == 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            var j = list[i];
            if (s.Owner[j.Province] == j.Nation) continue;
            list.RemoveAt(i--);   // the land changed hands: the builders went home
            (changed ??= new List<int>()).Add(j.Province);
        }
        // the shares are set before anything finishes, so a job's speed this cycle does not depend on the list order
        Span<int> rate = list.Count <= 256 ? stackalloc int[list.Count] : new int[list.Count];
        for (int i = 0; i < list.Count; i++) rate[i] = Unrest.Works(s, list[i].Province) ? RateOf(JobsIn(s, list[i].Province)) : 0;
        int done = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var j = list[i];
            j.Work = (int)Math.Min(j.Total, (long)j.Work + rate[i]);
            if (j.Work < j.Total) continue;
            s.Buildings[j.Province].Add(j.Building);
            (changed ??= new List<int>()).Add(j.Province);
            done++;
            if (s.Nat[j.Nation].Human) sink?.Notify("hammer", $"Стройка окончена: «{Data.BldName[(int)j.Building]}» в провинции {w.PName[j.Province]}");
        }
        if (done > 0) list.RemoveAll(j => j.Work >= j.Total);
    }

    /// <summary>Nothing under way: the snapshot block and the hash are as before construction existed.</summary>
    public static bool IsBlank(GameState s) => s.Builds.Count == 0;
}
