using System;
using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>Peaceful rivalry: opinions and their drift, gifts, pacts (taxes, a quiet border), tribute (demands, answers,
/// money), the pull of the borders, the save — and bots that deal with each other.</summary>
public static class DiplomacyTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("diplomacy: opinions, gifts, pacts");
        var s = Fresh(w);
        int m = Neighbour(w, s);
        Check(m > 0, $"a neighbour to deal with: {(m > 0 ? s.Nations[m].Name : "none")}");
        if (m <= 0) return;
        Met(s, m);
        RunCycles(w, s, Diplomacy.DriftCycles * 12);
        int baseOp = Diplomacy.Opinion(s, m, Me);
        Check(baseOp == (s.Nations[m].Religion == s.Nations[Me].Religion ? 10 : -5), $"opinions drift to their base: {baseOp}");
        Check(Commands.Apply(w, s, Cmd.Pact(Me, m, true), null) == (int)DiploError.Disliked, "no pact without friendship");
        s.Nat[Me].Treasury = 100_000 * Rules.Cents;
        long theirs = s.Nat[m].Treasury;
        int price = Diplomacy.GiftPrice(s, m);
        Check(Commands.Apply(w, s, Cmd.Gift(Me, m), null) == 0 && s.Nat[m].Treasury == theirs + price * Rules.Cents && Diplomacy.Opinion(s, m, Me) == baseOp + Diplomacy.GiftOpinion,
              $"a gift of {price} gold: opinion {baseOp} → {Diplomacy.Opinion(s, m, Me)}");
        while (Diplomacy.Opinion(s, m, Me) < Diplomacy.Friendly) Commands.Apply(w, s, Cmd.Gift(Me, m), null);
        Check(Commands.Apply(w, s, Cmd.Pact(Me, m, true), null) == 0 && Diplomacy.HasPact(s, Me, m) && Diplomacy.HasPact(s, m, Me), "friends sign a pact (both ways)");
        RunCycles(w, s, 1);
        var b = Policy.BudgetOf(s, Me);
        Check(b.Pacts == b.Taxes * Diplomacy.PactTaxPermille / 1000 && b.Pacts > 0, $"the pact trades: +{b.Pacts} hundredths a cycle on {b.Taxes}");
        Check(Commands.Apply(w, s, Cmd.Pact(Me, m, false), null) == 0 && !Diplomacy.HasPact(s, m, Me), "a pact can be broken");
        Check(Diplomacy.Opinion(s, m, Me) < Diplomacy.Friendly, $"and they take it badly: {Diplomacy.Opinion(s, m, Me)}");

        Section("diplomacy: tribute");
        var t = Fresh(w);
        Met(t, m);
        Check(Diplomacy.CheckDemand(w, t, Me, m) == DiploError.NotStronger || Diplomacy.CheckDemand(w, t, Me, m) == DiploError.None, "tribute only from the much weaker");
        Strengthen(w, t, Me, 4);
        Check(Commands.Apply(w, t, Cmd.DemandTribute(Me, m), null) == 0 && t.TributeTo[m] == Me, "a bot half as strong pays");
        RunCycles(w, t, 1);
        var bm = Policy.BudgetOf(t, Me);
        Check(bm.TributeIn > 0 && t.Nat[m].LastUpkeep > 0, $"tribute flows: +{bm.TributeIn} hundredths a cycle to us");
        Check(Commands.Apply(w, t, Cmd.StopTribute(m), null) == 0 && t.TributeTo[m] < 0 && Diplomacy.Opinion(t, Me, m) <= Diplomacy.RefuseOpinion + 20, "the payer may stop, the receiver turns cold");

        var h = Fresh(w);
        Met(h, m);
        h.Nat[m].Control = NationControl.Bot;
        Strengthen(w, h, m, 4);
        int cycle = Clock.CycleOf(h.Tick);
        Diplomacy.Demand(h, m, Me, cycle, null);
        Check(h.DemandFrom[Me] == m && h.TributeTo[Me] < 0, "a bot demands tribute from the human: a question, not a fact");
        Check(Commands.Apply(w, h, Cmd.AnswerDemand(Me, false), null) == 0 && h.DemandFrom[Me] < 0 && Diplomacy.Opinion(h, m, Me) <= Diplomacy.Hostile,
              $"refused: they turn hostile ({Diplomacy.Opinion(h, m, Me)})");
        Diplomacy.Demand(h, m, Me, Clock.CycleOf(h.Tick), null);
        RunCycles(w, h, Diplomacy.DemandWait + 2);
        Check(h.DemandFrom[Me] < 0 && h.TributeTo[Me] < 0, "no answer in time counts as a refusal");
        Diplomacy.Demand(h, m, Me, Clock.CycleOf(h.Tick), null);
        Check(Commands.Apply(w, h, Cmd.AnswerDemand(Me, true), null) == 0 && h.TributeTo[Me] == m, "paying is an answer too");

        Section("diplomacy: the pull of the borders");
        var p = Fresh(w);
        Met(p, m);
        int border = -1;
        for (int q = 0; q < w.P && border < 0; q++)
            if (p.Owner[q] == Me && !Cities.IsCity(p, q) && w.Adj[q].Any(r => p.Owner[r] == m)) border = q;
        if (border < 0)
        {
            // no common border on this seed: make one by giving them a province next to ours
            for (int q = 0; q < w.P && border < 0; q++)
                if (p.Owner[q] == Me && !Cities.IsCity(p, q))
                    foreach (int r in w.Adj[q]) if (p.Owner[r] < 0 && w.PLand[r] == 1) { p.Owner[r] = p.Controller[r] = (short)m; border = q; break; }
        }
        Check(border >= 0, "a border province of ours next to them");
        if (border >= 0)
        {
            foreach (int r in w.Adj[border]) if (p.Owner[r] == m) p.Mood[r] = 90;
            Hold(w, p, border, 25, m, Diplomacy.PullAt / 2);
            Check(p.Owner[border] == Me && p.Pull[border] > 0, $"unhappy next to happy neighbours, it leans to them ({p.Pull[border]} of {Diplomacy.PullAt})");
            Hold(w, p, border, 25, m, Diplomacy.PullAt);
            Check(p.Owner[border] == m, "after a minute of leaning it goes over");

            var q2 = Fresh(w);
            Met(q2, m);
            int bb = -1;
            for (int q = 0; q < w.P && bb < 0; q++)
                if (q2.Owner[q] == Me && !Cities.IsCity(q2, q))
                    foreach (int r in w.Adj[q]) if (q2.Owner[r] < 0 && w.PLand[r] == 1 || q2.Owner[r] == m) { if (q2.Owner[r] < 0) q2.Owner[r] = q2.Controller[r] = (short)m; bb = q; break; }
            Diplomacy.MakePact(q2, Me, m);
            if (bb >= 0)
            {
                foreach (int r in w.Adj[bb]) if (q2.Owner[r] == m) q2.Mood[r] = 90;
                Hold(w, q2, bb, 25, m, Diplomacy.PullAt + 10);
                Check(q2.Owner[bb] == Me && q2.Pull[bb] == 0, "a pact keeps the border quiet");
            }
        }

        Section("diplomacy: saved, and bots deal");
        var sv = Fresh(w);
        Met(sv, m);
        Diplomacy.AddOpinion(sv, m, Me, 40); Diplomacy.MakePact(sv, Me, m);
        var back = SaveFile.Restore(SaveFile.Snapshot(sv), w, TestContent.Db, 100);
        Check(Diplomacy.Opinion(back, m, Me) == Diplomacy.Opinion(sv, m, Me) && Diplomacy.HasPact(back, Me, m), "opinions and pacts survive a save");
        Check(Diplomacy.IsBlank(Fresh(w)), "a new game: blank diplomacy, hashed as before");

        var bots = NationGen.CreateInitialState(w);
        foreach (var x in bots.Nat) x.Control = NationControl.Bot;
        Simulation.Begin(w, bots);
        for (int a = 0; a < bots.Nat.Length; a++)   // neighbours that like each other already
            for (int c = 0; c < bots.Nat.Length; c++) if (a != c) Diplomacy.AddOpinion(bots, a, c, 40);
        RunCycles(w, bots, 1200);
        int pacts = bots.Pact.Count(x => x) / 2;
        Info($"bots: {pacts} pacts, {bots.TributeTo.Count(x => x >= 0)} paying tribute after 10 min");
        Check(bots.Nat.All(x => x.Treasury > -1_000_000 * Rules.Cents), "bots do not ruin themselves on gifts");
    }

    static void Met(GameState s, int m)
    {
        if (s.Nat[Me].Fog is { } f && m < f.Met.Length) f.Met[m] = true;
    }

    /// <summary>Make nation n `times` as strong in the score (more people in its land).</summary>
    static void Strengthen(WorldData w, GameState s, int n, int times)
    {
        for (int p = 0; p < w.P; p++) if (s.Owner[p] == n) s.Pop[p] *= times * 3;
    }

    static int Neighbour(WorldData w, GameState s)
    {
        for (int m = 1; m < s.Nat.Length; m++) if (Rules.Met(s, Me, m) || s.Nat[Me].Fog == null) return m;
        return 1;
    }

    static void Hold(WorldData w, GameState s, int p, int mood, int m, int cycles)
    {
        for (int done = 0; done < cycles && s.Owner[p] == Me;)
        {
            s.Mood[p] = (byte)mood;
            foreach (int r in w.Adj[p]) if (s.Owner[r] == m) s.Mood[r] = 90;
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

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }
}
