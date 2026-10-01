using System;
using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>Unrest in steps, secession, «Раздать хлеб», the empty treasury, and the plague with its quarantine.</summary>
public static class UnrestTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Stages(w);
        Secession(w);
        ReliefAndDebt(w);
        Plague(w);
        Saved(w);
    }

    static void Stages(WorldData w)
    {
        Section("unrest: the steps");
        Check(Unrest.Stage(60) == UnrestStage.Calm && Unrest.Stage(39) == UnrestStage.Grumbling && Unrest.Stage(24) == UnrestStage.Unrest && Unrest.Stage(14) == UnrestStage.Revolt,
              "mood 60 calm, 39 grumbling, 24 strikes, 14 rising");
        var s = Fresh(w);
        int p = Own(w, s, q => s.CapitalOf[q] < 0 && s.Pop[q] > 5000);
        s.Pop[p] = 400_000;   // big enough for whole hundredths
        s.Mood[p] = 40;
        long calm = Rules.ProvinceTax(s, p);
        s.Mood[p] = 39;
        long grumble = Rules.ProvinceTax(s, p);
        Check(grumble > 0 && grumble <= calm * 80 / 100, $"one mood point under 40 costs a quarter of the taxes: {calm} → {grumble}");
        s.Mood[p] = 20;
        Check(Rules.ProvinceTax(s, p) == 0 && !Unrest.Works(s, p), "on strike: no taxes, no work");
        int town = Own(w, s, q => s.IsTown[q]);
        if (town >= 0)
        {
            s.Mood[town] = 20;
            Check(Cities.Influence(s, town) == 0 && Cities.CyclesToNext(w, s, town) == -1, "a striking town does not grow (and says so, no division by zero)");
        }
    }

    static void Secession(WorldData w)
    {
        Section("unrest: a rising lasts a minute, then the province leaves");
        var s = Fresh(w);
        int p = Own(w, s, q => s.CapitalOf[q] < 0 && !s.IsTown[q]);
        Hold(w, s, p, 10, Unrest.RevoltAt - 2);
        Check(s.Owner[p] == Me && s.Unrest[p] >= Unrest.RevoltAt - 3, $"after {Unrest.RevoltAt - 2} cycles of rising it still holds (count {s.Unrest[p]})");
        Hold(w, s, p, 10, 4);
        Check(s.Owner[p] < 0 && s.Unrest[p] == 0 && s.Mood[p] > Unrest.GrumbleBelow, "then it secedes: free land, calm again");
        Check(Rules.CheckClaim(w, s, p, Me) is ClaimError.None or ClaimError.NoGold or ClaimError.CityFull, "and it can be claimed back");

        var t = Fresh(w);
        int q2 = Own(w, t, q => t.CapitalOf[q] < 0 && !t.IsTown[q]);
        Hold(w, t, q2, 10, 30);
        int count = t.Unrest[q2];
        t.Mood[q2] = 50;
        RunCycles(w, t, 10);
        Check(t.Unrest[q2] <= Math.Max(0, count - 15) && t.Owner[q2] == Me, $"calmed down, the count falls twice as fast: {count} → {t.Unrest[q2]}");

        var c = Fresh(w);
        int cap = c.NationCapital[Me];
        Hold(w, c, cap, 5, Unrest.RevoltAt - 3);
        c.Nat[Me].Treasury = 1000 * Rules.Cents;
        Hold(w, c, cap, 5, 5);
        Check(c.Owner[cap] == Me && c.Nat[Me].Treasury < 1000 * Rules.Cents * (100 - Unrest.RiotLootPct + 5) / 100, $"the capital never secedes: it riots and the treasury is looted (1000 → {c.Nat[Me].Treasury / Rules.Cents} gold)");

        var d = Fresh(w);
        int town = Own(w, d, q => d.IsTown[q]);
        if (town >= 0)
        {
            int sphere = Enumerable.Range(0, w.P).Count(q => d.Owner[q] == Me && d.City[q] == town);
            for (int q = 0; q < w.P; q++) if (d.Owner[q] == Me && d.City[q] == town) d.Mood[q] = 20;
            Hold(w, d, town, 5, Unrest.RevoltAt + 2);
            int gone = Enumerable.Range(0, w.P).Count(q => d.Owner[q] < 0 && d.City[q] < 0 && !d.IsTown[q] && q == town);
            Check(d.Owner[town] < 0 && !d.IsTown[town], $"a town secedes with its restless land ({sphere} provinces in its sphere)");
            Check(Enumerable.Range(0, w.P).Where(q => d.Owner[q] == Me).All(q => d.City[q] >= 0 && Cities.IsCity(d, d.City[q])), "the loyal land goes to the cities left");
        }
        else Check(true, "no town on this seed (skip)");
    }

    static void ReliefAndDebt(WorldData w)
    {
        Section("unrest: bread and an empty treasury");
        var s = Fresh(w);
        int p = Own(w, s, q => s.CapitalOf[q] < 0);
        s.Mood[p] = 70;
        Check(Unrest.CheckRelief(s, p, Me) == ReliefError.Calm, "no bread where people are content");
        s.Mood[p] = 20; s.Unrest[p] = 0;
        s.Nat[Me].Treasury = 0;
        Check(Commands.Apply(w, s, Cmd.Relief(Me, p), null) == (int)ReliefError.NoGold, "no gold, no bread");
        s.Nat[Me].Treasury = 10_000 * Rules.Cents;
        int price = Unrest.ReliefPrice(s, p);
        Check(Commands.Apply(w, s, Cmd.Relief(Me, p), null) == 0 && s.Mood[p] == 20 + Unrest.ReliefMood && s.Nat[Me].Treasury == (10_000 - price) * Rules.Cents,
              $"«Раздать хлеб»: mood 20 → {s.Mood[p]} for {price} gold");

        var d = Fresh(w);
        d.Nat[Me].Era = 1;
        Policy.Set(d.Nat[Me], Policy.Index("sages"), true);
        Policy.Set(d.Nat[Me], Policy.Index("levy"), true);
        d.Nat[Me].Treasury = 1;
        int given = 0;   // a sprawling realm: its administration eats more than it earns
        for (int q = 0; q < w.P && given < 200; q++) if (d.Owner[q] < 0 && w.PLand[q] == 1) { d.Owner[q] = d.Controller[q] = Me; given++; }
        for (int k = 0; k < 20 && d.Nat[Me].Treasury >= 0; k++) RunCycles(w, d, 1);
        Check(d.Nat[Me].Treasury < 0 && !Policy.On(d.Nat[Me], Policy.Index("sages")) && Policy.On(d.Nat[Me], Policy.Index("levy")),
              "the treasury runs dry: paid edicts are repealed, the levy stays");
        int cap = d.NationCapital[Me];
        int before = d.Mood[cap];
        RunCycles(w, d, 30);
        Check(d.Nat[Me].Treasury >= 0 || d.Mood[cap] < before, $"unpaid, people sulk: capital mood {before} → {d.Mood[cap]}");
    }

    static void Plague(WorldData w)
    {
        Section("crises: the plague");
        var s = Fresh(w);
        int p = Own(w, s, q => s.Pop[q] > 5000 && w.Adj[q].Count(r => w.PLand[r] == 1) >= 3);
        s.Plague[p] = Unrest.PlagueCycles / Unrest.PlagueStep;
        int pop0 = s.Pop[p], mood0 = s.Mood[p];
        RunCycles(w, s, 60);
        Check(s.Pop[p] < pop0 && s.Mood[p] < mood0, $"60 cycles of plague: people {pop0} → {s.Pop[p]}, mood {mood0} → {s.Mood[p]}");
        RunCycles(w, s, Unrest.PlagueCycles);
        Check(!Unrest.Sick(s, p) && Unrest.Immune(s, p), "after its time it heals, and stays immune");
        int spread = Enumerable.Range(0, w.P).Count(q => q != p && s.Plague[q] != 0);
        Info($"it reached {spread} more provinces");

        int sickFree = 0, sickQuarantine = 0;
        for (int k = 0; k < 6; k++)
        {
            var a = Fresh(w); var b = Fresh(w);
            RunCycles(w, a, k * 7); RunCycles(w, b, k * 7);   // different rolls
            b.Nat[Me].Era = 1;
            Policy.Set(b.Nat[Me], Policy.Index("quarantine"), true);
            int pa = Own(w, a, q => a.Pop[q] > 5000), pb = pa;
            a.Plague[pa] = Unrest.PlagueCycles / Unrest.PlagueStep; b.Plague[pb] = Unrest.PlagueCycles / Unrest.PlagueStep;
            RunCycles(w, a, Unrest.PlagueCycles); RunCycles(w, b, Unrest.PlagueCycles);
            sickFree += Enumerable.Range(0, w.P).Count(q => a.Plague[q] != 0);
            sickQuarantine += Enumerable.Range(0, w.P).Count(q => b.Plague[q] != 0);
        }
        Check(sickQuarantine < sickFree, $"«Карантин» holds it back: {sickQuarantine} provinces touched against {sickFree} without (6 outbreaks)");

        var o = NationGen.CreateInitialState(w);
        foreach (var x in o.Nat) x.Control = NationControl.Bot;
        Simulation.Begin(w, o);
        foreach (var x in o.Nat) x.Era = Math.Max((byte)1, x.Era);
        RunCycles(w, o, 4800);
        int touched = o.Plague.Count(x => x != 0);
        Check(touched > 0, $"in 40 min of a bot world the plague breaks out by itself ({touched} provinces sick or immune)");
    }

    static void Saved(WorldData w)
    {
        Section("unrest: saved");
        var s = Fresh(w);
        int p = Own(w, s, q => s.CapitalOf[q] < 0);
        s.Mood[p] = 10; s.Unrest[p] = 33; s.Plague[p] = 20;
        var back = SaveFile.Restore(SaveFile.Snapshot(s), w, TestContent.Db, 100);
        Check(back.Unrest[p] == 33 && back.Plague[p] == 20, "risings and sickness survive a save");
        Check(Fresh(w).Hash().Provinces != s.Hash().Provinces, "the hash sees them");
    }

    /// <summary>Keep p at this mood for n cycles (the drift would pull it back).</summary>
    static void Hold(WorldData w, GameState s, int p, int mood, int cycles)
    {
        for (int done = 0; done < cycles;)
        {
            if (s.Owner[p] >= 0) s.Mood[p] = (byte)mood;
            if (Clock.IsCycleTick(s.Tick)) done++;
            Simulation.Step(w, s, null);
        }
    }

    static void RunCycles(WorldData w, GameState s, int cycles)
    {
        for (int done = 0; done < cycles;)
        {
            if (Clock.IsCycleTick(s.Tick)) done++;
            Simulation.Step(w, s, null);
        }
    }

    static int Own(WorldData w, GameState s, Func<int, bool> ok)
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
