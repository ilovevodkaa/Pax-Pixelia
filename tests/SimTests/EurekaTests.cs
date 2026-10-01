using System.Collections.Generic;
using System.IO;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Tests;

/// <summary>Eurekas (CONTENT §11): a deed in the land puts 40% of a technology's price into it, once, never for a known
/// technology; the points wait for the choice; bots get theirs; the record is saved and a game without one hashes as before.</summary>
public static class EurekaTests
{
    const int Me = GameState.LocalPlayer;

    sealed class Notes : ISimSink
    {
        public readonly List<string> Lines = new();
        public void Notify(string icon, string text) { if (text.StartsWith("Озарение")) Lines.Add(text); }
        public void RaiseProvincesChanged(IReadOnlyList<int> provinces) { }
        public void RaiseFogChanged(IReadOnlyList<int> provinces) { }
    }

    public static void Run(WorldData w)
    {
        Section("eurekas: the table");
        Check(Eurekas.All.Length >= 25 && Eurekas.TechOf.All(t => t >= 0 && Techs.All[t].Fork < 0 && Techs.All[t].Cost > 0),
              $"{Eurekas.All.Length} eurekas, each on a real technology of the tree (no forks, not the root)");
        Check(Eurekas.TechOf.Distinct().Count() == Eurekas.TechOf.Length, "one eureka per technology at most");

        Section("eurekas: three pastures and the wheel");
        var s = Fresh(w);
        var nat = s.Nat[Me];
        int wheel = Techs.Index("wheel"), e = Eurekas.Of(wheel);
        var notes = new Notes();
        int cycle = 0;
        foreach (int p in Enumerable.Range(0, w.P).Where(p => s.Owner[p] == Me)) s.Buildings[p].Remove(Bld.Pasture);
        nat.Eurekas[e >> 6] &= ~(1UL << (e & 63));   // a clean slate for the wheel whatever the start held
        Run(w, s, ref cycle, notes);
        notes.Lines.Clear();
        long before = nat.TechPts[wheel];
        Check(!Eurekas.Fired(nat, e), "no pastures yet: no eureka");
        int put = 0;
        for (int p = 0; p < w.P && put < 3; p++)
            if (s.Owner[p] == Me && !s.Buildings[p].Contains(Bld.Pasture)) { s.Buildings[p].Add(Bld.Pasture); put++; }
        Run(w, s, ref cycle, notes);
        long gain = nat.TechPts[wheel] - before;
        Check(Eurekas.Fired(nat, e) && gain == (long)Techs.Cost(wheel, s.Pace) * Eurekas.Permille / 1000,
              $"3 pastures: «Колесо» +{gain} of {Techs.Cost(wheel, s.Pace)} (40%), not even chosen");
        Check(notes.Lines.Any(x => x.Contains("Колесо") && x.Contains("Гончар")), $"told with its anecdote: «{notes.Lines.FirstOrDefault(x => x.Contains("Колесо"))}»");
        int told = notes.Lines.Count;
        Run(w, s, ref cycle, notes);
        Check(nat.TechPts[wheel] - before == gain && notes.Lines.Count(x => x.Contains("Колесо")) == 1, "only once");

        Section("eurekas: the land at the founding is no deed");
        var b = Fresh(w);
        int c0 = 0;
        var quiet = new Notes();
        Run(w, b, ref c0, quiet);
        Check(quiet.Lines.Count == 0, $"no eureka at the first check of a fresh game ({quiet.Lines.Count})");
        Check(Enumerable.Range(0, Eurekas.All.Length).All(x => !Eurekas.Fired(b.Nat[Me], x) || b.Nat[Me].TechPts[Eurekas.TechOf[x]] == 0),
              "what the start already held is marked without points");

        Section("eurekas: nothing for what is known");
        var k = Fresh(w);
        int grain = Techs.Index("wild_grain"), eg = Eurekas.Of(grain);
        Techs.Set(k.Nat[Me], grain, true);
        long pts = k.Nat[Me].TechPts[grain];
        int c2 = 0;
        Run(w, k, ref c2, null);
        Check(Eurekas.Fired(k.Nat[Me], eg) && k.Nat[Me].TechPts[grain] == pts, "a technology known before the deed: marked, no points");

        Section("eurekas: the points wait and finish the study");
        var r = Fresh(w);
        var rn = r.Nat[Me];
        int writing = Techs.Index("writing");
        Techs.GrantBefore(rn, 1);
        for (int t = 0; t < Techs.Count; t++) if (Techs.All[t].Era == 1 && Techs.All[t].Fork < 0 && t != writing && t != Techs.Index("mathematics")) Techs.Learn(rn, t);
        rn.TechPts[writing] = Techs.Cost(writing, r.Pace) - 1;
        rn.Era = 1;
        rn.Researching = writing;
        Check(Techs.Open(rn, writing), "«Письменность» is open to study");
        for (int i = 0; i < 2 * Clock.CycleTicks; i++) Simulation.Step(w, r, null);
        Check(Techs.Known(rn, writing), "points past the price finish the study as soon as it is studied");

        Section("eurekas: bots and a real game");
        var g = Fresh(w);
        for (long i = 0; i < Clock.TicksFor(40 * 60); i++) Simulation.Step(w, g, null);
        var struck = Enumerable.Range(0, g.NationCount).Where(n => !g.IsHuman(n))
            .Select(n => Enumerable.Range(0, Eurekas.All.Length).Count(x => Eurekas.Fired(g.Nat[n], x))).ToArray();
        Info($"after 40 min bots have {struck.Sum()} eurekas marked ({string.Join(" ", struck)})");
        Check(struck.Count(x => x > 0) * 2 >= struck.Length, "most bots had eurekas");
        var who = Enumerable.Range(0, Eurekas.All.Length).Select(x => Enumerable.Range(0, g.NationCount).Count(n => Eurekas.Fired(g.Nat[n], x))).ToArray();
        Check(who.Count(x => x > 0) >= 5, $"and not only one kind ({who.Count(x => x > 0)} different eurekas struck somewhere)");

        Section("eurekas: saved");
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) g.WriteSnapshot(bw);
        ms.Position = 0;
        GameState l;
        using (var br = new BinaryReader(ms)) l = GameState.ReadSnapshot(br, w, null);
        Check(l.Hash().All == g.Hash().All && Enumerable.Range(0, g.NationCount).All(n => l.Nat[n].Eurekas.SequenceEqual(g.Nat[n].Eurekas)), "a loaded game has the same eurekas");
        for (int i = 0; i < 400 * Clock.CycleTicks; i++) { Simulation.Step(w, g, null); Simulation.Step(w, l, null); }
        Check(l.Hash().All == g.Hash().All, "and plays on identically");
    }

    /// <summary>One eureka check (the next multiple of CheckCycles).</summary>
    static void Run(WorldData w, GameState s, ref int cycle, ISimSink sink)
    {
        cycle += Eurekas.CheckCycles;
        Eurekas.Cycle(w, s, cycle, sink);
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }
}
