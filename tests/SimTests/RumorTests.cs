using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>Rumors: unmet nations within reach are heard of at an unexplored spot near their home; meeting them or
/// exploring the home ends the talk; the list is a pure function of the state (same state, same rumors).</summary>
public static class RumorTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("rumors: unmet neighbours are heard of");
        var s = Fresh(w, nomad: false);
        var f = s.Nat[Me].Fog;
        var list = Rumors.Of(w, s, Me);
        int reachable = Enumerable.Range(1, s.Nat.Length - 1)
            .Count(m => !f.Met[m] && !f.Explored[Rumors.Home(s, m)] && Simulation.Distance(w, s.NationCapital[Me], s.NationCapital[m]) <= Rumors.Reach(0));
        Check(list.Count == reachable && list.Count > 0, $"{list.Count} rumors = the {reachable} unmet nations within {Rumors.Reach(0)} px");
        Check(list.All(r => r.Nation != Me && !f.Met[r.Nation] && w.PLand[r.Province] == 1 && !f.Explored[r.Province]), "each points at unexplored land of an unmet nation");
        Check(list.All(r => Simulation.Distance(w, r.Province, Rumors.Home(s, r.Nation)) <= 200), "… near that nation's home");
        Check(Rumors.Of(w, s, Me).SequenceEqual(list), "the same state tells the same rumors");
        Check(Rumors.Of(w, s, 1).Count == 0, "a bot has no map and hears nothing");
        var text = Rumors.Text(w, s, Me, list[0]);
        Check(text.Contains("на ") || text.Contains("неподалёку"), $"a line with a direction: «{text}»");
        Check(Rumors.Where(w, 0, 0) == "неподалёку", "no direction to the place itself");

        Section("rumors: the talk ends");
        var r0 = list[0];
        f.Met[r0.Nation] = true;
        Check(Rumors.Of(w, s, Me).All(r => r.Nation != r0.Nation), "a met nation is no rumor any more");
        f.Met[r0.Nation] = false;
        f.Explored[Rumors.Home(s, r0.Nation)] = true;
        Check(Rumors.Of(w, s, Me).All(r => r.Nation != r0.Nation), "its home explored: the rumor ends");
        f.Explored[Rumors.Home(s, r0.Nation)] = false;
        if (r0.Province != Rumors.Home(s, r0.Nation))
        {
            f.Explored[r0.Province] = true;
            Check(Rumors.Of(w, s, Me).Any(r => r.Nation == r0.Nation && r.Province == Rumors.Home(s, r0.Nation)), "the vague spot seen: the talk points at the home itself");
            f.Explored[r0.Province] = false;
        }
        int before = Rumors.Of(w, s, Me).Count;
        s.Nat[Me].Era = 3;
        Check(Rumors.Of(w, s, Me).Count >= before, $"talk travels further in later eras ({Rumors.Reach(3)} px)");
        Check(!Rumors.Text(w, s, Me, Rumors.Of(w, s, Me)[0]).Contains("костр"), "later eras tell merchants' tales, not campfire smoke");

        Section("rumors: tribes of the nomad phase");
        var n = Fresh(w, nomad: true);
        var tribes = Rumors.Of(w, n, Me);
        Check(tribes.Count > 0 && tribes.All(r => n.Nat[r.Nation].Camp >= 0), $"a wandering tribe hears of {tribes.Count} other tribes near their camps");
    }

    static GameState Fresh(WorldData w, bool nomad)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        if (nomad) Nomads.Start(w, s);
        Simulation.Begin(w, s);
        return s;
    }
}
