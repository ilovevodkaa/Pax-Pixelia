using System;
using System.Linq;
using PaxPixelia.Sim;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>The tick clock, the integer tick pump and the calendar (dates, eras, text).</summary>
public static class TimeTests
{
    public static void Run()
    {
        Section("clock: integer tick pump");
        foreach (int speed in new[] { 1, 3, 5 })
        {
            int tps = Clock.TicksPerSecond[speed];
            var counts = new[] { 30, 60, 144, 0 }.Select(fps => Pump(tps, fps)).ToArray();
            Check(counts.All(c => c == 10 * tps), $"speed {speed}: 10 s = {10 * tps} ticks at 30/60/144 fps and ragged frames ({string.Join(", ", counts)})");
        }
        var hitch = new TickPump();
        Check(hitch.Advance(5_000_000, Clock.TicksPerSecond[5]) == 10, "a 5 s hitch runs at most 0.25 s of ticks (10 at speed 5)");
        Check(Clock.TicksPerSecond[3] * 60 / Clock.CycleTicks == 120, "speed 3: 120 rules cycles a minute (one every 0.5 s, the old «год» pace)");
        Check(Enumerable.Range(0, 12).Count(t => Clock.IsCycleTick(t)) == 3 && !Clock.IsCycleTick(0) && Clock.IsCycleTick(3), "a cycle every 4th tick, the first after a full cycle");

        Section("calendar: days ↔ dates");
        bool round = true;
        for (long z = Calendar.DaysFromCivil(-3999, 1, 1); z < Calendar.DaysFromCivil(2400, 1, 1) && round; z += 17)
        {
            Calendar.CivilFromDays(z, out long y, out int m, out int d);
            round = Calendar.DaysFromCivil(y, m, d) == z && m is >= 1 and <= 12 && d is >= 1 and <= 31;
        }
        Check(round, "days → date → days round-trips from 4000 до н. э. to 2400");
        Check(Calendar.DateOf(0) == new CalendarDate(-3999, 1, 1) && Calendar.DateOf(0).Year == -4000, "day 0 = 1 января 4000 до н. э.");
        var lastBc = Calendar.DateOf(Calendar.DayOfYear(1) - Calendar.DayUnit);
        var firstAd = Calendar.DateOf(Calendar.DayOfYear(1));
        Check(lastBc.Year == -1 && lastBc.Month == 12 && lastBc.Day == 31 && firstAd.Year == 1 && firstAd.Month == 1 && firstAd.Day == 1,
            $"31 декабря 1 до н. э. is followed by 1 января 1 н. э. — no year 0 ({Calendar.Text(lastBc, true)} → {Calendar.Text(firstAd, true)})");
        Check(Calendar.DateOf(Calendar.DayOfYear(1893) + 70 * Calendar.DayUnit) is { Year: 1893, Month: 3, Day: 12 }, "1893 + 70 days = 12 марта 1893");
        Check(Calendar.Text(new CalendarDate(-3199, 3, 5), false) == "март 3200 до н. э.", $"month text: «{Calendar.Text(new CalendarDate(-3199, 3, 5), false)}»");
        Check(Calendar.Text(new CalendarDate(1893, 3, 12), true) == "12 марта 1893", $"day text: «{Calendar.Text(new CalendarDate(1893, 3, 12), true)}»");
        Check(Calendar.Text(new CalendarDate(620, 5, 1), true) == "1 мая 620 н. э.", $"early AD keeps «н. э.»: «{Calendar.Text(new CalendarDate(620, 5, 1), true)}»");
        Check(Calendar.WantsDay(10 * Calendar.DayUnit, false) && !Calendar.WantsDay(30 * Calendar.DayUnit, true) && Calendar.WantsDay(16 * Calendar.DayUnit, true) && !Calendar.WantsDay(16 * Calendar.DayUnit, false),
            "day format below 15 days a tick, month format above 17 (hysteresis between)");

        Section("calendar: eras");
        Check(Eras.Count == 11 && Eras.Name(0) == "Первобытная" && Eras.Name(3) == "Средневековье" && Eras.Name(6) == "Индустриальная" && Eras.Name(10) == "Будущее", "11 eras, Первобытная … Будущее");
        foreach (int pace in new[] { 480, 1000, 1600 })
        {
            bool exact = true, mono = true;
            for (int e = 0; e < Eras.Count; e++)
            {
                long thr = Eras.Threshold(e, pace);
                exact &= Calendar.DayAt(thr, pace) == Calendar.DayOfYear(Eras.All[e].StartYear) && Eras.EraOf(thr, pace) == e && (e == 0 || Eras.EraOf(thr - 1, pace) == e - 1);
            }
            long prev = -1;
            for (long p = 0; p < Eras.Threshold(Eras.Last, pace) + 200_000; p += 97) { long d = Calendar.DayAt(p, pace); mono &= d >= prev; prev = d; }
            Check(exact, $"pace {pace}‰: every era starts on 1 January of its year ({string.Join(", ", Eras.All.Select(x => x.StartYear))})");
            Check(mono, $"pace {pace}‰: the date never goes back as progress grows (also past the last era)");
        }
        Check(Eras.Threshold(5, 1600) == Eras.Threshold(5, 1000) * 1600 / 1000 && Eras.Threshold(5, 480) == Eras.Threshold(5, 1000) * 480 / 1000, "presets scale every era's cost");
        long mid = (Eras.Threshold(1, 1000) + Eras.Threshold(2, 1000)) / 2;
        Info($"midway through Древний мир the date is {Calendar.Text(Calendar.DateOf(Calendar.DayAt(mid, 1000)), true)}");
    }

    /// <summary>Ticks after 10 s of frames at a frame rate (0 = ragged frames of 1…40 ms, summing to exactly 10 s).</summary>
    static int Pump(int tps, int fps)
    {
        var pump = new TickPump();
        long left = 10 * TickPump.MicrosPerSecond; int ticks = 0; uint h = 12345;
        while (left > 0)
        {
            long frame = fps > 0 ? TickPump.MicrosPerSecond / fps + (fps == 144 ? 1 : 0) : 1000 + (h = h * 1103515245 + 12345) % 39_000;
            frame = Math.Min(frame, left);
            ticks += pump.Advance(frame, tps);
            left -= frame;
        }
        return ticks;
    }
}
