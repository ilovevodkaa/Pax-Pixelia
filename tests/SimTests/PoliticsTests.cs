using System.Linq;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>The policy tree: «Основы государства» first, one course at a time over its cycles, gold and a lasting bonus
/// when done, the eight ways of the first ring shutting the three facing them, the compass position, the save and the
/// hash, bots taking courses of their own.</summary>
public static class PoliticsTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("politics: the centre and the first ring");
        Check(Politics.All[Politics.Root].Ring == 0 && Politics.All.Count(c => c.Ring == 1) == 8
              && Enumerable.Range(0, 8).All(d => Politics.All.Count(c => c.Ring == 1 && c.Dir == d) == 1), "the centre and one course for each of the eight ways");
        Check(Politics.All.Select(c => c.Id).Distinct().Count() == Politics.Count && Politics.All.All(c => c.Quote.Length > 0 && c.Effect.Length > 0), "ids are unique, every course has its quote and effect");
        Check(Politics.All.Where(c => c.Ring == 1).All(c => (c.X, c.Y) == Politics.Ways[c.Dir]), "each course of the ring leans its own way on the compass");

        var s = Fresh(w);
        var nat = s.Nat[Me];
        int one = Politics.Index("one_rule"), assembly = Politics.Index("assembly"), noble = Politics.Index("noble_kin"), equals = Politics.Index("circle_of_equals"), commons = Politics.Index("common_land");
        Check(Commands.Apply(w, s, Cmd.Course(Me, one, true), null) == (int)CourseError.Locked, "the ring waits for «Основы государства»");
        Check(Commands.Apply(w, s, Cmd.Course(Me, 99, true), null) == (int)CourseError.Unknown && Commands.Apply(w, s, Cmd.Course(Me, -2, true), null) == (int)CourseError.Unknown,
            "an unknown course is refused, not thrown");
        Check(Commands.Apply(w, s, Cmd.Course(Me, Politics.Root, false), null) == (int)CourseError.NotAdopting, "nothing to drop yet");
        Check(Commands.Apply(w, s, Cmd.Course(Me, Politics.Root, true), null) == 0 && nat.CourseNow == Politics.Root, "«Основы государства» are being laid");
        Check(Commands.Apply(w, s, Cmd.Course(Me, Politics.Root, true), null) == (int)CourseError.Busy, "one course at a time");
        int admin0 = Techs.Sum(nat, TechFx.AdminLimit);
        int cycles = RunUntil(w, s, () => nat.CourseCycles == Politics.Total(Politics.Root, s.Pace) - 1);
        long gold0 = nat.Treasury;
        RunCycles(w, s, 1);
        Check(Politics.Has(nat, Politics.Root) && cycles + 1 == Politics.Total(Politics.Root, s.Pace) && nat.CourseNow < 0, $"adopted after {cycles + 1} cycles");
        Check(Techs.Sum(nat, TechFx.AdminLimit) == admin0 + 2, "its bonus counts with the technologies' (the administration limit +2)");
        Check(nat.Treasury - gold0 == nat.LastTaxes - nat.LastUpkeep + Politics.GoldFor(Politics.Root, nat) * Rules.Cents,
            $"and it pays {Politics.GoldFor(Politics.Root, nat)} gold on top of the cycle's budget");
        Check(Commands.Apply(w, s, Cmd.Course(Me, Politics.Root, true), null) == (int)CourseError.Done, "a course is adopted once");

        Section("politics: a way shuts the ways facing it");
        Check(Commands.Apply(w, s, Cmd.Course(Me, one, true), null) == 0, "«Единоначалие» is under way");
        Check(Politics.Shut(nat, assembly) && Politics.Shut(nat, Politics.Index("free_trade")) && Politics.Shut(nat, equals),
            "while it is under way, «Вече» and the two beside it are shut");
        Check(Commands.Apply(w, s, Cmd.Course(Me, one, false), null) == 0 && nat.CourseNow < 0 && !Politics.Shut(nat, assembly), "dropped: the other ways open again");
        Check(Commands.Apply(w, s, Cmd.Course(Me, one, true), null) == 0, "taken up again (the progress is lost)");
        RunUntil(w, s, () => Politics.Has(nat, one));
        Check(Commands.Apply(w, s, Cmd.Course(Me, assembly, true), null) == (int)CourseError.Closed, "«Вече» is closed for good to a realm of one rule");
        Check(Commands.Apply(w, s, Cmd.Course(Me, noble, true), null) == 0, "«Знать и род», next to it, is open");
        RunUntil(w, s, () => Politics.Has(nat, noble));
        Check(Politics.Position(nat) == (1, 2), $"on the compass: {Politics.Position(nat)} (one step right, two up)");
        Check(Politics.Shut(nat, commons), "«Общая земля», facing «Знать и род», is shut too");

        Section("politics: the save and the hash");
        var blank = Fresh(w);
        Check(Politics.IsBlank(blank.Nat[Me]) && blank.Hash().Nations == Fresh(w).Hash().Nations, "nothing adopted hashes as before");
        Commands.Apply(w, s, Cmd.Course(Me, Politics.Index("family_plot"), true), null);
        RunCycles(w, s, 7);
        var back = SaveFile.Restore(SaveFile.Snapshot(s), w, null, 100);   // no event deck on either side: the hashes compare
        var bn = back.Nat[Me];
        Check(Politics.Has(bn, Politics.Root) && Politics.Has(bn, one) && Politics.Has(bn, noble) && bn.CourseNow == Politics.Index("family_plot") && bn.CourseCycles == 7,
            "the courses and the one under way survive a save");
        Check(back.Hash().Nations == s.Hash().Nations && Techs.Sum(bn, TechFx.TaxPermille) == Techs.Sum(nat, TechFx.TaxPermille), "same hash, same bonuses after the load");
        Check(Fresh(w).Hash().Nations != s.Hash().Nations, "the hash sees the courses");

        Section("politics: a wandering tribe has no state yet");
        var t = NationGen.CreateInitialState(w);
        t.Nat[Me].Control = NationControl.Human;
        Nomads.Start(w, t);
        Simulation.Begin(w, t);
        Check(Commands.Apply(w, t, Cmd.Course(Me, Politics.Root, true), null) == (int)CourseError.NoCapital, "no «Основы государства» before the capital");

        Section("politics: bots take courses of their own");
        var b = NationGen.CreateInitialState(w);
        foreach (var x in b.Nat) x.Control = NationControl.Bot;
        Simulation.Begin(w, b);
        RunCycles(w, b, 1500);
        int based = b.Nat.Count(x => Politics.Has(x, Politics.Root));
        var ways = b.Nat.SelectMany(x => Enumerable.Range(0, Politics.Count).Where(c => c != Politics.Root && Politics.Has(x, c))).Distinct().Count();
        Check(based >= b.Nat.Length / 2, $"after 1500 cycles {based} of {b.Nat.Length} bots laid their foundations");
        Check(ways >= 3, $"and they lean different ways ({ways} courses of the ring taken)");
        Check(b.Nat.All(x => Enumerable.Range(0, Politics.Count).Where(c => Politics.Has(x, c)).All(c => !Politics.Shut(x, c))), "no bot holds two facing courses");

        Section("politics: the journal");
        var cmd = Cmd.Course(Me, 3, true);
        Check(Cmd.Parse(cmd.ToLine()) == cmd, $"«{cmd.ToLine()}» round-trips");
    }

    static int RunUntil(WorldData w, GameState s, System.Func<bool> done)
    {
        int cycles = 0;
        for (int k = 0; k < 40_000 && !done(); k++)
        {
            if (Clock.IsCycleTick(s.Tick)) cycles++;
            Simulation.Step(w, s, null);
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
        return s;
    }
}
