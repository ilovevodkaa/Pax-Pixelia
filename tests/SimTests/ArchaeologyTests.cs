using System.IO;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>Archaeology: ruins placed by the seed, a dig only by the owner from Возрождение and with gold, its rewards
/// once, the dug ruins in the save and the hash, bots digging what they own.</summary>
public static class ArchaeologyTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("archaeology: ruins in the world");
        var ruins = Archaeology.Ruins(w);
        int land = Enumerable.Range(0, w.P).Count(p => w.PLand[p] == 1);
        Check(ruins.Length >= 10 && ruins.Length <= land / 60, $"{ruins.Length} ruins among {land} land provinces");
        Check(ruins.SequenceEqual(ruins.OrderBy(p => p)) && ruins.All(p => w.PLand[p] == 1) && Archaeology.IndexOf(w, ruins[3]) == 3, "in id order, on land, indexed");
        Check(Archaeology.Ruins(WorldGen.Generate(w.Seed + 1, 2560, 1440)).Length > 0, "another seed has its own");

        Section("archaeology: the dig");
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        int p = ruins[0];
        Check(Commands.Apply(w, s, Cmd.Excavate(Me, p), null) == (int)ExcavateError.NotOwned, "only the owner digs");
        s.Owner[p] = Me;
        Check(Commands.Apply(w, s, Cmd.Excavate(Me, p), null) == (int)ExcavateError.NeedEra, "not before Возрождение");
        s.Nat[Me].Era = Archaeology.Era;
        long gold = s.Nat[Me].Treasury = Archaeology.Cost * Rules.Cents - 1;
        Check(Commands.Apply(w, s, Cmd.Excavate(Me, p), null) == (int)ExcavateError.NoGold, "not without the gold");
        s.Nat[Me].Treasury = gold = 10_000 * Rules.Cents;
        int glory = s.Nat[Me].Glory; long pool = s.Nat[Me].TechPool;
        var h0 = s.Hash();
        Check(Commands.Apply(w, s, Cmd.Excavate(Me, p), null) == 0 && Archaeology.Dug(w, s, p), $"dug: «{Archaeology.FindAt(w, p)}»");
        Check(s.Nat[Me].Glory == glory + Archaeology.Glory && s.Nat[Me].TechPool == pool + Archaeology.Knowledge && s.Nat[Me].Treasury == gold - Archaeology.Cost * Rules.Cents,
            $"+{Archaeology.Glory} glory, +{Archaeology.Knowledge} research, −{Archaeology.Cost} gold");
        Check(Commands.Apply(w, s, Cmd.Excavate(Me, p), null) == (int)ExcavateError.Dug && Commands.Apply(w, s, Cmd.Excavate(Me, ruins[0] + 1 == ruins[1] ? -1 : ruins[0] + 1), null) == (int)ExcavateError.NotRuin,
            "once only; no ruins, no dig");
        Check(!s.Hash().Equals(h0), "the dig is in the hash");

        Section("archaeology: in the save");
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) s.WriteSnapshot(bw);
        ms.Position = 0;
        GameState back;
        using (var br = new BinaryReader(ms)) back = GameState.ReadSnapshot(br, w, null);
        Check(back.Hash().Equals(s.Hash()) && Archaeology.Dug(w, back, p) && !Archaeology.Dug(w, back, ruins[1]), "dug ruins survive a save");

        Section("archaeology: bots dig");
        var b = NationGen.CreateInitialState(w);
        Simulation.Begin(w, b);
        int r = ruins.First(q => b.Owner[q] >= 1);
        int bot = b.Owner[r];
        b.Nat[bot].Era = Archaeology.Era; b.Nat[bot].Treasury = 100_000 * Rules.Cents;
        for (int c = 0; c < 400 && !Archaeology.Dug(w, b, r); c++) Archaeology.Cycle(w, b, c, null);
        Check(Archaeology.Dug(w, b, r), $"a rich bot in Возрождение digs the ruins of {w.PName[r]}");
    }
}
