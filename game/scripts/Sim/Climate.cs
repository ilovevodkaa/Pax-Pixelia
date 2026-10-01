using System;
using System.Runtime.CompilerServices;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>What the climate does to a land province (from its biome and neighbours, fixed per world).</summary>
public enum ClimateKind : byte { None, Desert, DesertEdge, Cold, GlacierEdge }

/// <summary>The three climate epochs at a calendar year, ‰ of their full strength.</summary>
public readonly record struct ClimateNow(int Green, int Frost, int Warm);

/// <summary>
/// «Дышащая планета»: the world changes with the calendar, as it did after 4000 до н. э. Three epochs, each a function
/// of the year (so of the leader's progress — nothing is stored; saves and the lockstep are untouched):
///  • the Green Sahara: deserts are savanna at the start and dry out by 2500 до н. э. (+400 ‰ fertility → none);
///    the steppes beside them lose a little of their green too;
///  • the Little Ice Age, 1300–1850 (strongest about 1650): tundra, taiga and the cold steppe lose up to 35 %;
///  • the warming from 1880: the edges of the glaciers thaw into poor but living land (+200 ‰), the steppes beside the
///    deserts dry (−30 %).
/// The rules read <see cref="FertNow"/> where they read the land's fertility for people (capacity, growth, the tribe's
/// food and its choice of a site); the map tints the provinces from <see cref="Look"/>.
/// </summary>
public static class Climate
{
    public const int GreenEnd = -2500, FrostStart = 1300, FrostPeak = 1650, FrostEnd = 1850, WarmStart = 1880, WarmFull = 2100;
    /// <summary>The climate is recomputed every this many calendar years.</summary>
    public const int StepYears = 5;

    const int Desert = 14, Glacier = 1;

    static readonly ConditionalWeakTable<WorldData, ClimateKind[]> Kinds = new();
    static readonly ConditionalWeakTable<GameState, Cache> Caches = new();

    sealed class Cache { public int Step = int.MinValue; public int[] Fert; }

    // ------------------------------------------------------------------ the world

    /// <summary>The climate class of every province (deserts, the green land beside them, the cold north, the glacier edge).</summary>
    public static ClimateKind[] KindsOf(WorldData w) => Kinds.GetValue(w, Classify);

    static ClimateKind[] Classify(WorldData w)
    {
        var k = new ClimateKind[w.P];
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] != 1) continue;
            int b = w.PBiome[p];
            if (b == Desert) { k[p] = ClimateKind.Desert; continue; }
            if (b == Glacier)
            {
                foreach (int q in w.Adj[p]) if (w.PLand[q] == 1 && w.PBiome[q] != Glacier) { k[p] = ClimateKind.GlacierEdge; break; }
                continue;
            }
            bool byDesert = false;
            foreach (int q in w.Adj[p]) if (w.PLand[q] == 1 && w.PBiome[q] == Desert) { byDesert = true; break; }
            if (byDesert && b is 6 or 9 or 10 or 11 or 13) k[p] = ClimateKind.DesertEdge;
            else if (b is 4 or 5 or 6) k[p] = ClimateKind.Cold;
        }
        return k;
    }

    // ------------------------------------------------------------------ the time

    public static int Year(GameState s) => Calendar.DateOf(s.Day256).Year;

    /// <summary>Strength of each epoch in a calendar year (до н. э. negative).</summary>
    public static ClimateNow At(int year)
    {
        int green = year <= -4000 ? 1000 : year >= GreenEnd ? 0 : (GreenEnd - year) * 1000 / (GreenEnd + 4000);
        int frost = year <= FrostStart || year >= FrostEnd ? 0
            : year <= FrostPeak ? (year - FrostStart) * 1000 / (FrostPeak - FrostStart)
            : (FrostEnd - year) * 1000 / (FrostEnd - FrostPeak);
        int warm = year <= WarmStart ? 0 : year >= WarmFull ? 1000 : (year - WarmStart) * 1000 / (WarmFull - WarmStart);
        return new ClimateNow(green, frost, warm);
    }

    /// <summary>The climate of the state's calendar, in <see cref="StepYears"/>-year steps (what the rules use).</summary>
    public static ClimateNow Now(GameState s) => At(StepOf(Year(s)) * StepYears);

    static int StepOf(int year) => year >= 0 ? year / StepYears : -((-year + StepYears - 1) / StepYears);   // floor, also before our era

    /// <summary>Fertility (‰) of a province of the given kind under a climate.</summary>
    public static int Fert(ClimateKind kind, int baseFert, in ClimateNow c)
    {
        int f = kind switch
        {
            ClimateKind.Desert => baseFert + c.Green * 400 / 1000,
            ClimateKind.DesertEdge => baseFert + c.Green * 100 / 1000 - baseFert * c.Warm * 300 / 1_000_000,
            ClimateKind.Cold => baseFert - baseFert * c.Frost * 350 / 1_000_000,
            ClimateKind.GlacierEdge => baseFert + c.Warm * 200 / 1000,
            _ => baseFert,
        };
        return IntMath.Clamp(f, 0, 1000);
    }

    /// <summary>Every province's fertility (‰) now — the climate applied to WorldFacts.FertPm, cached per climate step.
    /// The array is shared: read it, never write it.</summary>
    public static int[] FertNow(WorldData w, GameState s)
    {
        var cache = Caches.GetValue(s, _ => new Cache());
        int step = StepOf(Year(s));
        if (cache.Step == step && cache.Fert != null && cache.Fert.Length == w.P) return cache.Fert;
        var baseFert = WorldFacts.Of(w).FertPm;
        var kinds = KindsOf(w);
        var c = At(step * StepYears);
        var f = cache.Fert is { Length: var n } && n == w.P ? cache.Fert : new int[w.P];
        for (int p = 0; p < w.P; p++) f[p] = Fert(kinds[p], baseFert[p], c);
        cache.Fert = f; cache.Step = step;
        return f;
    }

    /// <summary>A key that changes whenever the climate does (the map re-tints then).</summary>
    public static int Key(GameState s) => StepOf(Year(s));

    // ------------------------------------------------------------------ what it looks like and what is said

    /// <summary>Map tint of province p, each 0..255: R the drying of the steppe, G the green of the desert, B the frost,
    /// A the thaw of the glacier edge.</summary>
    public static (byte Dry, byte Green, byte Frost, byte Thaw) Look(WorldData w, GameState s, int p)
    {
        var c = Now(s);
        static byte B(int pm) => (byte)IntMath.Clamp(pm * 255 / 1000, 0, 255);
        return KindsOf(w)[p] switch
        {
            ClimateKind.Desert => (0, B(c.Green), 0, 0),
            ClimateKind.DesertEdge => (B(c.Warm * 7 / 10), B(c.Green / 3), 0, 0),
            ClimateKind.Cold => (0, 0, B(c.Frost * 6 / 10), 0),
            ClimateKind.GlacierEdge => (0, 0, 0, B(c.Warm)),
            _ => (0, 0, 0, 0),
        };
    }

    /// <summary>One line for the province card while the climate changes it, else null: «Пустыня наступает: −18 % плодородия».</summary>
    public static string Describe(WorldData w, GameState s, int p)
    {
        if ((uint)p >= (uint)w.P) return null;
        var kind = KindsOf(w)[p];
        int baseFert = WorldFacts.Of(w).FertPm[p], now = FertNow(w, s)[p];
        if (kind == ClimateKind.None || now == baseFert) return null;
        var c = Now(s);
        string what = kind switch
        {
            ClimateKind.Desert => "Зелёная пустыня: дожди уходят",
            ClimateKind.DesertEdge when c.Warm > 0 => "Засуха: пустыня наступает",
            ClimateKind.DesertEdge => "Дожди ещё идут, но слабеют",
            ClimateKind.Cold => "Малый ледниковый период: зимы длиннее",
            ClimateKind.GlacierEdge => "Ледник тает: открывается земля",
            _ => "",
        };
        return $"{what} · плодородие {baseFert / 10} % → {now / 10} %";
    }

    /// <summary>The epoch a year belongs to for the chronicle: 0 green, 1 dry (the Sahara gone), 2 frost, 3 thaw of the
    /// frost, 4 warming. The Game announces each change for the player.</summary>
    public static int Stage(int year) =>
        year < -3250 ? 0 : year < FrostStart ? 1 : year < FrostPeak ? 2 : year < WarmStart ? 3 : 4;

    public static string StageText(int stage) => stage switch
    {
        1 => "Дожди уходят на юг: зелёные пустыни сохнут, и людям там уже не прокормиться",
        2 => "Зимы стали длиннее: на севере начинается Малый ледниковый период",
        3 => "Холода отступают: северные земли снова кормят людей",
        4 => "Лето всё жарче: ледники тают, а пустыни наступают на степь",
        _ => null,
    };
}
