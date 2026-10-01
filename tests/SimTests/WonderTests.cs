using System;
using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>The race for the wonders of the world: prices, laying, pouring in, the race (one owner, the rivals get half
/// back), the bonuses, the bell and the village, glory in the score, the save — and bots that race too.</summary>
public static class WonderTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("wonders: the list and the prices");
        Check(Wonders.Count >= 30 && Wonders.All.Select(d => d.Id).Distinct().Count() == Wonders.Count, $"{Wonders.Count} wonders, every id once");
        Check(Wonders.All.Count(d => d.Era == 0) >= 3 && Wonders.All.Count(d => d.Era == 1) >= 5, "the first two eras hold at least 3 and 5 wonders to race for");
        Check(Wonders.All.All(d => d.Era >= 0 && d.Era <= Eras.Last && d.Glory > 0 && d.Effect.Length > 0 && d.Lore.Length > 0), "every wonder has an era, glory, an effect and a story");
        int stone = Wonders.Index("stone_circle"), pyramid = Wonders.Index("great_pyramid"), village = Wonders.Index("potemkin_village"), bell = Wonders.Index("tsar_bell");
        Check(Wonders.GoldCost(stone) == 2500 && Wonders.MatCost(stone) == 2000, $"«Каменный круг»: {Wonders.GoldCost(stone)} gold, {Wonders.MatCost(stone)} materials");
        Check(Wonders.GoldCost(pyramid) > Wonders.GoldCost(Wonders.Index("hanging_gardens")), "the pyramid costs more than the gardens of its era");
        Check(Wonders.GoldCost(village) * 5 == Wonders.GoldCost(Wonders.Index("observatory")), "the Potemkin village: a fifth of the price");

        Section("wonders: laying one, pouring in, giving up");
        var s = Fresh(w);
        var nat = s.Nat[Me];
        Check(Wonders.CheckStart(s, Me, Wonders.Index("hanging_gardens")) == WonderError.TooEarly, "a wonder of a later era: refused");
        Check(Commands.Apply(w, s, Cmd.WonderStart(Me, stone), null) == 0 && nat.Wonder == stone, "a wonder is laid by command");
        Check(Commands.Apply(w, s, Cmd.WonderStart(Me, Wonders.Index("painted_cave")), null) == (int)WonderError.Busy, "one wonder at a time");
        nat.Treasury = 1000 * Rules.Cents; nat.Materials = 500;
        Check(Commands.Apply(w, s, Cmd.WonderInvest(Me), null) == 0 && nat.Treasury == 0 && nat.Materials == 0
              && nat.WonderGold == 1000 * Rules.Cents && nat.WonderMats == 500, "«Вложить»: the treasury and the store go in");
        Check(Commands.Apply(w, s, Cmd.WonderInvest(Me), null) == (int)WonderError.Nothing, "nothing left to give: refused");
        nat.Treasury = 100_000 * Rules.Cents; nat.Materials = 100_000;
        Commands.Apply(w, s, Cmd.WonderInvest(Me), null);
        Check(nat.WonderGold == Wonders.GoldCost(stone) * Rules.Cents && nat.WonderMats == Wonders.MatCost(stone), "it takes no more than the price");
        var t = Fresh(w);
        Commands.Apply(w, t, Cmd.WonderStart(Me, stone), null);
        t.Nat[Me].Treasury = 1000 * Rules.Cents; t.Nat[Me].Materials = 0;
        Commands.Apply(w, t, Cmd.WonderInvest(Me, 600), null);
        Check(t.Nat[Me].WonderGold == 600 * Rules.Cents && t.Nat[Me].Treasury == 400 * Rules.Cents, "a part can be poured in");
        Check(Commands.Apply(w, t, Cmd.WonderStart(Me, -1), null) == 0 && t.Nat[Me].Wonder < 0 && t.Nat[Me].Treasury == 700 * Rules.Cents, "giving up: half comes back");

        Section("wonders: the race");
        var r = Fresh(w);
        int rival = 1;
        r.Nat[rival].Control = NationControl.Human;   // no bot taste in the way
        Commands.Apply(w, r, Cmd.WonderStart(Me, stone), null);
        Commands.Apply(w, r, Cmd.WonderStart(rival, stone), null);
        r.Nat[rival].Treasury = 1000 * Rules.Cents; r.Nat[rival].Materials = 400;
        Commands.Apply(w, r, Cmd.WonderInvest(rival), null);
        long rivalGold = r.Nat[rival].Treasury;
        r.Nat[Me].Treasury = 100_000 * Rules.Cents; r.Nat[Me].Materials = 100_000;
        Commands.Apply(w, r, Cmd.WonderInvest(Me), null);
        int glory0 = r.Nat[Me].Glory, score0 = Rules.Scores(r)[Me];
        RunCycles(w, r, 1);
        Check(r.WonderOwner[stone] == Me && r.Nat[Me].Wonder < 0, "the first to pay owns it");
        Check(r.Nat[rival].Wonder < 0 && r.Nat[rival].Treasury >= rivalGold + 500 * Rules.Cents && r.Nat[rival].Materials >= 200, "the rival stops, half of its gold and stone comes back");
        Check(r.Nat[Me].Glory >= glory0 + Wonders.All[stone].Glory, $"glory {glory0} → {r.Nat[Me].Glory} (the wonder and the world first «Чудо света»)");
        Check(Firsts.Holder(r, Firsts.FirstWonder) == Me, "the world first «Чудо света» is ours");
        Check(Rules.Scores(r)[Me] >= score0 + Wonders.All[stone].Glory * Wonders.GloryScore, $"the score counts glory ×{Wonders.GloryScore}: {score0} → {Rules.Scores(r)[Me]}");
        Check(Wonders.CheckStart(r, rival, stone) == WonderError.Taken, "nobody can lay it again");
        Check(Techs.Sum(r.Nat[Me], TechFx.Science) >= 1 && Wonders.Fx(r.Nat[Me], TechFx.Mood) == 2, "its bonuses count as technology effects (mood +2, study +1)");

        Section("wonders: the bell and the village");
        int cracked = 0, whole = 0;
        for (int seedShift = 0; seedShift < 40; seedShift++)
        {
            var b = Fresh(w);
            b.Nat[Me].Era = 3;
            RunCycles(w, b, seedShift);   // finished in a different cycle each time: a different roll
            Commands.Apply(w, b, Cmd.WonderStart(Me, bell), null);
            b.Nat[Me].Treasury = 1_000_000 * Rules.Cents; b.Nat[Me].Materials = 1_000_000;
            Commands.Apply(w, b, Cmd.WonderInvest(Me), null);
            RunCycles(w, b, 1);
            if ((b.WonderFlag[bell] & Wonders.Cracked) != 0) { cracked++; if (Wonders.Fx(b.Nat[Me], TechFx.ShrineMood) != 0) cracked = -999; }
            else if (b.WonderOwner[bell] == Me) whole++;
        }
        Check(cracked > 5 && whole > 5, $"the Tsar Bell cracks sometimes ({cracked} of 40), a cracked one gives no shrine bonus");
        var v = Fresh(w);
        v.Nat[Me].Era = 4;
        Commands.Apply(w, v, Cmd.WonderStart(Me, village), null);
        v.Nat[Me].Treasury = 1_000_000 * Rules.Cents; v.Nat[Me].Materials = 1_000_000;
        Commands.Apply(w, v, Cmd.WonderInvest(Me), null);
        RunCycles(w, v, 1);
        int gloryWith = v.Nat[Me].Glory;
        int foreign = -1;
        for (int p = 0; p < w.P && foreign < 0; p++)
            if (v.Owner[p] < 0 && w.PLand[p] == 1 && Rules.Borders(w, v, p, Me)) foreign = p;
        if (foreign >= 0) { v.Owner[foreign] = v.Controller[foreign] = 1; }
        RunCycles(w, v, Wonders.FacadeCycles + 1);
        Check(gloryWith >= Wonders.All[village].Glory && (foreign < 0 || Wonders.Fallen(v, village) && v.Nat[Me].Glory <= gloryWith - Wonders.All[village].Glory),
              $"the Potemkin village: glory {gloryWith} until a neighbour comes to the border, then {v.Nat[Me].Glory}");

        Section("wonders: saved, and bots race too");
        var sv = SaveFile.Restore(SaveFile.Snapshot(r), w, TestContent.Db, 100);
        Check(sv.WonderOwner[stone] == Me && sv.Nat[Me].Glory == r.Nat[Me].Glory && Wonders.Fx(sv.Nat[Me], TechFx.Mood) == 2, "owners, glory and bonuses survive a save");
        var plain = Fresh(w);
        Check(Wonders.IsBlank(plain), "a new game: no glory, no wonder — hashed as before wonders");

        var bots = NationGen.CreateInitialState(w);
        foreach (var x in bots.Nat) x.Control = NationControl.Bot;
        Simulation.Begin(w, bots);
        RunCycles(w, bots, 3000);
        int laid = bots.Nat.Count(x => x.Wonder >= 0) + bots.WonderOwner.Count(o => o >= 0);
        Check(laid >= 3, $"in 25 min bots lay or finish wonders: {bots.Nat.Count(x => x.Wonder >= 0)} under way, {bots.WonderOwner.Count(o => o >= 0)} standing");
        Check(bots.Nat.All(x => x.Treasury >= 0 && x.Materials >= 0), "no bot goes into debt for a wonder");
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
        Simulation.Begin(w, s);
        return s;
    }
}
