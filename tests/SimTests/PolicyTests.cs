using System;
using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>The economy of choices (Policy): prices that grow with the era and the realm, the administration limit and
/// its overextension, the edicts — their slots, effects, costs, the command, the save — and bots that use them.</summary>
public static class PolicyTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Prices(w);
        Administration(w);
        Closed(w);
        // the edicts are put aside in the game; their rules stay covered for when they come back
        Policy.EdictsOpen = true;
        try { Edicts(w); Effects(w); SavedAndBots(w); }
        finally { Policy.EdictsOpen = false; }
    }

    static void Closed(WorldData w)
    {
        Section("policy: the edicts are put aside for now");
        var s = Fresh(w);
        s.Nat[Me].Era = 1;
        Check(Commands.Apply(w, s, Cmd.Edict(Me, Policy.Index("feasts"), true), null) == (int)EdictError.Closed && s.Nat[Me].Edicts == 0, "no edict can be issued");
        Policy.Set(s.Nat[Me], Policy.Index("sages"), true);   // as an older save would have it
        RunCycles(w, s, 1);
        Check(s.Nat[Me].Edicts == 0, "an older save's edicts are repealed");
        var b = NationGen.CreateInitialState(w);
        foreach (var n in b.Nat) n.Control = NationControl.Bot;
        Simulation.Begin(w, b);
        RunCycles(w, b, 400);
        Check(b.Nat.All(n => n.Edicts == 0), "bots issue none either");
    }

    static void Prices(WorldData w)
    {
        Section("policy: prices grow with the era and the realm");
        Check(Rules.ClaimPriceFor(0, 0, 0) == Rules.ClaimCost, $"the first province of a new realm costs the base {Rules.ClaimCost}");
        Check(Rules.ClaimPriceFor(40, 0, 0) == 2 * Rules.ClaimCost, "a realm of 40 provinces pays twice the base");
        Check(Rules.ClaimPriceFor(20, 4, 0) == Rules.ClaimPriceFor(20, 0, 0) * 2, "four eras later land costs twice as much");
        Check(Rules.ClaimPriceFor(20, 0, 50) == Rules.ClaimPriceFor(20, 0, 0) * 3 / 2, "50% overextension: land 50% dearer");
        var s = Fresh(w);
        var nat = s.Nat[Me];
        int b0 = Rules.BuildPrice(Core.Data.Bld.Farm, nat), m0 = Rules.BuildMaterials(Core.Data.Bld.Farm, nat);
        nat.Era = 4;
        Check(Rules.BuildPrice(Core.Data.Bld.Farm, nat) == 2 * b0 && Rules.BuildMaterials(Core.Data.Bld.Farm, nat) == 2 * m0, $"a farm in the 5th era: {b0} → {Rules.BuildPrice(Core.Data.Bld.Farm, nat)} gold, {m0} → {Rules.BuildMaterials(Core.Data.Bld.Farm, nat)} materials");
        Check(Rules.SurveyPrice(nat) == 2 * Rules.SurveyCost, "geologists too");
        Check(Cities.FoundPriceFor(4, 0) == 2 * Cities.FoundCost && Cities.FoundPriceFor(1, 4) == Cities.FoundCost * 5 / 4 * 2, "a town: +25% per city the realm has, +25% per era");
    }

    static void Administration(WorldData w)
    {
        Section("policy: the administration limit and overextension");
        var s = Fresh(w);
        var (prov, limit, over) = Policy.Admin(s, Me);
        Check(over == 0 && prov < limit, $"a fresh realm governs itself: {prov} of {limit} provinces");
        Check(Policy.AdminUpkeep(10, 20) == 10 * Policy.AdminPerProvince && Policy.AdminUpkeep(30, 20) == 30 * Policy.AdminPerProvince + 10 * Policy.AdminPerOver,
              "upkeep: every province, and five times more beyond the limit");
        Check(Policy.OverPct(30, 20) == 50 && Policy.OverMood(50) == -10 && Policy.OverMood(500) == -Policy.OverMoodMax, "50% over: mood −10; it never falls by more than 15");
        int before = Policy.AdminLimit(s.Nat[Me], 1);
        Techs.Set(s.Nat[Me], Techs.Index("elders"), true);
        Check(Policy.AdminLimit(s.Nat[Me], 1) == before + 3, $"«Совет старейшин» raises the limit: {before} → {Policy.AdminLimit(s.Nat[Me], 1)}");

        // grab land far past the limit: upkeep and mood follow, the next province costs more
        var t = Fresh(w);
        t.Nat[Me].Treasury = 0;
        RunCycles(w, t, 1);
        long upkeepBefore = t.Nat[Me].LastUpkeep;
        int grabbed = 0;
        for (int p = 0; p < w.P && grabbed < 40; p++)
            if (t.Owner[p] < 0 && w.PLand[p] == 1 && Rules.Borders(w, t, p, Me)) { t.Owner[p] = t.Controller[p] = Me; grabbed++; }
        var (prov2, limit2, over2) = Policy.Admin(t, Me);
        Check(over2 > 0, $"after taking {grabbed} provinces by force: {prov2} of {limit2}, {over2}% over");
        RunCycles(w, t, 1);
        Check(t.Nat[Me].LastUpkeep > upkeepBefore + grabbed * Policy.AdminPerProvince, $"upkeep {upkeepBefore} → {t.Nat[Me].LastUpkeep} (hundredths a cycle)");
        Check(Rules.ClaimPrice(t, Me) == Rules.ClaimPriceFor(prov2, 0, over2) && over2 > 0, $"the next province costs {Rules.ClaimPrice(t, Me)} gold");
        int cap = t.NationCapital[Me];
        int moodBefore = t.Mood[cap];
        RunCycles(w, t, 40);
        Check(t.Mood[cap] < moodBefore || moodBefore <= 20, $"the capital sulks under overextension: mood {moodBefore} → {t.Mood[cap]}");
    }

    static void Edicts(WorldData w)
    {
        Section("policy: edicts — slots and the command");
        var s = Fresh(w);
        var nat = s.Nat[Me];
        int feasts = Policy.Index("feasts"), sages = Policy.Index("sages"), levy = Policy.Index("levy");
        Check(Policy.Count >= 5 && feasts >= 0 && sages >= 0 && levy >= 0, $"{Policy.Count} edicts: {string.Join(", ", Policy.Edicts.Select(e => e.Name))}");
        Check(Policy.Slots(0) == 1 && Policy.Slots(1) == 2 && Policy.Slots(3) == 3 && Policy.Slots(6) == 4, "slots: 1, +1 in Древний мир, Средневековье, Индустриальная");
        Check(Commands.Apply(w, s, Cmd.Edict(Me, feasts, true), null) == 0 && Policy.On(nat, feasts), "an edict is issued by command");
        Check(Commands.Apply(w, s, Cmd.Edict(Me, sages, true), null) == (int)EdictError.NoSlot && !Policy.On(nat, sages), "a second one in Первобытная: refused, no slot");
        Check(Commands.Apply(w, s, Cmd.Edict(Me, feasts, true), null) == 0 && Policy.Active(nat) == 1, "issuing it again changes nothing");
        Check(Commands.Apply(w, s, Cmd.Edict(Me, sages, false), null) == (int)EdictError.NotOn, "repealing one not in force: refused");
        Check(Commands.Apply(w, s, Cmd.Edict(Me, 99, true), null) == (int)EdictError.Unknown, "an unknown edict: refused");
        nat.Era = 1;
        Check(Commands.Apply(w, s, Cmd.Edict(Me, sages, true), null) == 0 && Policy.Active(nat) == 2, "Древний мир: a second slot");
        Check(Commands.Apply(w, s, Cmd.Edict(Me, feasts, false), null) == 0 && !Policy.On(nat, feasts) && Policy.Active(nat) == 1, "an edict is repealed by command");
    }

    static void Effects(WorldData w)
    {
        Section("policy: what the edicts do");
        int feasts = Policy.Index("feasts"), sages = Policy.Index("sages"), envoys = Policy.Index("envoys"), corvee = Policy.Index("corvee"), levy = Policy.Index("levy");

        // the levy: +25% taxes, nothing to pay; festivals: 15% of the taxes
        var s = Fresh(w);
        RunCycles(w, s, 1);
        var b0 = Policy.BudgetOf(s, Me);
        Policy.Set(s.Nat[Me], levy, true);
        var b1 = Policy.BudgetOf(s, Me);
        Check(b1.Taxes > b0.Taxes * 120 / 100 && b1.Taxes <= b0.Taxes * 126 / 100 && b1.Edicts == 0, $"«Сбор податей»: taxes {b0.Taxes} → {b1.Taxes}, nothing to pay");
        Policy.Set(s.Nat[Me], levy, false); Policy.Set(s.Nat[Me], feasts, true);
        var b2 = Policy.BudgetOf(s, Me);
        Check(b2.Edicts == b2.Taxes * 15 / 100 && b2.Edicts > 0, $"«Праздники» take 15% of the taxes: {b2.Edicts} of {b2.Taxes}");
        long gold = s.Nat[Me].Treasury;
        RunCycles(w, s, 1);
        Check(s.Nat[Me].LastUpkeep == b2.Upkeep || System.Math.Abs(s.Nat[Me].LastUpkeep - b2.Upkeep) <= b2.Upkeep / 20, $"the cycle charges the edict: upkeep {s.Nat[Me].LastUpkeep} ≈ {b2.Upkeep}");

        // festivals lift the mood, the levy and the corvée lower it
        var happy = Fresh(w); var sad = Fresh(w); var plain = Fresh(w);
        Policy.Set(happy.Nat[Me], feasts, true);
        Policy.Set(sad.Nat[Me], levy, true);
        RunCycles(w, happy, 60); RunCycles(w, sad, 60); RunCycles(w, plain, 60);
        int Mood(GameState x) => (int)Enumerable.Range(0, w.P).Where(p => x.Owner[p] == Me).Average(p => (double)x.Mood[p]);
        Check(Mood(happy) > Mood(plain) && Mood(plain) > Mood(sad), $"60 cycles: mood with festivals {Mood(happy)}, without {Mood(plain)}, under the levy {Mood(sad)}");

        // the corvée: +50% materials
        var c = Fresh(w);
        RunCycles(w, c, 1);
        int m0 = c.Nat[Me].LastMaterials;
        Policy.Set(c.Nat[Me], corvee, true);
        RunCycles(w, c, 1);
        Check(c.Nat[Me].LastMaterials == m0 + (m0 * 50 + 50) / 100 && c.Nat[Me].LastMaterials > m0, $"«Трудовая повинность»: materials {m0} → {c.Nat[Me].LastMaterials} a cycle");

        // sages: research +30%; envoys: city influence +35%
        var r = Fresh(w);
        r.Nat[Me].ScienceRate = 100;
        int rr = Techs.ResearchRate(r.Nat[Me]);
        Policy.Set(r.Nat[Me], sages, true);
        Check(Techs.ResearchRate(r.Nat[Me]) == rr + rr * 30 / 100, $"«Покровительство мудрецам»: research {rr} → {Techs.ResearchRate(r.Nat[Me])}");
        int cap = r.NationCapital[Me];
        int inf = Cities.Influence(r, cap);
        Policy.Set(r.Nat[Me], envoys, true);
        Check(Cities.Influence(r, cap) == inf + inf * 35 / 100, $"«Гонцы к племенам»: the capital's influence {inf} → {Cities.Influence(r, cap)}");
    }

    static void SavedAndBots(WorldData w)
    {
        Section("policy: edicts are saved, bots issue them");
        var s = Fresh(w);
        s.Nat[Me].Era = 1;
        Policy.Set(s.Nat[Me], Policy.Index("sages"), true);
        Policy.Set(s.Nat[Me], Policy.Index("corvee"), true);
        var snap = SaveFile.Snapshot(s);
        var back = SaveFile.Restore(snap, w, TestContent.Db, 100);
        Check(back.Nat.Select(n => n.Edicts).SequenceEqual(s.Nat.Select(n => n.Edicts)) && back.Nat[Me].Edicts != 0, "edicts survive a save");
        var plain = Fresh(w);
        Check(plain.Hash().Nations != s.Hash().Nations, "the hash sees edicts");

        var b = NationGen.CreateInitialState(w);
        foreach (var n in b.Nat) n.Control = NationControl.Bot;
        Simulation.Begin(w, b);
        RunCycles(w, b, 400);
        int with = b.Nat.Count(n => n.Edicts != 0);
        Check(with >= b.Nat.Length * 3 / 4, $"after 400 cycles {with} of {b.Nat.Length} bots govern by edicts");
        Check(b.Nat.All(n => Policy.Active(n) <= Policy.Slots(n.Era)), "no bot holds more edicts than its slots");
    }

    static void RunCycles(WorldData w, GameState s, int cycles)
    {
        for (int done = 0; done < cycles;)
        {
            if (Clock.IsCycleTick(s.Tick)) done++;
            Simulation.Step(w, s, null);
        }
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        for (int t = 0; t < Techs.Count; t++) Techs.Set(s.Nat[Me], t, t < 6);
        Simulation.Begin(w, s);
        s.Nat[Me].RulerTraits = 0; Leader.Refresh(s.Nat[Me]);   // no ruler's bonuses in the numbers
        return s;
    }
}
