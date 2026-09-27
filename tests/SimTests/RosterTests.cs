using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Core.Flags;
using PaxPixelia.Core.Nations;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>The per-game roster (MAIN_MENU §2.6): defaults bit-identical, the player's design, colour conflicts, 2..16 nations.</summary>
public static class RosterTests
{
    public static void Run(WorldData w)
    {
        Section("nation roster");
        var def = NationRoster.Build(GameSetup.Default(w.Seed));
        Check(def.Length == 16 && def.Select(n => (n.Name, n.R, n.G, n.B, n.Religion)).SequenceEqual(Data.Nations.Select(n => (n.Name, n.R, n.G, n.B, n.Religion))),
            "default setup → Data.Nations (names, colours, religions)");
        Check(def.Select(n => n.Flag).Distinct().Count() >= 14 && def.SequenceEqual(NationRoster.Build(GameSetup.Default(w.Seed))), "bot flags are varied and the same on every build");
        var a = NationGen.CreateInitialState(w); Simulation.Begin(w, a);
        var b = NationGen.CreateInitialState(w, def); Simulation.Begin(w, b);
        Check(a.Hash().Equals(b.Hash()), "the default roster gives exactly the default game (state hash)");

        var design = new NationDesign("t", "Торнвальд", 72, 112, 182, new FlagSpec(3, 1, 2, 0, 5, 0, 1, 0), 2, 0);   // Кесарат Мирры's blue
        var mine = NationRoster.Build(GameSetup.Default(w.Seed) with { Player = design });
        Check(mine[0].Name == "Торнвальд" && mine[0].R == 72 && mine[0].Flag == design.Flag && mine[0].Culture == 2, "slot 0 is the player's design");
        Check(mine[0].CultureAdj == "торнвальдская", $"adjective from the name: {mine[0].CultureAdj}");
        Check(mine.Skip(1).All(n => NationRoster.DeltaE(n, mine[0].R, mine[0].G, mine[0].B) >= NationRoster.MinColorDistance),
            $"no bot keeps a colour close to the player's (Кесарат Мирры → {mine[1].R},{mine[1].G},{mine[1].B})");
        Check(mine.Select(n => (n.R, n.G, n.B)).Distinct().Count() == mine.Length, "all colours distinct");

        var clash = NationRoster.Build(GameSetup.Default(w.Seed) with { Player = design with { Name = "Торн" } });
        Check(clash.Count(n => n.Name == "Торн") == 1 && clash[2].Name == Data.Nations[0].Name, "a bot with the player's name takes the unused default name");
        Check(new[] { ("Ардания", "арданская"), ("Нирея", "нирейская"), ("Роменна", "роменская"), ("Лура", "лурская"), ("Ун", "унская") }
            .All(x => NationNames.Adjective(x.Item1) == x.Item2), "adjectives: арданская, нирейская, роменская, лурская, унская");

        var four = NationRoster.Build(GameSetup.Default(w.Seed) with { NationCount = 4 });
        var s4 = NationGen.CreateInitialState(w, four); Simulation.Begin(w, s4);
        Check(four.Length == 4 && s4.Nat.Length == 4 && s4.NationCapital.Length == 4 && s4.Owner.All(o => o < 4), "a 4-nation game has 4 nations, capitals and owners");
        Check(NationGen.PlaceCapitals(w, 16).SequenceEqual(a.NationCapital), "PlaceCapitals (for the planet preview) = the game's capitals");
        for (int k = 0; k < 400; k++) Simulation.Step(w, s4, null);
        Check(s4.Owner.All(o => o < 4) && s4.Nat.All(n => n.Treasury >= 0), "a small roster runs");
    }
}
