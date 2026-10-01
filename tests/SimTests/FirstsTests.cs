using System.Collections.Generic;
using System.IO;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>World firsts (CONTENT §10.3): each goes to the first nation only and stays; it gives its bonus through
/// Techs.Sum and a push to the character; humans hear of it by name or as «некий далёкий народ»; it is saved and hashed,
/// and a game with none taken hashes as before.</summary>
public static class FirstsTests
{
    const int Me = GameState.LocalPlayer;

    sealed class Notes : ISimSink
    {
        public readonly List<string> Lines = new();
        public void Notify(string icon, string text) { if (icon == "trophy") Lines.Add(text); }
        public void RaiseProvincesChanged(IReadOnlyList<int> provinces) { }
        public void RaiseFogChanged(IReadOnlyList<int> provinces) { }
    }

    public static void Run(WorldData w)
    {
        Section("firsts: taken once, by the first");
        var s = Fresh(w);
        Check(Enumerable.Range(0, Firsts.Count).All(f => Firsts.Holder(s, f) < 0) && Firsts.IsBlank(s), $"{Firsts.Count} firsts, all open at the start");
        var notes = new Notes();
        int other = Enumerable.Range(1, s.NationCount - 1).First(n => !s.IsHuman(n) && !Rules.Met(s, Me, n));
        ulong hash0 = s.Hash().All;
        int tax0 = Techs.Sum(s.Nat[other], TechFx.TaxPermille);
        int gold = Enumerable.Range(0, w.P).FirstOrDefault(p => s.Owner[p] == other && s.Ore[p] == Rules.OreGold, -1);
        if (gold < 0) { gold = Enumerable.Range(0, w.P).First(p => s.Owner[p] == other); s.Ore[gold] = Rules.OreGold; }
        Firsts.OnSurvey(w, s, other, gold, notes);
        Check(Firsts.Holder(s, Firsts.Gold) == other && Firsts.Has(s.Nat[other], Firsts.Gold), "the first gold goes to the nation that found it");
        Check(Techs.Sum(s.Nat[other], TechFx.TaxPermille) == tax0 + 50, "and its bonus counts in Techs.Sum (taxes +5%)");
        Check(notes.Lines.Count == 1 && notes.Lines[0].StartsWith("Некий далёкий народ"), $"the player has not met them: «{notes.Lines.FirstOrDefault()}»");
        Firsts.OnSurvey(w, s, Me, gold, notes);
        Check(Firsts.Holder(s, Firsts.Gold) == other && !Firsts.Has(s.Nat[Me], Firsts.Gold) && notes.Lines.Count == 1, "a second find takes nothing and says nothing");
        Check(s.Hash().All != hash0, "a taken first changes the state hash");

        notes.Lines.Clear();
        s.Nat[Me].Fog.Met[other] = true;
        Firsts.OnFound(w, s, other, notes);
        Check(notes.Lines.Count == 1 && notes.Lines[0].StartsWith(s.Nations[other].Name), $"met: named — «{notes.Lines[0]}»");
        notes.Lines.Clear();
        int writing = System.Array.FindIndex(Techs.All, t => t.Id == "writing");
        Firsts.OnLearn(w, s, Me, writing, notes);
        Check(Firsts.Holder(s, Firsts.Writing) == Me && notes.Lines.Count == 1 && notes.Lines[0].Contains("наше навсегда"), $"ours: «{notes.Lines[0]}»");

        Section("firsts: standing deeds, checked every few cycles");
        var r = Fresh(w);
        int a = Enumerable.Range(1, r.NationCount - 1).First(n => !r.IsHuman(n)), b = Enumerable.Range(a + 1, r.NationCount - a - 1).First(n => !r.IsHuman(n));
        Give(w, r, a, Firsts.RealmProvinces);
        Give(w, r, b, Firsts.RealmProvinces + 3);
        Firsts.Cycle(w, r, Firsts.CheckCycles, null);
        Check(Firsts.Holder(r, Firsts.Realm) == b, "two realms past 50 in the same check: the bigger one takes it");
        Check(Firsts.Holder(r, Firsts.GreatCity) < 0, "no city of 250 000 yet");
        int cap = r.NationCapital[a];
        r.Pop[cap] = Firsts.GreatCityPeople;
        Firsts.Cycle(w, r, Firsts.CheckCycles + 1, null);
        Check(Firsts.Holder(r, Firsts.GreatCity) < 0, "between checks nothing is awarded");
        Firsts.Cycle(w, r, 2 * Firsts.CheckCycles, null);
        Check(Firsts.Holder(r, Firsts.GreatCity) == a, "at the next check the great city is a's");

        Section("firsts: a game of bots");
        var g = Fresh(w);
        for (long k = 0; k < Clock.TicksFor(60 * 60); k++) Simulation.Step(w, g, null);
        var taken = Enumerable.Range(0, Firsts.Count).Where(f => Firsts.Holder(g, f) >= 0).ToList();
        Info("after 60 min: " + string.Join(" · ", taken.Select(f => $"{Firsts.All[f].Name} — {g.Nations[Firsts.Holder(g, f)].Name}")));
        Check(taken.Contains(Firsts.NewTown) && taken.Contains(Firsts.Mine), "within an hour someone founds a town and digs a mine");
        Check(!taken.Contains(Firsts.Hearth), "a game started settled has no race for the first hearth");
        Check(taken.Select(f => Firsts.Holder(g, f)).Distinct().Count() >= 2, "the firsts do not all go to one nation");
        Check(taken.All(f => Firsts.Has(g.Nat[Firsts.Holder(g, f)], f)), "every holder carries its bit");

        Section("firsts: saved and played on");
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) g.WriteSnapshot(bw);
        ms.Position = 0;
        GameState l;
        using (var br = new BinaryReader(ms)) l = GameState.ReadSnapshot(br, w, null);
        Check(l.Hash().All == g.Hash().All && Enumerable.Range(0, g.NationCount).All(n => l.Nat[n].Firsts == g.Nat[n].Firsts
              && Techs.Sum(l.Nat[n], TechFx.TaxPermille) == Techs.Sum(g.Nat[n], TechFx.TaxPermille)), "loaded: the same holders, bits and bonuses");
        for (int k = 0; k < 400 * Clock.CycleTicks; k++) { Simulation.Step(w, g, null); Simulation.Step(w, l, null); }
        Check(l.Hash().All == g.Hash().All, "and it plays on identically");

        Section("firsts: the first hearth in a nomad start");
        var nm = Fresh(w, nomad: true);
        var hearth = new Notes();
        for (long k = 0; k < Clock.TicksFor(3 * 60) && Firsts.Holder(nm, Firsts.Hearth) < 0; k++) Simulation.Step(w, nm, hearth);
        int h = Firsts.Holder(nm, Firsts.Hearth);
        Check(h >= 0 && nm.Nat.Count(x => Nomads.IsNomad(x)) > 0, $"the first tribe to settle takes it ({(h >= 0 ? nm.Nations[h].Name : "nobody")}) while others still wander");
    }

    /// <summary>Hand nation n free land next to it until it holds `count` provinces.</summary>
    static void Give(WorldData w, GameState s, int n, int count)
    {
        var frontier = new Queue<int>();
        for (int p = 0; p < w.P; p++) if (s.Owner[p] == n) frontier.Enqueue(p);
        int have = s.Owner.Count(o => o == n);
        while (have < count && frontier.Count > 0)
        {
            int p = frontier.Dequeue();
            foreach (int q in w.Adj[p])
                if (have < count && w.PLand[q] == 1 && s.Owner[q] < 0) { s.Owner[q] = s.Controller[q] = (short)n; have++; frontier.Enqueue(q); }
        }
    }

    static GameState Fresh(WorldData w, bool nomad = false)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        if (nomad) Nomads.Start(w, s);
        Simulation.Begin(w, s);
        return s;
    }
}
