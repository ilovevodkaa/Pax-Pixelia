using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>Right-click orders: a scout party on its way turns to a new target (Cmd.ScoutMove) by its stable id,
/// finishing the hop under way so it never jumps; refusals for strangers, the sea, the same spot and the unreachable.</summary>
public static class ScoutOrderTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("unit orders: a scout party turns on the way");
        var s = Fresh(w);
        int cap = s.NationCapital[Me];
        var rec = new Recorder();
        Check(Commands.Apply(w, s, Cmd.ScoutAuto(Me), rec) == 0 && s.Scouts.Count == 1, "an auto party sets out");
        var sc = s.Scouts[0];
        Check(Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id + 7, cap), null) == (int)ScoutError.NoParty, "an unknown party id is refused");
        Check(Commands.Apply(w, s, Cmd.ScoutMove(Me + 1, sc.Id, cap), null) == (int)ScoutError.NoParty, "another nation cannot order my party");
        int sea = Enumerable.Range(0, w.P).First(p => w.PLand[p] != 1);
        Check(Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, sea), null) == (int)ScoutError.Sea, "a party does not walk into the sea");
        Check(Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, -1), null) == (int)ScoutError.Sea
              && Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, w.P + 5), null) == (int)ScoutError.Sea, "an out-of-range target is refused, not thrown");
        var land = new SimScratch.Bfs(w.P);
        land.RunLand(w, new[] { cap });
        int island = Enumerable.Range(0, w.P).FirstOrDefault(p => w.PLand[p] == 1 && land.Dist[p] < 0, -1);
        if (island >= 0) Check(Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, island), null) == (int)ScoutError.Far, "land across the sea is out of reach");

        // standing still at the start: it turns from where it stands
        Check(sc.Sub == 0 && Scouts.Current(sc) == cap, "the party stands in the capital before the first tick");
        Check(Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, cap), null) == (int)ScoutError.Here, "standing on the target is refused");
        int far = Enumerable.Range(0, w.P).Where(p => land.Dist[p] == 6).OrderBy(p => p).First();
        Check(Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, far), null) == 0, "ordered six provinces away");
        Check(!sc.Auto && sc.MaxSteps == int.MaxValue && sc.Step == 0 && sc.Path[0] == cap && Scouts.Target(sc) == far && Connected(w, sc.Path),
            $"an auto party becomes a sent one; path of {sc.Path.Length} provinces from the capital to the target");
        Check(s.Scouts.Count == 1 && Scouts.Free(s, Me) == Scouts.Max - 1, "re-routing sends no new party");

        // in the middle of a hop: the hop is finished first, its vision stays
        int guard = 0;
        while (sc.Sub == 0 && guard++ < 40) Simulation.Step(w, s, rec);
        int cur = Scouts.Current(sc), next = sc.Path[sc.Step + 1], sub = sc.Sub, seq = s.ScoutSeq;
        int back = Enumerable.Range(0, w.P).Where(p => land.Dist[p] == 3 && p != cur && p != next).OrderBy(p => p).First();
        Check(sc.Sub > 0 && Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, back), null) == 0, $"turned mid-hop (sub {sub} of {Scouts.SubSteps})");
        Check(Scouts.Current(sc) == cur && sc.Path[1] == next && sc.Sub == sub && sc.Step == 0 && Scouts.Target(sc) == back && Connected(w, sc.Path),
            "it stays where it is, finishes the hop under way, then walks on to the new target");
        Check(s.ScoutSeq == seq, "ids are not spent on a turn");
        Check(Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, cur), null) == 0 && sc.Path[1] == next && Scouts.Target(sc) == cur,
            "back to where it stands mid-hop: one hop on and one hop back, never a jump");

        // it arrives and comes home with the note of a sent party
        Check(Commands.Apply(w, s, Cmd.ScoutMove(Me, sc.Id, back), null) == 0, "and to the new target again");
        guard = 0;
        while (s.Scouts.Count > 0 && guard++ < 4000) Simulation.Step(w, s, rec);
        Check(s.Scouts.Count == 0 && rec.Notes.Any(n => n.text.Contains(w.PName[back])), $"the party reached {w.PName[back]} and came home with its maps");

        Section("unit orders: a walking tribe turns without a jump back");
        var t = NationGen.CreateInitialState(w);
        t.Nat[Me].Control = NationControl.Human;
        Nomads.Start(w, t);
        Simulation.Begin(w, t);
        var me = t.Nat[Me];
        var near = new SimScratch.Bfs(w.P);
        near.RunLand(w, new[] { me.Camp });
        int two = Enumerable.Range(0, w.P).Where(p => near.Dist[p] == 2 && me.Fog.Explored[p]).OrderBy(p => p).FirstOrDefault(-1);
        int other = Enumerable.Range(0, w.P).Where(p => near.Dist[p] == 2 && me.Fog.Explored[p] && p != two).OrderBy(p => p).FirstOrDefault(-1);
        if (two >= 0 && other >= 0)
        {
            Check(Commands.Apply(w, t, Cmd.TribeTo(Me, two), null) == 0, "the tribe sets out two provinces away");
            for (int k = 0; k < 6; k++) Simulation.Step(w, t, null);
            int camp = me.Camp, stepTo = me.CampPath[me.CampStep + 1], tsub = me.CampSub;
            Check(tsub > 0 && Commands.Apply(w, t, Cmd.TribeTo(Me, other), null) == 0, $"sent elsewhere mid-step ({tsub} of {Nomads.StepTicks})");
            Check(me.Camp == camp && me.CampSub == tsub && me.CampStep == 0 && me.CampPath[0] == camp && me.CampPath[1] == stepTo && me.CampPath[^1] == other,
                "it keeps the camp and the step under way, then heads for the new place");
            Check(Commands.Apply(w, t, Cmd.TribeTo(Me, camp), null) == 0 && me.CampPath[1] == stepTo && me.CampPath[^1] == camp,
                "back to the camp mid-step: one step on and one back, never a jump");
        }
        else Check(true, "no explored land two steps from the camp (skip)");

        Section("unit orders: the journal");
        var c = Cmd.ScoutMove(Me, 12, 345);
        var line = c.ToLine();
        Check(Cmd.Parse(line) == c, $"«{line}» round-trips");
    }

    static bool Connected(WorldData w, int[] path)
    {
        for (int i = 1; i < path.Length; i++) if (!w.Adj[path[i - 1]].Contains(path[i]) || w.PLand[path[i]] != 1) return false;
        return path.Length > 1;
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }
}
