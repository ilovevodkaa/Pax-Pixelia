using System;

namespace PaxPixelia.Sim;

/// <summary>A calendar day: astronomical year (0 = 1 до н. э.), month 1..12, day 1..31.</summary>
public readonly record struct CalendarDate(int AstroYear, int Month, int Day)
{
    /// <summary>Year as players count it: negative = до н. э., there is no year 0.</summary>
    public int Year => AstroYear > 0 ? AstroYear : AstroYear - 1;
    /// <summary>Months since the epoch year, for «a month has passed» checks.</summary>
    public long MonthIndex => AstroYear * 12L + Month - 1;
}

/// <summary>
/// The calendar is a story told on top of the tick clock (IDEAS principle 1): the date is a function of the leading
/// nation's progress, not of elapsed time. Day256 = days × 256 since 1 January 4000 до н. э. (proleptic Gregorian).
/// Each era's calendar span is spread over 16 progress steps with geometrically shrinking lengths (the 11×17 table
/// below, built once from <see cref="Eras"/> with integer maths), so eras begin exactly on their historical years
/// whatever the pace preset, and the date slows smoothly from ≈ a month per tick to a fraction of a day per tick.
/// </summary>
public static class Calendar
{
    public const int Steps = 16;
    public const long DayUnit = 256;

    static readonly long EpochDays = DaysFromCivil(-3999, 1, 1);
    static readonly long[][] Table = BuildTable();

    // ------------------------------------------------------------------ progress → date

    /// <summary>The calendar day (Day256) that a leader with this progress has reached.</summary>
    public static long DayAt(long progress, int pacePermille)
    {
        if (progress <= 0) return 0;
        int e = Eras.EraOf(progress, pacePermille);
        long span = Math.Max(1, Eras.Span(e, pacePermille));
        long pos = (progress - Eras.Threshold(e, pacePermille)) * Steps;
        long k = pos / span, rem = pos - k * span;
        var row = Table[e];
        if (e < Eras.Last)
        {
            if (k >= Steps) return row[Steps];
            return row[k] + (row[k + 1] - row[k]) * rem / span;
        }
        long slope = row[1] - row[0];   // the Future keeps the pace of the Space age's last step
        return row[0] + slope * k + slope * rem / span;
    }

    /// <summary>Day256 of 1 January of a year (до н. э. negative) — where the era table anchors its rows.</summary>
    public static long DayOfYear(int year) => (DaysFromCivil(year < 0 ? year + 1 : year, 1, 1) - EpochDays) * DayUnit;

    // ------------------------------------------------------------------ day ↔ civil date

    public static CalendarDate DateOf(long day256)
    {
        CivilFromDays(day256 / DayUnit + EpochDays, out long y, out int m, out int d);
        return new CalendarDate((int)y, m, d);
    }

    /// <summary>Days from 1970-01-01 to a proleptic Gregorian date (H. Hinnant's algorithm, floor division for BC).</summary>
    public static long DaysFromCivil(long y, int m, int d)
    {
        y -= m <= 2 ? 1 : 0;
        long era = (y >= 0 ? y : y - 399) / 400;
        long yoe = y - era * 400;
        long doy = (153 * (m > 2 ? m - 3 : m + 9) + 2) / 5 + d - 1;
        long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    public static void CivilFromDays(long z, out long y, out int m, out int d)
    {
        z += 719468;
        long era = (z >= 0 ? z : z - 146096) / 146097;
        long doe = z - era * 146097;
        long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        long mp = (5 * doy + 2) / 153;
        d = (int)(doy - (153 * mp + 2) / 5 + 1);
        m = (int)(mp < 10 ? mp + 3 : mp - 9);
        y = yoe + era * 400 + (m <= 2 ? 1 : 0);
    }

    // ------------------------------------------------------------------ text (Russian)

    static readonly string[] MonthNom = { "январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь" };
    static readonly string[] MonthGen = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };

    /// <summary>«3200 до н. э.», «620 н. э.», «1893».</summary>
    public static string YearText(int year) => year < 0 ? $"{-year} до н. э." : year < 1000 ? $"{year} н. э." : year.ToString();

    /// <summary>«март 3200 до н. э.» while a tick spans weeks, «12 марта 1893» once days can be told apart.</summary>
    public static string Text(CalendarDate d, bool withDay) =>
        withDay ? $"{d.Day} {MonthGen[d.Month - 1]} {YearText(d.Year)}" : $"{MonthNom[d.Month - 1]} {YearText(d.Year)}";

    /// <summary>Show the day once a tick is shorter than 15 days; back to months only above 17 (no flicker at the edge).</summary>
    public static bool WantsDay(long stepDay256, bool shownWithDay) =>
        shownWithDay ? stepDay256 <= 17 * DayUnit : stepDay256 < 15 * DayUnit;

    // ------------------------------------------------------------------ table

    static long[][] BuildTable()
    {
        int n = Eras.Count;
        var rows = new long[n][];
        for (int e = 0; e < n - 1; e++)
        {
            long from = DayOfYear(Eras.All[e].StartYear), to = DayOfYear(Eras.All[e + 1].StartYear);
            var row = rows[e] = new long[Steps + 1];
            // weights q^k with q = 16th root of the era's shape, in Q30 fixed point
            ulong q = Root16(Eras.All[e].ShapePermille), w = 1UL << 30, sum = 0;
            var ws = new ulong[Steps];
            for (int k = 0; k < Steps; k++) { ws[k] = w; sum += w; w = (ulong)((UInt128)w * q >> 30); }
            long span = to - from, acc = 0;
            row[0] = from;
            for (int k = 0; k < Steps; k++) { acc += (long)((UInt128)(ulong)span * ws[k] / sum); row[k + 1] = from + acc; }
            row[Steps] = to;
        }
        var last = rows[n - 2];
        long slope = last[Steps] - last[Steps - 1];
        rows[n - 1] = new long[Steps + 1];
        for (int k = 0; k <= Steps; k++) rows[n - 1][k] = last[Steps] + k * slope;
        return rows;
    }

    /// <summary>q in Q30 with q¹⁶ ≈ permille/1000, by bisection on integers.</summary>
    static ulong Root16(int permille)
    {
        ulong target = ((ulong)permille << 30) / 1000, lo = 0, hi = 2UL << 30;
        while (hi - lo > 1)
        {
            ulong mid = (lo + hi) / 2, p = 1UL << 30;
            for (int i = 0; i < Steps; i++) p = (ulong)((UInt128)p * mid >> 30);
            if (p <= target) lo = mid; else hi = mid;
        }
        return lo;
    }
}
