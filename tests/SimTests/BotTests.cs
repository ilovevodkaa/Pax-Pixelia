using System.Collections.Generic;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Tests;

/// <summary>Bots build: they put up mills, farms and shrines in their provinces by the player's rules (terrain, knowledge,
/// gold, materials), keep their income above the upkeep, send geologists to their hills, and stay deterministic.</summary>
public static class BotTests
{
    const int Me = GameState.LocalPlayer;
    const int Cycles = 1200;   // ≈ 10 min at speed 3: studies take two minutes, so the first buildings open later

    public static void Run(WorldData w)
    {
        Section("bots: they build in their provinces");
        var s = Fresh(w);
        int bots = Enumerable.Range(0, s.NationCount).Count(n => !s.IsHuman(n));
        var before = BotBuildings(w, s);
        var start = Enumerable.Range(0, w.P).Select(p => s.Buildings[p].ToList()).ToArray();   // the generator's own, tech or not
        long minNet = long.MaxValue;
        for (int done = 0; done < Cycles;)
        {
            if (Clock.IsCycleTick(s.Tick)) done++;
            Simulation.Step(w, s, null);
            if (Clock.IsCycleTick(s.Tick))
                for (int n = 0; n < s.NationCount; n++)
                    if (!s.IsHuman(n) && s.Nat[n].LastUpkeep > 0) minNet = System.Math.Min(minNet, s.Nat[n].LastTaxes - s.Nat[n].LastUpkeep);
        }
        var after = BotBuildings(w, s);
        int builders = Enumerable.Range(0, s.NationCount).Count(n => !s.IsHuman(n) && after[n] > before[n]);
        int total = after.Sum() - before.Sum();
        Check(builders * 4 >= bots * 3, $"{builders} of {bots} bots built something in {Cycles} cycles ({total} buildings outside the capitals)");

        var kinds = Enumerable.Range(0, w.P).Where(p => s.Owner[p] >= 0 && !s.IsHuman(s.Owner[p]) && s.CapitalOf[p] < 0)
            .SelectMany(p => s.Buildings[p].Except(start[p])).GroupBy(b => b).ToDictionary(g => g.Key, g => g.Count());
        Info("bots built: " + string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{Core.Data.BldName[(int)k.Key]} {k.Value}")));
        Check(kinds.GetValueOrDefault(Bld.Lumber) + kinds.GetValueOrDefault(Bld.Quarry) > 0, "bots dig themselves materials (lumber mills, quarries)");
        Check(kinds.GetValueOrDefault(Bld.Farm) + kinds.GetValueOrDefault(Bld.Fishery) + kinds.GetValueOrDefault(Bld.Pasture) > 0, "bots grow food (farms, fisheries, pastures)");

        bool legal = true;
        var facts = WorldFacts.Of(w);
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            if (o < 0 || s.IsHuman(o)) continue;
            if (s.Buildings[p].Count > s.Slots[p] || s.Buildings[p].Distinct().Count() != s.Buildings[p].Count) legal = false;
            if (s.CapitalOf[p] < 0 && s.Buildings[p].Except(start[p]).Any(b => !facts.Allows(p, b) || !Techs.Allows(s.Nat[o], b))) legal = false;
        }
        Check(legal, "every bot building stands on allowed terrain, with the knowledge for it, in a free slot");
        Check(s.Nat.Where(x => !x.Human).All(x => x.Treasury >= 0 && x.Materials >= 0), "no bot ran into debt or below zero materials");
        Check(minNet >= 0, $"bot income never fell below the upkeep (worst net {minNet / 100.0:0.##} a cycle)");

        Section("bots: geologists");
        var g = Fresh(w);
        foreach (var x in g.Nat) if (!x.Human) Techs.Learn(x, Techs.SurveyTech);   // Кремень known from the start
        for (int k = 0; k < Cycles * Clock.CycleTicks; k++) Simulation.Step(w, g, null);
        int surveyed = Enumerable.Range(0, w.P).Count(p => g.Owner[p] >= 0 && !g.IsHuman(g.Owner[p]) && g.OreFound[p]);
        int found = Enumerable.Range(0, w.P).Count(p => g.Owner[p] >= 0 && !g.IsHuman(g.Owner[p]) && g.OreFound[p] && g.Ore[p] >= 0);
        Check(surveyed > 0, $"bots who know flint survey their hills: {surveyed} provinces in {Cycles} cycles, {found} with ore");

        Section("bots: the player's land is left alone, and it is all deterministic");
        var a = Fresh(w); var b2 = Fresh(w);
        var mine = Enumerable.Range(0, w.P).Where(p => a.Owner[p] == Me).ToDictionary(p => p, p => a.Buildings[p].Count);
        for (int k = 0; k < 3000; k++) { Simulation.Step(w, a, null); Simulation.Step(w, b2, null); }
        Check(mine.All(kv => a.Owner[kv.Key] != Me || a.Buildings[kv.Key].Count == kv.Value || a.CapitalOf[kv.Key] >= 0),
            "nobody builds in the player's provinces (only the capital's own projects)");
        Check(a.Hash().All == b2.Hash().All, "same world, same ticks → same bot buildings");
    }

    /// <summary>Buildings each nation has outside its capital (capital projects come by themselves).</summary>
    static int[] BotBuildings(WorldData w, GameState s)
    {
        var c = new int[s.NationCount];
        for (int p = 0; p < w.P; p++)
            if (s.Owner[p] >= 0 && s.CapitalOf[p] < 0) c[s.Owner[p]] += s.Buildings[p].Count;
        return c;
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }
}
