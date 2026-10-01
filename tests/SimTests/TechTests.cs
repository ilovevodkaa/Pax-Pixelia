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
        Check(Techs.OnlyRoot(nat) && nat.Researching == -1 && nat.TechPts.Length == Techs.Count, $"a tribe knows only the fire, the root of all {Techs.Count} technologies");
        Check(Techs.CountIn(0) >= 12 && Techs.Required(0) < Techs.CountIn(0), $"Первобытная: {Techs.CountIn(0)} technologies, {Techs.Required(0)} needed for the next era");
        Check(Enumerable.Range(0, Techs.Count).Where(t => t != Techs.Root).All(t => Techs.Requires(t).Length > 0), "every technology but the root grows from another: one tree");
        Check(Enumerable.Range(0, Techs.Count).Count(t => Techs.Open(nat, t)) is >= 3 and <= 5, "from the fire a few first steps open, not everything");
        Check(Techs.Count <= 63, "the tree fits the bit set");
        Check(Techs.CountIn(1) >= 10 && Techs.Required(1) > 0 && Techs.Required(1) < Techs.CountIn(1) - 2, $"Древний мир: {Techs.CountIn(1)} technologies, {Techs.Required(1)} needed");
        Check(Enumerable.Range(0, Techs.Count).All(t => Techs.Requires(t).All(r => r >= 0 && Techs.All[r].Era <= Techs.All[t].Era)), "every prerequisite exists and comes no later");
        Check(Enumerable.Range(0, Techs.Count).Select(t => (Techs.All[t].Era, Techs.All[t].Lane, Techs.All[t].Order)).Distinct().Count() == Techs.Count, "no two cards on one spot of the tree");
        Check(Techs.All.Where(d => d.Fork == 1).Count() == 3, "the Great Fork has three paths");
        int plot = Own(w, s, p => s.Buildings[p].Count < s.Slots[p] && WorldFacts.Of(w).Allows(p, Bld.Farm) && !s.Buildings[p].Contains(Bld.Farm));
        if (plot >= 0) Check(Rules.CheckBuild(w, s, plot, Bld.Farm, Me) == BuildError.NeedTech, "a farm needs «Дикие злаки»");
        int dig = Own(w, s, p => !s.OreFound[p]);
        if (dig >= 0) Check(Rules.CheckSurvey(s, dig, Me) == SurveyError.NeedTech, "geologists need «Кремень»");
        Check(Rules.CheckBuild(w, s, System.Math.Max(0, plot), Bld.Market, Me) is BuildError.NeedTech or BuildError.NotAllowed or BuildError.NotOwned,
            "markets and granaries wait for their technologies");
        Check(!Techs.Open(nat, Techs.Index("pottery")), "Древний мир technologies stay closed in Первобытная");

        Section("technologies: the pool, a choice, a switch");
        int gath = Techs.Index("gathering"), tools = Techs.Index("stone_tools"), grain = Techs.Index("wild_grain");
        Check(!Techs.Open(nat, grain) && Commands.Apply(w, s, Cmd.Research(Me, grain), null) == Commands.BadCommand, "«Дикие злаки» wait for «Собирательство»");
        int rate = nat.ScienceRate;
        Cycles(w, s, 5);
        Check(nat.TechPool == 5L * rate && rate > 0, $"5 cycles with nothing chosen: {nat.TechPool} points wait in the pool");
        Check(Commands.Apply(w, s, Cmd.Research(Me, gath), null) == 0 && nat.Researching == gath && nat.TechPts[gath] == 5L * rate && nat.TechPool == 0,
            "choosing moves the pool into the technology");
        Cycles(w, s, 3);
        long a = nat.TechPts[gath];
        Check(Commands.Apply(w, s, Cmd.Research(Me, tools), null) == 0 && nat.Researching == tools && nat.TechPts[gath] == a, "a switch keeps what was put in");
        Check(Commands.Apply(w, s, Cmd.Research(Me, 99), null) == Commands.BadCommand && Commands.Apply(w, s, Cmd.Research(Me, -1), null) == Commands.BadCommand,
            "an unknown technology id is refused, not thrown");
        Check(Commands.Apply(w, s, Cmd.Research(Me, gath), null) == 0, "and back");
        int guard = 0;
        while (!Techs.Known(nat, gath) && guard++ < 5000) Simulation.Step(w, s, null);
        Check(Techs.Known(nat, gath) && nat.Researching == -1 && nat.TechPts[gath] == 0, $"«Собирательство» learned after {guard} ticks (≈ {guard / (double)Clock.TicksPerSecond[3]:0} s at speed 3)");
        Check(guard / (double)Clock.TicksPerSecond[3] is > 20 and < 150, "a first-era technology takes about a minute");
        Check(Techs.Open(nat, grain), "a branch grows: «Собирательство» opens «Дикие злаки»");
        Techs.Learn(nat, grain);
        if (plot >= 0) Check(Rules.CheckBuild(w, s, plot, Bld.Farm, Me) is not BuildError.NeedTech, "the farm is open now");
        Check(Commands.Apply(w, s, Cmd.Research(Me, gath), null) == Commands.BadCommand, "a known technology cannot be chosen again");

        Section("technologies: the era gate");
        var g = Fresh(w);
        var gn = g.Nat[Me];
        string[] firstSteps = { "gathering", "hunting", "stone_tools", "speech", "wild_grain", "flint", "stone_axe" };
        foreach (var id in firstSteps) Techs.Learn(gn, Techs.Index(id));
        gn.Progress = Eras.Threshold(1, g.Pace) + 10;
        Cycles(w, g, 1);
        Check(gn.Era == 0 && Techs.EraCap(gn) == 0, $"enough science but {Techs.KnownIn(gn, 0)} of {Techs.Required(0)} technologies: still Первобытная");
        Techs.Learn(gn, Techs.Index("ancestors"));
        Cycles(w, g, 1);
        Check(gn.Era == 1 && Techs.EraCap(gn) >= 1, $"the {Techs.Required(0)}th technology (the fire counts) opens Древний мир");
        Check(Techs.Open(gn, Techs.Index("taming")) && Techs.Open(gn, Techs.Index("harpoon")), "the rest of the first era can still be studied later");
        int pottery = Techs.Index("pottery"), barter = Techs.Index("barter");
        Check(Techs.Open(gn, pottery) && !Techs.Open(gn, barter), "Древний мир: «Гончарный круг» opens (grain known), «Обмен» waits for it");
        Techs.Learn(gn, pottery);
        Check(Techs.Open(gn, barter), "a prerequisite learned opens the next step");
        Techs.Learn(gn, barter);
        Check(Techs.Allows(gn, Bld.Market) && Techs.Allows(gn, Bld.Granary), "«Обмен» opens the market, «Гончарный круг» the granary");

        Section("technologies: the Great Fork");
        var fk = Fresh(w);
        var fn = fk.Nat[Me];
        Simulation.JumpToEra(fk, 1);
        foreach (var id in new[] { "pottery", "chief_law", "first_cities" }) Techs.Learn(fn, Techs.Index(id));
        int temple = Techs.Index("temple_kingdom"), river = Techs.Index("river_realm"), steppe = Techs.Index("steppe_union");
        Check(Techs.Open(fn, temple) && Techs.Open(fn, river) && Techs.Open(fn, steppe), "after «Первые города» all three paths are open");
        Techs.Choose(fn, river);
        fn.TechPts[river] += 5;
        Check(Commands.Apply(w, fk, Cmd.Research(Me, temple), null) == 0 && fn.TechPts[river] == 5, "switching between paths while studying is free");
        Techs.Learn(fn, temple);
        Check(!Techs.Open(fn, river) && !Techs.Open(fn, steppe) && Techs.ForkClosed(fn, river) && fn.TechPts[river] == 0, "a path learned closes the others forever");
        Check(Commands.Apply(w, fk, Cmd.Research(Me, steppe), null) == Commands.BadCommand, "a closed path cannot be chosen");
        int sci0 = Science.Of(fk, Me).Total;
        Check(Techs.Sum(fn, TechFx.Science) >= 2 && Science.Of(fk, Me).Knowledge >= 2, $"«Храмовое царство» adds science (+{Science.Of(fk, Me).Knowledge})");

        Section("technologies: effects");
        var fx = Fresh(w);
        var xn = fx.Nat[Me];
        for (int t = 0; t < Techs.Count; t++) Techs.Set(xn, t, t != Techs.Index("masonry"));   // every bit, all fork paths too
        int full = Rules.BuildMaterials(Bld.Shrine, xn);
        Techs.Learn(xn, Techs.Index("masonry"));
        Check(Rules.BuildMaterials(Bld.Shrine, xn) < full, $"«Каменное строительство»: a shrine takes {full} → {Rules.BuildMaterials(Bld.Shrine, xn)} materials");
        int cap = fx.NationCapital[Me];
        long tax0 = Rules.ProvinceTax(fx, cap);
        Techs.Set(xn, Techs.Index("chief_law"), false);
        Check(Rules.ProvinceTax(fx, cap) < tax0, "«Закон вождя» raises the taxes");

        Section("technologies: bots and the whole first era");
        var b = Fresh(w);
        Cycles(w, b, 1);
        var first = Enumerable.Range(1, b.Nat.Length - 1).Select(n => b.Nat[n].Researching).ToList();
        Check(first.All(t => t >= 0) && first.Distinct().Count() > 1, $"bots pick their first study themselves, and not all the same ({first.Distinct().Count()} different)");
        for (int k = 0; k < Clock.TicksFor(30 * 60); k++) Simulation.Step(w, b, null);
        var bots = Enumerable.Range(1, b.Nat.Length - 1).Select(n => b.Nat[n]).ToList();
        Check(bots.All(x => Techs.KnownCount(x) > 1), "every bot studies by itself");
        int e1 = bots.Count(x => x.Era >= 1);
        Check(e1 == bots.Count, $"after 30 min at speed 3 {e1} of {bots.Count} bots are in Древний мир");
        Check(b.Nat[Me].Era == 0 && b.Nat[Me].TechPool > 0, "a player who never chose stays in Первобытная with the science banked");

        Section("technologies: the capital's queue waits for knowledge");
        var q = Fresh(w);
        int qc = q.NationCapital[Me];
        Cycles(w, q, 300);   // 2.5 min at speed 3: every project the first era allows is done
        var qn = q.Nat[Me];
        Check(!Techs.Known(qn, Techs.Index("pottery")) && !q.Buildings[qc].Contains(Bld.Granary) && !q.Buildings[qc].Contains(Bld.Market),
            $"no granary or market without pottery and barter (capital: {string.Join(", ", q.Buildings[qc])})");
        Check(qn.ProjectIndex < 0 || Simulation.Projects[qn.ProjectIndex].Building is not Bld pb || Techs.Allows(qn, pb), "the queue never stands on a locked building");
        Check(Simulation.QueueWaitsForKnowledge(q, Me), "the panel can tell «waiting for knowledge» from «all done»");
        Techs.Learn(qn, Techs.Index("pottery"));
        Cycles(w, q, 40);
        Check(q.Buildings[qc].Contains(Bld.Granary), "pottery learned: the granary joins the queue and gets built");

        Section("technologies: the debug era jump grants what came before");
        var j = Fresh(w);
        Simulation.JumpToEra(j, 3);
        Check(j.Nat.All(x => x.Era == 3 && Techs.KnownIn(x, 0) == Techs.CountIn(0)), "--era=3: every nation there, all first-era technologies known");
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
