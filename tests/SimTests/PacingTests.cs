using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using PaxPixelia.Core;
using PaxPixelia.Core.Nations;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>
/// Pacing: bots only, the real rules. Quick check (default): the first two era changes on «Быстрая» land on their
/// calendar years and on time. Full report (argument «pacing»): all presets to the Future, printed and checked
/// against the targets (Быстрая ≈ 12 h, Обычная ≈ 25 h, Эпическая ≈ 40 h at speed 3).
/// </summary>
public static class PacingTests
{
    public static void Run(WorldData w, bool full)
    {
        Section(full ? "pacing: full report" : "pacing: quick check (run with «pacing» for the full report)");
        var roster = NationRoster.Build(GameSetup.Default(w.Seed));
        if (!full)
        {
            var sw = Stopwatch.StartNew();
            var marks = Pacing.Run(w, roster, GameSetup.PaceQuick, Clock.TicksFor(3 * 3600), default, TestContent.Db);
            Info(Pacing.Format($"Быстрая, first 3 h ({sw.ElapsedMilliseconds} ms):", marks.Take(3).ToList()).TrimEnd());
            Check(marks.Count >= 3, "the leader reaches Античность within 3 h on «Быстрая»");
            if (marks.Count >= 3) CheckDates(marks.Take(3).ToArray());
            return;
        }

        var presets = new[] { ("Быстрая", GameSetup.PaceQuick, 12.0), ("Обычная", GameSetup.PaceNormal, 25.0), ("Эпическая", GameSetup.PaceEpic, 40.0) };
        var watch = Stopwatch.StartNew();
        var runs = presets.Select(p => Task.Run(() => Pacing.Run(w, roster, p.Item2, Clock.TicksFor(60 * 3600), default, TestContent.Db))).ToArray();
        Task.WaitAll(runs);
        Info($"{watch.ElapsedMilliseconds} ms for the three presets");
        for (int i = 0; i < presets.Length; i++)
        {
            var (name, pace, hours) = presets[i];
            var marks = runs[i].Result;
            Console.Write(Pacing.Format($"   {name} ({pace}‰):", marks));
            bool done = marks[^1].Era == Eras.Last;
            double h = marks[^1].HoursX100 / 100.0;
            Check(done && Math.Abs(h - hours) <= hours * .12, $"{name}: the Future after {h:F1} h at speed 3 (target ≈ {hours} h ± 12%)");
            CheckDates(marks.ToArray());
        }
    }

    /// <summary>Every era mark within a cycle's glide of 1 January of its year.</summary>
    static void CheckDates(EraMark[] marks)
    {
        var off = marks.Skip(1).Where(m => Math.Abs(m.Day256 - Calendar.DayOfYear(Eras.All[m.Era].StartYear)) > 400 * Calendar.DayUnit).ToList();
        Check(off.Count == 0, off.Count == 0 ? "each era begins on its historical year (±1 year)"
            : "era dates off: " + string.Join(", ", off.Select(m => $"{Eras.Name(m.Era)} {Calendar.Text(m.Date, true)}")));
    }
}
