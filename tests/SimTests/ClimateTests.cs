using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>«Дышащая планета»: the three climate epochs by the year, the kinds of land they touch, the fertility the
/// rules read (a green desert feeds people at the start and starves them by 2500 до н. э., the Little Ice Age chills
/// the north, the warming thaws the glacier edge) and that it all follows the calendar alone.</summary>
public static class ClimateTests
{
    public static void Run(WorldData w)
    {
        Section("climate: the epochs by the year");
        Check(Climate.At(-4000) == new ClimateNow(1000, 0, 0) && Climate.At(-2500).Green == 0 && Climate.At(-3250).Green == 500, "the Green Sahara: full at 4000 до н. э., half at 3250, gone by 2500");
        Check(Climate.At(1300).Frost == 0 && Climate.At(1650).Frost == 1000 && Climate.At(1850).Frost == 0 && Climate.At(1475).Frost == 500, "the Little Ice Age: 1300–1850, strongest in 1650");
        Check(Climate.At(1880).Warm == 0 && Climate.At(1990).Warm == 500 && Climate.At(2100).Warm == 1000 && Climate.At(2500).Warm == 1000, "the warming from 1880, full by 2100");
        Check(Enumerable.Range(0, 5).All(k => Climate.StageText(k) != null || k == 0) && Climate.Stage(-4000) == 0 && Climate.Stage(-3000) == 1 && Climate.Stage(1400) == 2 && Climate.Stage(1700) == 3 && Climate.Stage(1950) == 4,
            "chronicle stages: green, dry, frost, thaw, warming");

        Section("climate: the land it touches");
        var kinds = Climate.KindsOf(w);
        int Count(ClimateKind k) => kinds.Count(x => x == k);
        Info($"desert {Count(ClimateKind.Desert)}, beside the desert {Count(ClimateKind.DesertEdge)}, cold {Count(ClimateKind.Cold)}, glacier edge {Count(ClimateKind.GlacierEdge)}");
        Check(Count(ClimateKind.Desert) > 0 && Count(ClimateKind.Cold) > 0 && Count(ClimateKind.DesertEdge) > 0, "the world has deserts, their steppe edge and a cold north");
        Check(Enumerable.Range(0, w.P).All(p => w.PLand[p] == 1 || kinds[p] == ClimateKind.None), "the sea has no climate kind");

        Section("climate: what the rules read");
        var s = NationGen.CreateInitialState(w);
        Simulation.Begin(w, s);
        var baseFert = WorldFacts.Of(w).FertPm;
        int desert = System.Array.IndexOf(kinds, ClimateKind.Desert);
        int cold = Enumerable.Range(0, w.P).First(p => kinds[p] == ClimateKind.Cold && baseFert[p] > 100);
        var start = Climate.FertNow(w, s);
        Check(start[desert] == System.Math.Min(1000, baseFert[desert] + 400), $"4000 до н. э.: a desert feeds like a savanna ({baseFert[desert]} → {start[desert]} ‰)");
        int capGreen = Simulation.Capacity(w, s, desert);
        s.Day256 = Calendar.DayOfYear(-2000);
        var dry = Climate.FertNow(w, s);
        Check(dry[desert] == baseFert[desert] && Simulation.Capacity(w, s, desert) < capGreen, $"2000 до н. э.: the rains are gone, the desert's people cap falls ({capGreen} → {Simulation.Capacity(w, s, desert)})");
        s.Day256 = Calendar.DayOfYear(1650);
        Check(Climate.FertNow(w, s)[cold] == baseFert[cold] - baseFert[cold] * 350 / 1000, $"1650: the cold north loses 35 % ({baseFert[cold]} → {Climate.FertNow(w, s)[cold]} ‰)");
        Check(Climate.Describe(w, s, cold)?.Contains("ледниковый") == true, $"the card tells why: «{Climate.Describe(w, s, cold)}»");
        s.Day256 = Calendar.DayOfYear(2100);
        int glacier = System.Array.IndexOf(kinds, ClimateKind.GlacierEdge);
        if (glacier >= 0) Check(Climate.FertNow(w, s)[glacier] == baseFert[glacier] + 200, "2100: the glacier edge thawed into poor living land");
        var look = Climate.Look(w, s, cold);
        Check(look.Frost == 0 && Climate.Look(w, s, desert).Green == 0, "after the frost and the green the map shows neither");

        Section("climate: it follows the calendar alone");
        var a = NationGen.CreateInitialState(w); Simulation.Begin(w, a);
        var b = NationGen.CreateInitialState(w); Simulation.Begin(w, b);
        a.Day256 = b.Day256 = Calendar.DayOfYear(-3100);
        Check(Climate.FertNow(w, a).SequenceEqual(Climate.FertNow(w, b)), "two states at one date have one climate");
        var before = Climate.FertNow(w, a).ToArray();
        a.Day256 = Calendar.DayOfYear(-3000);
        Check(!Climate.FertNow(w, a).SequenceEqual(before), "a century later the deserts are drier (the cache follows the date)");
    }
}
