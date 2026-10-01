namespace PaxPixelia.Sim;

/// <summary>
/// One era of the leader's calendar. Cost = progress points a nation needs to leave this era at «Обычная»
/// (GameSetup.PacePermille = 1000; other presets scale every cost). StartYear = the calendar year the leading nation
/// enters it (до н. э. negative). Shape = how the calendar slows down inside the era: days per progress point at the
/// era's end ÷ at its start, in ‰ (the ratio of the neighbouring eras' mean speeds, so the date glides without a jolt).
/// </summary>
public sealed record EraDef(string Name, int StartYear, int Cost, int ShapePermille);

/// <summary>
/// The era table (data only; a future tech tree just feeds the same progress stock). Costs are tuned so a typical
/// leader (≈ 10 points a cycle with its temples, see <see cref="Science"/>) enters the Future after ≈ 25 h at speed 3 on
/// «Обычная»:
/// 20 · 101 · 135 · 151 · 168 · 168 · 185 · 185 · 185 · 202 minutes per era (IDEAS §0 proportions stretched to 25 h).
/// Start years follow IDEAS §0: Средневековье ≈ 620, Индустриальная ≈ 1855.
/// </summary>
public static class Eras
{
    public static readonly EraDef[] All =
    {
        new("Первобытная",     -4000,  22_750, 479),
        new("Древний мир",     -3090, 121_125, 496),
        new("Античность",       -880, 161_375, 527),
        new("Средневековье",     620, 181_500, 394),
        new("Возрождение",      1410, 201_875, 322),
        new("Эпоха пара",       1730, 201_875, 525),
        new("Индустриальная",   1855, 222_000, 575),
        new("Атомная",          1930, 222_000, 806),
        new("Информационная",   1980, 222_000, 1027),
        new("Космическая",      2025, 242_125, 803),
        new("Будущее",          2070,       0, 1000),
    };

    public static int Count => All.Length;
    public static int Last => All.Length - 1;

    /// <summary>Cumulative cost at «Обычная»: Start[e] = points needed to enter era e.</summary>
    static readonly long[] Start = BuildStarts();

    static long[] BuildStarts()
    {
        var s = new long[All.Length + 1];
        for (int e = 0; e < All.Length; e++) s[e + 1] = s[e] + (e < All.Length - 1 ? All[e].Cost : All[e - 1].Cost);
        return s;
    }

    public static int ClampPace(int pacePermille) => IntMath.Clamp(pacePermille, 50, 20_000);

    /// <summary>Progress a nation needs to enter era e at a pace.</summary>
    public static long Threshold(int era, int pacePermille) => Start[IntMath.Clamp(era, 0, All.Length)] * ClampPace(pacePermille) / 1000;

    /// <summary>Points from the start of era e to the next (the Future repeats the Космическая span for the calendar).</summary>
    public static long Span(int era, int pacePermille) => Threshold(era + 1, pacePermille) - Threshold(era, pacePermille);

    /// <summary>The era a progress stock has reached.</summary>
    public static int EraOf(long progress, int pacePermille)
    {
        int e = 0;
        while (e < Last && progress >= Threshold(e + 1, pacePermille)) e++;
        return e;
    }

    /// <summary>Progress inside the current era, 0…1000 (1000 only in the last era's far future).</summary>
    public static int FractionPermille(long progress, int pacePermille)
    {
        int e = EraOf(progress, pacePermille);
        long span = Span(e, pacePermille);
        return span <= 0 ? 1000 : (int)IntMath.Clamp((progress - Threshold(e, pacePermille)) * 1000 / span, 0, 1000);
    }

    public static string Name(int era) => All[IntMath.Clamp(era, 0, Last)].Name;
}
