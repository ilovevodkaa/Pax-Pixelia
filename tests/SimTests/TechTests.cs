using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Tests;

/// <summary>Technologies of the first era: nothing is known at the start, science waits in the pool until a choice,
/// a switch keeps the points, a learned technology opens its building, the era needs 4 of 6, bots study by themselves.</summary>
public static class TechTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("technologies: the start");
        var s = Fresh(w);
        var nat = s.Nat[Me];
        Check(nat.TechsDone == 0 && nat.Researching == -1 && nat.TechPts.Length == Techs.Count, $"a tribe knows none of the {Techs.Count} technologies");
        Check(Techs.CountIn(0) == 6 && Techs.Required(0) == 4, "Первобытная: 6 technologies, 4 needed for the next era");
        int plot = Own(w, s, p => s.Buildings[p].Count < s.Slots[p] && WorldFacts.Of(w).Allows(p, Bld.Farm) && !s.Buildings[p].Contains(Bld.Farm));
        if (plot >= 0) Check(Rules.CheckBuild(w, s, plot, Bld.Farm, Me) == BuildError.NeedTech, "a farm needs «Дикие злаки»");
        int dig = Own(w, s, p => !s.OreFound[p]);
        if (dig >= 0) Check(Rules.CheckSurvey(s, dig, Me) == SurveyError.NeedTech, "geologists need «Кремень»");
        Check(Rules.CheckBuild(w, s, System.Math.Max(0, plot), Bld.Market, Me) is BuildError.NeedTech or BuildError.NotAllowed or BuildError.NotOwned,
            "markets and granaries wait for Древний мир");

        Section("technologies: the pool, a choice, a switch");
        int rate = nat.ScienceRate;
        Cycles(w, s, 5);
        Check(nat.TechPool == 5L * rate && rate > 0, $"5 cycles with nothing chosen: {nat.TechPool} points wait in the pool");
        Check(Commands.Apply(w, s, Cmd.Research(Me, 0), null) == 0 && nat.Researching == 0 && nat.TechPts[0] == 5L * rate && nat.TechPool == 0,
            "choosing moves the pool into the technology");
        Cycles(w, s, 3);
        long a = nat.TechPts[0];
        Check(Commands.Apply(w, s, Cmd.Research(Me, 1), null) == 0 && nat.Researching == 1 && nat.TechPts[0] == a, "a switch keeps what was put in");
        Check(Commands.Apply(w, s, Cmd.Research(Me, 99), null) == Commands.BadCommand && Commands.Apply(w, s, Cmd.Research(Me, -1), null) == Commands.BadCommand,
            "an unknown technology id is refused, not thrown");
        Check(Commands.Apply(w, s, Cmd.Research(Me, 0), null) == 0, "and back");
        int guard = 0;
        while (!Techs.Known(nat, 0) && guard++ < 5000) Simulation.Step(w, s, null);
        Check(Techs.Known(nat, 0) && nat.Researching == -1 && nat.TechPts[0] == 0, $"«Дикие злаки» learned after {guard} ticks (≈ {guard / (double)Clock.TicksPerSecond[3]:0} s at speed 3)");
        Check(guard / (double)Clock.TicksPerSecond[3] is > 60 and < 400, "one technology takes minutes, not seconds or hours");
        if (plot >= 0) Check(Rules.CheckBuild(w, s, plot, Bld.Farm, Me) is not BuildError.NeedTech, "the farm is open now");
        Check(Commands.Apply(w, s, Cmd.Research(Me, 0), null) == Commands.BadCommand, "a known technology cannot be chosen again");

        Section("technologies: the era gate");
        var g = Fresh(w);
        var gn = g.Nat[Me];
        for (int t = 0; t < 3; t++) Techs.Learn(gn, t);
        gn.Progress = Eras.Threshold(1, g.Pace) + 10;
        Cycles(w, g, 1);
        Check(gn.Era == 0 && Techs.EraCap(gn) == 0, "enough science but 3 of 4 technologies: still Первобытная");
        Techs.Learn(gn, 3);
        Cycles(w, g, 1);
        Check(gn.Era == 1 && Techs.EraCap(gn) >= 1, "the 4th technology opens Древний мир");
        Check(Rules.CheckBuild(w, g, 0, Bld.Market, Me) is not BuildError.NeedTech, "markets open with the era");
        Check(Techs.Open(gn, 4) && Techs.Open(gn, 5), "the rest of the first era can still be studied later");

        Section("technologies: bots and the whole first era");
        var b = Fresh(w);
        Cycles(w, b, 1);
        var first = Enumerable.Range(1, b.Nat.Length - 1).Select(n => b.Nat[n].Researching).ToList();
        Check(first.All(t => t >= 0) && first.Distinct().Count() > 1, $"bots pick their first study themselves, and not all the same ({first.Distinct().Count()} different)");
        for (int k = 0; k < Clock.TicksFor(30 * 60); k++) Simulation.Step(w, b, null);
        var bots = Enumerable.Range(1, b.Nat.Length - 1).Select(n => b.Nat[n]).ToList();
        Check(bots.All(x => x.TechsDone != 0), "every bot studies by itself");
        int e1 = bots.Count(x => x.Era >= 1);
        Check(e1 == bots.Count, $"after 30 min at speed 3 {e1} of {bots.Count} bots are in Древний мир");
        Check(b.Nat[Me].Era == 0 && b.Nat[Me].TechPool > 0, "a player who never chose stays in Первобытная with the science banked");

        Section("technologies: the debug era jump grants what came before");
        var j = Fresh(w);
        Simulation.JumpToEra(j, 3);
        Check(j.Nat.All(x => x.Era == 3 && Techs.KnownIn(x, 0) == 6), "--era=3: every nation there, all first-era technologies known");
    }

    static void Cycles(WorldData w, GameState s, int cycles)
    {
        for (int done = 0; done < cycles;)
        {
            if (Clock.IsCycleTick(s.Tick)) done++;
            Simulation.Step(w, s, null);
        }
    }

    static int Own(WorldData w, GameState s, System.Func<int, bool> ok)
    {
        for (int p = 0; p < w.P; p++) if (s.Owner[p] == Me && w.PLand[p] == 1 && ok(p)) return p;
        return -1;
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }
}
