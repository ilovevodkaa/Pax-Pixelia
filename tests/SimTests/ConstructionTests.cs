using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Tests;

/// <summary>Construction takes time: an order is paid at once and stands after its cycles; jobs side by side cost more
/// and share the builders; a strike stops them, a lost province drops them, a call-off pays half back; jobs in the save
/// and the hash.</summary>
public static class ConstructionTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("construction: a building goes up over time");
        var s = Fresh(w);
        var nat = s.Nat[Me];
        nat.Treasury = 100_000 * Rules.Cents; nat.Materials = 10_000;
        var (p, B) = Site(w, s);
        if (!Check(p >= 0, "something to build somewhere")) return;
        int built = s.Buildings[p].Count;
        long g0 = nat.Treasury;
        var rec = new Recorder();
        Check(Commands.Apply(w, s, Cmd.Build(Me, p, B), rec) == 0, $"«{Data.BldName[(int)B]}» is ordered");
        var job = Construction.Find(s, p, B);
        Check(job != null && !s.Buildings[p].Contains(B) && s.Buildings[p].Count == built && g0 - nat.Treasury == job.Gold && job.Gold > 0,
            $"paid at once ({job?.Gold / Rules.Cents} gold), not standing yet");
        Check(Construction.Occupied(s, p) == built + 1, "the job takes a plot");
        Check(Rules.CheckBuild(w, s, p, B, Me) == BuildError.AlreadyBuilt, "the same building cannot be ordered twice");
        Check(job.Era == nat.Era && job.Total == Construction.Cycles(B, s.Pace) * Construction.Work, $"{Construction.Cycles(B, s.Pace)} cycles of work, the era {job.Era} remembered");
        int cycles = RunUntil(w, s, () => s.Buildings[p].Contains(B), rec);
        Check(cycles == Construction.Cycles(B, s.Pace) && !Construction.Has(s, p, B) && s.Builds.Count(j => j.Province == p) == 0,
            $"it stands after {cycles} cycles, the job is gone");
        Check(s.Buildings[p].Count(b => b == B) == 1, "and stands once");
        Check(rec.Notes.Any(n => n.text.Contains("Стройка окончена") && n.text.Contains(w.PName[p])), "the chronicle says it is done");

        Section("construction: side by side is dearer and slower");
        var s2 = Fresh(w);
        s2.Nat[Me].Treasury = 100_000 * Rules.Cents; s2.Nat[Me].Materials = 10_000;
        int q = Enumerable.Range(0, w.P).FirstOrDefault(x => s2.Owner[x] == Me && Rules.BuildOptions(w, s2, x, Me).Count >= 2 && s2.Slots[x] - s2.Buildings[x].Count >= 2, -1);
        if (q >= 0)
        {
            var o = Rules.BuildOptions(w, s2, q, Me);
            Bld a = o[0], b = o[1];
            int pa = Rules.BuildPrice(a, s2.Nat[Me]), pb = Rules.BuildPrice(b, s2.Nat[Me]);
            Check(Rules.BuildPriceAt(s2, q, a, Me) == pa, "the first job pays the plain price");
            Commands.Apply(w, s2, Cmd.Build(Me, q, a), null);
            Check(Rules.BuildPriceAt(s2, q, b, Me) == pb * (100 + Construction.ParallelPct) / 100, $"the second pays {Construction.ParallelPct}% more");
            Commands.Apply(w, s2, Cmd.Build(Me, q, b), null);
            var ja = Construction.Find(s2, q, a); var jb = Construction.Find(s2, q, b);
            RunCycles(w, s2, 3);
            Check(ja.Work == 3 * Construction.RateOf(2) && jb.Work == 3 * Construction.RateOf(2) && Construction.RateOf(2) == Construction.Work * 2 / 3,
                $"two jobs share the builders: {Construction.RateOf(2)} work each a cycle instead of {Construction.Work}");
            Check(Construction.RateOf(3) == Construction.Work / 2, "three get half each");
            long t0 = s2.Nat[Me].Treasury; long m0 = s2.Nat[Me].Materials;
            long back = jb.Gold * Construction.RefundPct / 100; int mback = jb.Materials * Construction.RefundPct / 100;
            Check(Commands.Apply(w, s2, Cmd.CancelBuild(Me, q, b), null) == 0 && !Construction.Has(s2, q, b), "a job is called off");
            Check(s2.Nat[Me].Treasury - t0 == back && s2.Nat[Me].Materials - m0 == mback, $"half of what it cost comes back ({back / Rules.Cents} gold, {mback} materials)");
            Check(Commands.Apply(w, s2, Cmd.CancelBuild(Me, q, b), null) == (int)CancelBuildError.NoJob, "nothing left to call off");
            Check(Commands.Apply(w, s2, Cmd.CancelBuild(Me + 1, q, a), null) == (int)CancelBuildError.NoJob, "another nation cannot call off my job");
            Check(Commands.Apply(w, s2, Cmd.CancelBuild(Me, -3, a), null) == (int)CancelBuildError.NoJob, "a bad province is refused, not thrown");
            int w0 = ja.Work;
            RunCycles(w, s2, 1);
            Check(ja.Work == w0 + Construction.Work, "alone again, the first job goes at full speed");
        }
        else Check(true, "no province with two free plots (skip)");

        Section("construction: strikes and lost land");
        var s3 = Fresh(w);
        s3.Nat[Me].Treasury = 100_000 * Rules.Cents; s3.Nat[Me].Materials = 10_000;
        var (r, rb) = Site(w, s3);
        if (r >= 0)
        {
            Commands.Apply(w, s3, Cmd.Build(Me, r, rb), null);
            var j = Construction.Find(s3, r, rb);
            RunCycles(w, s3, 2);
            int w1 = j.Work;
            s3.Mood[r] = 0;   // a strike: the builders put their tools down
            RunCycles(w, s3, 1);
            Check(!Unrest.Works(s3, r) && j.Work == w1, $"a strike stops the work ({w1} → {j.Work})");
            s3.Mood[r] = 70;
            s3.Owner[r] = -1;
            RunCycles(w, s3, 1);
            Check(!Construction.Has(s3, r, rb) && s3.Builds.Count == 0, "the province lost: the job is gone");
        }

        Section("construction: the save and the hash");
        var s4 = Fresh(w);
        s4.Nat[Me].Treasury = 100_000 * Rules.Cents; s4.Nat[Me].Materials = 10_000;
        var blank = s4.Hash();
        var (u, ub) = Site(w, s4);
        if (u >= 0)
        {
            Commands.Apply(w, s4, Cmd.Build(Me, u, ub), null);
            Check(s4.Hash().Buildings != blank.Buildings, "a job is part of the hash");
            RunCycles(w, s4, 5);
            var back4 = SaveFile.Restore(SaveFile.Snapshot(s4), w, TestContent.Db, 100);
            var jj = Construction.Find(back4, u, ub);
            Check(jj != null && back4.Hash().Buildings == s4.Hash().Buildings && jj.Work == 5 * Construction.Work && jj.Era == s4.Nat[Me].Era && jj.Gold > 0,
                $"a job under way survives a save and load ({jj?.Work} of {jj?.Total})");
        }
        var empty = Fresh(w);
        Check(Construction.IsBlank(empty) && SaveFile.Restore(SaveFile.Snapshot(empty), w, TestContent.Db, 100).Builds.Count == 0, "nothing going up: an empty list");

        Section("construction: the journal");
        var c = Cmd.CancelBuild(Me, 77, Bld.Market);
        Check(Cmd.Parse(c.ToLine()) == c, $"«{c.ToLine()}» round-trips");
    }

    static (int p, Bld b) Site(WorldData w, GameState s)
    {
        for (int p = 0; p < w.P; p++)
            if (s.Owner[p] == Me && Rules.BuildOptions(w, s, p, Me) is { Count: > 0 } o) return (p, o[0]);
        return (-1, default);
    }

    static int RunUntil(WorldData w, GameState s, System.Func<bool> done, ISimSink sink)
    {
        int cycles = 0;
        for (int k = 0; k < 20_000 && !done(); k++)
        {
            if (Clock.IsCycleTick(s.Tick)) cycles++;
            Simulation.Step(w, s, sink);
        }
        return cycles;
    }

    static void RunCycles(WorldData w, GameState s, int n)
    {
        for (int done = 0; done < n;)
        {
            if (Clock.IsCycleTick(s.Tick)) done++;
            Simulation.Step(w, s, null);
        }
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        Techs.GrantBefore(s.Nat[Me], 1);   // every first-era building open
        return s;
    }
}
