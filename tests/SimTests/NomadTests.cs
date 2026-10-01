using System.IO;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>The nomad phase: tribes instead of borders, walking, supplies, legends, founding the capital with a myth,
/// bots settling by themselves, the elders' deadline, determinism and a save in the middle of a walk.</summary>
public static class NomadTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("nomads: the start");
        var s = Fresh(w);
        var me = s.Nat[Me];
        Check(s.Nat.All(n => n.Camp >= 0) && s.NationCapital.All(c => c < 0), "every nation is a tribe, nobody has a capital");
        Check(s.Owner.All(o => o < 0) && s.Routes.Count == 0 && s.Buildings.All(b => b.Count == 0), "no borders, buildings or routes yet");
        Check(me.Supplies == Nomads.StartSupplies && me.TribePop > 0, $"the tribe: {me.TribePop} people, {me.Supplies} supplies");
        Check(me.Fog.Explored[me.Camp] && me.Fog.Fog[me.Camp] == 2 && me.Fog.Explored.Count(e => e) > 5, $"the tribe knows its hunting grounds ({me.Fog.Explored.Count(e => e)} provinces)");
        Check(me.ScienceRate >= Science.Sages, $"the shamans already make science (+{me.ScienceRate})");
        Check(Nomads.LegendCount(me) <= Nomads.MaxLegends, $"legends of the start: {Names(me)}");

        Section("nomads: walking");
        int camp = me.Camp;
        int next = w.Adj[camp].FirstOrDefault(q => w.PLand[q] == 1 && s.Owner[q] < 0, -1);
        Check(next >= 0 && Commands.Apply(w, s, Cmd.TribeTo(Me, next), null) == 0 && me.CampPath != null, "the tribe sets out to a neighbour");
        for (int k = 0; k < Nomads.StepTicks; k++) Simulation.Step(w, s, null);
        Check(me.Camp == next && me.CampPath == null, $"after {Nomads.StepTicks} ticks ({Nomads.StepTicks / 8.0:0.#} s at speed 3) it stands there");
        Check(Commands.Apply(w, s, Cmd.TribeTo(Me, next), null) == (int)TribeMoveError.Here, "going where it already is: refused");
        int sea = Enumerable.Range(0, w.P).First(p => w.PLand[p] == 0);
        Check(Commands.Apply(w, s, Cmd.TribeTo(Me, sea), null) == (int)TribeMoveError.Sea, "no walking on the sea");

        Section("nomads: supplies");
        var fert = WorldFacts.Of(w).FertPm;
        int barren = Enumerable.Range(0, w.P).FirstOrDefault(p => w.PLand[p] == 1 && fert[p] <= 150 && w.PRiver[p] == 0, -1);
        if (barren >= 0)
        {
            var t = Fresh(w);
            t.Nat[Me].Camp = barren; t.Nat[Me].Supplies = 3;
            int pop0 = t.Nat[Me].TribePop;
            for (int k = 0; k < Nomads.SupplyTicks * 6; k++) Simulation.Step(w, t, null);
            Check(t.Nat[Me].Supplies == 0 && t.Nat[Me].TribePop < pop0, $"barren land eats the supplies, then people leave ({pop0} → {t.Nat[Me].TribePop})");
        }
        int rich = Enumerable.Range(0, w.P).FirstOrDefault(p => w.PLand[p] == 1 && fert[p] >= 600, -1);
        if (rich >= 0)
        {
            var t = Fresh(w);
            t.Nat[Me].Camp = rich; t.Nat[Me].Supplies = 50;
            for (int k = 0; k < Nomads.SupplyTicks * 10; k++) Simulation.Step(w, t, null);
            Check(t.Nat[Me].Supplies > 50, $"rich land feeds the tribe (50 → {t.Nat[Me].Supplies})");
        }

        Section("nomads: the site and the founding");
        var sites = Nomads.BestSites(w, s, Me, 3);
        Check(sites.Count > 0 && sites.All(p => Nomads.CheckSettle(w, s, Me, p) == SettleError.None), $"best sites nearby: {string.Join(", ", sites.Select(p => $"{w.PName[p]} {Nomads.SiteScore(w, s, Me, p)}"))}");
        Check(sites.Select(p => Nomads.SiteScore(w, s, Me, p)).SequenceEqual(sites.Select(p => Nomads.SiteScore(w, s, Me, p)).OrderByDescending(x => x)), "sorted best first");
        Check(Enumerable.Range(0, w.P).Where(p => w.PLand[p] == 1).All(p => Nomads.SiteScore(w, s, Me, p) is >= 0 and <= 100), "every site scores 0..100");
        me.Legends |= 1 << Nomads.River;
        long mat0 = me.Materials; int sup = me.Supplies, people = me.TribePop, at = me.Camp;
        Check(Commands.Apply(w, s, Cmd.Settle(Me, Nomads.River), null) == 0, $"the tribe settles at {w.PName[at]}");
        Check(s.NationCapital[Me] == at && s.CapitalOf[at] == Me && s.Owner[at] == Me && me.Camp == -1, "the camp becomes the capital");
        Check(w.Adj[at].Where(q => w.PLand[q] == 1).All(q => s.Owner[q] == Me || s.Owner[q] != -1 || Cities.IsCity(s, q)), "the land around it joins");
        Check(me.Materials == mat0 + sup / 2 && s.Pop[at] >= people, $"leftover supplies → {sup / 2} materials, the tribe's people move in");
        Check(me.Myth == Nomads.River && s.City[at] == at, "the river became the myth; the capital is a city");
        Check(Commands.Apply(w, s, Cmd.Settle(Me, -1), null) == (int)SettleError.Settled, "settling twice: refused");
        {
            long m0 = me.Materials, sum = 0, g0 = me.Treasury, gsum = 0;
            for (int k = 0; k < 400; k++)
            {
                bool cycle = Clock.IsCycleTick(s.Tick);
                Simulation.Step(w, s, null);
                if (cycle) { sum += me.LastMaterials; gsum += me.LastTaxes - me.LastUpkeep; }
            }
            Check(me.Materials - m0 == sum, $"after the founding materials grow only by production ({m0} → {me.Materials}, produced {sum})");
            T.Info($"gold {g0} → {me.Treasury}, budget {gsum}");
        }
        int riverLand = Enumerable.Range(0, w.P).FirstOrDefault(p => s.Owner[p] == Me && w.PRiver[p] != 0, -1);
        if (riverLand >= 0)
        {
            int withMyth = Simulation.Capacity(w, s, riverLand);
            me.Myth = -1;
            int without = Simulation.Capacity(w, s, riverLand);
            me.Myth = Nomads.River;
            Check(withMyth > without, $"«Дети реки»: river land feeds more ({without} → {withMyth})");
        }
        var near = Fresh(w);
        near.Nat[Me].Camp = s.NationCapital[Me];   // a second tribe on a finished capital's doorstep
        near.Owner[s.NationCapital[Me]] = 1; near.CapitalOf[s.NationCapital[Me]] = 1; near.NationCapital[1] = s.NationCapital[Me]; near.Nat[1].Camp = -1;
        int door = w.Adj[s.NationCapital[Me]].First(q => w.PLand[q] == 1);
        near.Nat[Me].Camp = door;
        Check(Nomads.CheckSettle(w, near, Me, door) == SettleError.TooClose, "no capital next to another city");

        Section("nomads: bots and the elders");
        var b = Fresh(w);
        long ticks = Nomads.AutoTicks + Nomads.StepTicks * 8;
        for (long k = 0; k < ticks; k++) Simulation.Step(w, b, null);
        var bots = Enumerable.Range(1, b.Nat.Length - 1).ToList();
        Check(bots.All(n => b.NationCapital[n] >= 0), $"all {bots.Count} bots settled within their 1–2 minutes");
        Check(b.NationCapital[Me] >= 0 && b.Nat[Me].Camp < 0, "a player who never chose: the elders founded the capital by themselves");
        Check(b.NationCapital.Distinct().Count() == b.NationCapital.Length, "every capital on its own province");
        Check(!b.AnyNomads, "the nomad phase is over for everyone");
        for (int k = 0; k < Clock.TicksFor(120); k++) Simulation.Step(w, b, null);
        Check(Enumerable.Range(0, b.Nat.Length).All(n => b.Owner.Count(o => o == n) >= 1), "every nation holds land and grows from there");

        {
            Section("nomads: no free site near the camp");
            var tt = Fresh(w);
            var tn = tt.Nat[Me];
            var nb = SimScratch.For(w, tt).B;
            nb.RunLand(w, new[] { tn.Camp });
            for (int k = 0; k < nb.Count && nb.Dist[nb.Queue[k]] <= Nomads.NearRadius; k++) tt.Owner[nb.Queue[k]] = 1;   // a neighbour holds it all
            Check(Nomads.BestSites(w, tt, Me, 1).Count == 0, "every province within 3 steps is taken");
            int farSite = Nomads.FallbackSite(w, tt, Me);
            Check(farSite >= 0 && Nomads.CheckSettle(w, tt, Me, farSite) == SettleError.None, $"the elders look further: {(farSite >= 0 ? w.PName[farSite] : "nothing")}");
            for (int k = 0; k < w.P; k++) if (tt.Owner[k] == 1 && tt.CapitalOf[k] < 0) tt.Owner[k] = -1;

            var uu = Fresh(w);
            while (uu.Tick < Nomads.AutoTicks - 1) Simulation.Step(w, uu, null);
            int walkTo = Nomads.BestSites(w, uu, Me, 3).LastOrDefault(-1);
            if (uu.Nat[Me].Camp >= 0 && walkTo >= 0 && walkTo != uu.Nat[Me].Camp)
            {
                Simulation.Step(w, uu, null);
                Check(Nomads.MoveTo(w, uu, Me, walkTo) == TribeMoveError.Elders, "after the elders' deadline the player can no longer lead the tribe away");
            }
            for (int k = 0; k < 120 * 8 && uu.Nat[Me].Camp >= 0; k++) Simulation.Step(w, uu, null);
            Check(uu.Nat[Me].Camp < 0, "the elders found the capital themselves");
        }

        Section("nomads: determinism and a save mid-walk");
        var x = Fresh(w); var y = Fresh(w);
        int far = Nomads.BestSites(w, x, Me, 3).LastOrDefault(-1);
        if (far < 0 || far == x.Nat[Me].Camp) far = w.Adj[x.Nat[Me].Camp].First(q => w.PLand[q] == 1);
        Commands.Apply(w, x, Cmd.TribeTo(Me, far), null);
        Commands.Apply(w, y, Cmd.TribeTo(Me, far), null);
        for (int k = 0; k < Nomads.StepTicks / 2 + 5; k++) { Simulation.Step(w, x, null); Simulation.Step(w, y, null); }
        Check(x.Hash().All == y.Hash().All, "same orders, same tribes");
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) x.WriteSnapshot(bw);
        ms.Position = 0;
        GameState z;
        using (var br = new BinaryReader(ms)) z = GameState.ReadSnapshot(br, w, null);
        Check(z.Hash().All == x.Hash().All && z.Nat[Me].CampPath != null, "a snapshot in the middle of a walk reads back the same");
        for (int k = 0; k < 3000; k++) { Simulation.Step(w, x, null); Simulation.Step(w, z, null); }
        Check(z.Hash().All == x.Hash().All, "and plays on identically through the founding");
    }

    static string Names(NationState n) => string.Join(", ", Enumerable.Range(0, Nomads.Legends.Length).Where(l => Nomads.HasLegend(n, l)).Select(l => Nomads.Legends[l].Name).DefaultIfEmpty("нет"));

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Nomads.Start(w, s);
        Simulation.Begin(w, s);
        return s;
    }
}
