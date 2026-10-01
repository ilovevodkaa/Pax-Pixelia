using System.IO;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Tests;

/// <summary>The character of a people (IDEAS N-2): deeds push scales, the push fades with a ≈20-minute half-life,
/// leanings and essences switch with a hysteresis and give technology effects, an essence hardens, achievements stay,
/// bots grow characters of their own, and it all survives a save bit for bit.</summary>
public static class CharacterTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("character: the land under a settled capital");
        var l = Fresh(w);
        int river = Enumerable.Range(0, l.NationCount).FirstOrDefault(n => l.NationCapital[n] >= 0 && w.PRiver[l.NationCapital[n]] != 0, -1);
        Check(river < 0 || l.Nat[river].CharA[Character.Agri] >= Character.LandPush * Character.Unit, "a capital on a river leans its people to the fields");
        Check(Enumerable.Range(0, l.NationCount).All(n => Enumerable.Range(0, Character.Count).All(k => Character.Level(l.Nat[n], k) == 0)),
              "but the land alone makes no leaning yet");

        Section("character: deeds and the value");
        var s = Blank(Fresh(w));
        var nat = s.Nat[Me];
        Check(Enumerable.Range(0, Character.Count).All(k => Character.Value(nat, k) == 0 && Character.Level(nat, k) == 0)
              && Character.Portrait(nat) == "Характер народа ещё не сложился", "no traits at the start: every scale at 0");
        Character.Deed(s, Me, Character.Faith, false, 15);
        Check(Character.Value(nat, Character.Faith) == -33, $"a myth-sized push (+15) gives v = −33, just short of a leaning ({Character.Value(nat, Character.Faith)})");
        Character.Deed(s, Me, Character.Faith, false, 3);
        Cycle(w, s);
        Check(Character.Level(nat, Character.Faith) == -1 && Character.Value(nat, Character.Faith) <= -Character.Lean, $"18 units: a leaning (v = {Character.Value(nat, Character.Faith)})");
        int shrineMood = Techs.Sum(nat, TechFx.ShrineMood);
        Check(Character.Fx(nat, TechFx.ShrineMood) == 1 && shrineMood >= 1, $"the leaning «набожный» adds +1 shrine mood through Techs.Sum (now {shrineMood})");
        Character.Deed(s, Me, Character.Faith, false, 60);
        Cycle(w, s);
        Check(Character.Level(nat, Character.Faith) == -2 && Character.Fx(nat, TechFx.ShrineMood) == 3, $"78 units: the essence, shrines +3 (v = {Character.Value(nat, Character.Faith)})");
        Check(Character.Portrait(nat) == "Набожный народ", $"portrait: «{Character.Portrait(nat)}»");
        Character.Deed(s, Me, Character.Agri, false, 20);
        Cycle(w, s);
        Check(Character.Portrait(nat) == "Набожный народ пахарей", $"two scales: «{Character.Portrait(nat)}»");
        int capBefore = Simulation.Capacity(w, s, s.NationCapital[Me]);
        Check(Character.Fx(nat, TechFx.CapPermille) == 50, "«земледельческий» leaning: population cap +5% of the base");

        Section("character: hysteresis");
        var h = Blank(Fresh(w));
        var hn = h.Nat[Me];
        SetValue(h, 72); Cycle(w, h);
        Check(Character.Level(hn, 0) == -2, "v = −72: essence");
        SetValue(h, 64); Cycle(w, h);
        Check(Character.Level(hn, 0) == -2, "v = −64: still the essence (it fades below 60)");
        SetValue(h, 55); Cycle(w, h);
        Check(Character.Level(hn, 0) == -1, "v = −55: back to a leaning");
        SetValue(h, 28); Cycle(w, h);
        Check(Character.Level(hn, 0) == -1, "v = −28: still a leaning (it fades below 25)");
        SetValue(h, 20); Cycle(w, h);
        Check(Character.Level(hn, 0) == 0 && Character.Fx(hn, TechFx.CapPermille) == 0, "v = −20: gone, and so is the bonus");
        SetValue(h, -40); Cycle(w, h);
        Check(Character.Level(hn, 0) == 1 && Character.Fx(hn, TechFx.TaxPermille) == 50, "the other side at once: «купеческий», taxes +5%");

        Section("character: the deeds fade");
        var d = Blank(Fresh(w));
        Character.Deed(d, Me, Character.Commune, true, 100);
        int a0 = d.Nat[Me].CharB[Character.Commune];
        int cycles = 0;
        while (d.Nat[Me].CharB[Character.Commune] > a0 / 2) { Cycle(w, d); cycles++; }
        double minutes = cycles * (double)Clock.CycleTicks / Clock.TicksPerSecond[Clock.ReferenceSpeed] / 60;
        Check(cycles > 2200 && cycles < 2600, $"half-life {cycles} cycles ≈ {minutes:0} min at speed 3");

        Section("character: an essence hardens, achievements stay");
        var e = Blank(Fresh(w));
        Character.Deed(e, Me, Character.Tradition, true, 200);
        for (int k = 0; k <= Character.HardenCycles; k++) { Character.Deed(e, Me, Character.Tradition, true, 1); Cycle(w, e); }
        Check(Character.Hardened(e.Nat[Me], Character.Tradition, true), "«новаторский» held for 10 min: hardened");
        e.Nat[Me].CharB[Character.Tradition] = 0;
        Character.Deed(e, Me, Character.Tradition, false, 200);
        Cycle(w, e);
        Check(Character.Level(e.Nat[Me], Character.Tradition) == -2 && Character.Fx(e.Nat[Me], TechFx.Science) == 1 && Character.Fx(e.Nat[Me], TechFx.Mood) == 4,
            "turned «традиционный»: its essence (mood +4) and the hardened leaning (+1 study) both count");
        var g = Fresh(w);
        int give = 0;
        for (int p = 0; p < w.P && g.Owner.Count(o => o == Me) < 60; p++)
            if (w.PLand[p] == 1 && g.Owner[p] < 0) { g.Owner[p] = g.Controller[p] = (short)Me; give++; }
        for (int k = 0; k < Character.TraitCheckCycles; k++) Cycle(w, g);
        Check(Character.Has(g.Nat[Me], Character.Gatherers) && Character.Fx(g.Nat[Me], TechFx.CityInfluence) >= 1, $"60 provinces: «Собиратели земель» (+{give} given)");

        Section("character: a real game");
        var r = Fresh(w);
        for (int k = 0; k < 2400 * Clock.CycleTicks; k++) Simulation.Step(w, r, null);   // 20 min at speed 3
        var bots = Enumerable.Range(0, r.NationCount).Where(n => !r.IsHuman(n)).ToList();
        var portraits = bots.Select(n => Character.Portrait(r.Nat[n])).ToList();
        int shaped = portraits.Count(p => p != "Характер народа ещё не сложился");
        Info("bots after 20 min: " + string.Join(" · ", bots.Take(6).Select(n => $"{r.Nations[n].Name}: {Character.Portrait(r.Nat[n])}")));
        Check(shaped * 2 >= bots.Count, $"{shaped} of {bots.Count} bots have a character after 20 min");
        Check(portraits.Distinct().Count() >= 3, $"and they differ ({portraits.Distinct().Count()} different portraits)");
        Check(Enumerable.Range(0, Character.Count).Any(k => bots.Any(n => Character.Value(r.Nat[n], k) > 0)) &&
              Enumerable.Range(0, Character.Count).Any(k => bots.Any(n => Character.Value(r.Nat[n], k) < 0)), "both poles occur");
        int[] traits = bots.Select(n => System.Numerics.BitOperations.PopCount((uint)(r.Nat[n].CharTraits & 0xFFFF))).ToArray();
        Info($"achievements earned by bots: {traits.Sum()}");

        Section("character: saved bit for bit");
        var a = Fresh(w);
        for (int k = 0; k < 1500 * Clock.CycleTicks; k++) Simulation.Step(w, a, null);
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) a.WriteSnapshot(bw);
        ms.Position = 0;
        GameState b;
        using (var br = new BinaryReader(ms)) b = GameState.ReadSnapshot(br, w, null);
        Check(b.Hash().All == a.Hash().All && Enumerable.Range(0, a.NationCount).All(n => Character.Portrait(a.Nat[n]) == Character.Portrait(b.Nat[n])
              && Enumerable.Range(0, (int)TechFx.MineMaterials + 1).All(f => Character.Fx(a.Nat[n], (TechFx)f) == Character.Fx(b.Nat[n], (TechFx)f))),
            "a loaded game has the same characters and bonuses");
        for (int k = 0; k < 600 * Clock.CycleTicks; k++) { Simulation.Step(w, a, null); Simulation.Step(w, b, null); }
        Check(b.Hash().All == a.Hash().All, "and plays on identically");
    }

    /// <summary>Scale 0 to exactly v (v &gt; 0 = the left pole here): A and B chosen so round(100·(B−A)/(A+B+30)) = −v.</summary>
    static void SetValue(GameState s, int v)
    {
        var nat = s.Nat[Me];
        // with B = 0: −100·A/(A+30) = −v → A = 30v/(100−v); in units × 1000, rounded to the nearest
        long target = System.Math.Abs(v);
        long a = 30_000L * target / (100 - target);
        if (v >= 0) { nat.CharA[0] = (int)a; nat.CharB[0] = 0; }
        else { nat.CharA[0] = 0; nat.CharB[0] = (int)a; }
    }

    /// <summary>One rules cycle of the character alone (the rest of the world stands still).</summary>
    static void Cycle(WorldData w, GameState s)
    {
        _cycle++;
        Character.Cycle(w, s, _cycle, null);
    }

    static int _cycle;

    /// <summary>The player's character wiped (the land's first push included), for exact numbers.</summary>
    static GameState Blank(GameState s)
    {
        var nat = s.Nat[Me];
        System.Array.Clear(nat.CharA); System.Array.Clear(nat.CharB); System.Array.Clear(nat.CharLevel); System.Array.Clear(nat.CharHeld);
        nat.CharTraits = 0; nat.FirstTechs = 0;
        Character.Refresh(nat);
        return s;
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }
}
