using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Tests;

/// <summary>Materials and ore: lumber mills and quarries fill the store every cycle, buildings and towns take from it,
/// the mills themselves are free, and surveyed ore changes the province (a mine, a gold vein, salt).</summary>
public static class MaterialTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("materials: production and costs");
        var s = Fresh(w);
        var nat = s.Nat[Me];
        Check(nat.Materials == Rules.StartMaterials, $"every nation starts with {Rules.StartMaterials} materials");
        int cap = s.NationCapital[Me];
        Check(cap >= 0 && nat.LastMaterials >= Rules.CapitalMaterials, $"the capital's workshops give materials from the start (+{nat.LastMaterials} a cycle)");

        int lumberAt = Own(w, s, p => WorldFacts.Of(w).Allows(p, Bld.Lumber) && s.Buildings[p].Count < s.Slots[p] && !s.Buildings[p].Contains(Bld.Lumber));
        Check(lumberAt >= 0, "an own province allows a lumber mill");
        if (lumberAt >= 0)
        {
            nat.Materials = 0;
            Check(Commands.Apply(w, s, Cmd.Build(Me, lumberAt, Bld.Lumber), null) == 0, "a lumber mill is built with an empty store (it costs no materials)");
            int before = Rules.ProvinceMaterials(s, lumberAt);
            Check(before >= Rules.LumberMaterials, $"the lumber mill yields +{Rules.LumberMaterials} a cycle");
            long m0 = nat.Materials;
            RunCycles(w, s, 3);
            Check(nat.Materials - m0 == 3L * nat.LastMaterials && nat.LastMaterials > 0, $"3 cycles: {m0} → {nat.Materials} materials (+{nat.LastMaterials} a cycle)");
        }

        int shrineAt = Own(w, s, p => WorldFacts.Of(w).Allows(p, Bld.Shrine) && s.Buildings[p].Count < s.Slots[p] && !s.Buildings[p].Contains(Bld.Shrine));
        if (shrineAt >= 0)
        {
            nat.Treasury += 10_000 * Rules.Cents;
            nat.Materials = Rules.BuildMaterials(Bld.Shrine) - 1;
            Check(Rules.CheckBuild(w, s, shrineAt, Bld.Shrine, Me) == BuildError.NoMaterials, "a shrine needs materials: refused one short");
            nat.Materials = Rules.BuildMaterials(Bld.Shrine);
            long gold = nat.Treasury;
            Check(Commands.Apply(w, s, Cmd.Build(Me, shrineAt, Bld.Shrine), null) == 0 && nat.Materials == 0
                  && gold - nat.Treasury == Rules.BuildCost(Bld.Shrine) * Rules.Cents, "built with exactly enough: gold and materials both spent");
        }
        else Check(true, "no shrine site on this seed (skip)");

        Section("materials: founding a town takes them");
        var t = Fresh(w);
        t.Nat[Me].Treasury += 100_000 * Rules.Cents;
        for (int k = 0; k < 400; k++) Simulation.Step(w, t, null);   // let the capital grow people for settlers
        int site = Enumerable.Range(0, w.P).FirstOrDefault(p => Cities.Check(w, t, p, Me) == FoundError.None, -1);
        if (site >= 0)
        {
            t.Nat[Me].Materials = Cities.FoundMaterials - 1;
            Check(Cities.Check(w, t, site, Me) == FoundError.NoMaterials, "a town one material short is refused");
            t.Nat[Me].Materials = Cities.FoundMaterials;
            Check(Commands.Apply(w, t, Cmd.FoundCity(Me, site), null) == 0 && t.Nat[Me].Materials == 0, $"founded with exactly {Cities.FoundMaterials} materials");
        }
        else Check(true, "no town site on this seed (skip)");

        Section("ore: only surveyed ore counts");
        var o = Fresh(w);
        int metal = Own(w, o, p => o.Ore[p] is Rules.OreCopper or Rules.OreTin or Rules.OreIron && WorldFacts.Of(w).Allows(p, Bld.Quarry) && o.Buildings[p].Count < o.Slots[p]);
        if (metal < 0)
        {
            // no metal under own land on this seed: plant a vein (and a quarry) in an own province to test the rule itself
            metal = Own(w, o, p => !o.Buildings[p].Contains(Bld.Lumber));
            if (metal >= 0) o.Ore[metal] = Rules.OreIron;
        }
        if (metal >= 0)
        {
            o.OreFound[metal] = false;
            if (!o.Buildings[metal].Contains(Bld.Quarry)) o.Buildings[metal].Add(Bld.Quarry);
            int hidden = Rules.ProvinceMaterials(o, metal);
            Check(!Rules.IsMine(o, metal), "an unsurveyed vein under a quarry is no mine");
            o.OreFound[metal] = true;
            Check(Rules.IsMine(o, metal) && Rules.ProvinceMaterials(o, metal) == hidden + Rules.MineMaterials,
                $"surveyed: the quarry becomes a mine (+{Rules.MineMaterials})");
        }
        else Check(true, "no quarry site under own land (skip)");

        int p0 = o.NationCapital[Me];
        sbyte ore0 = o.Ore[p0]; bool found0 = o.OreFound[p0];
        o.Ore[p0] = Rules.OreGold; o.OreFound[p0] = false;
        long tax = Rules.ProvinceTax(o, p0);
        o.OreFound[p0] = true;
        Check(Rules.ProvinceTax(o, p0) - tax == Rules.GoldVeinTax, "a surveyed gold vein adds to the taxes");
        o.Ore[p0] = ore0; o.OreFound[p0] = found0;

        Section("materials: deterministic and saved");
        var a = Fresh(w); var b = Fresh(w);
        for (int k = 0; k < 800; k++) { Simulation.Step(w, a, null); Simulation.Step(w, b, null); }
        Check(a.Hash().All == b.Hash().All && a.Nat.Select(x => x.Materials).SequenceEqual(b.Nat.Select(x => x.Materials)),
            $"same world, same ticks → same stores (player {a.Nat[Me].Materials})");
    }

    static void RunCycles(WorldData w, GameState s, int cycles)
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
        s.Nat[Me].TechsDone = (1L << 6) - 1;   // the first era's buildings are known; no later bonuses (discounts, taxes) in the numbers
        Simulation.Begin(w, s);
        return s;
    }
}
