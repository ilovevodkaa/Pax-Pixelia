using System.Collections.Generic;
using System.Text;
using System.Threading;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>When the leading nation entered an era: the tick (→ real time at speed 3) and the calendar day.</summary>
public readonly record struct EraMark(int Era, long Tick, long Day256, long StepDay256)
{
    /// <summary>Calendar days a tick covers when the era began, × 100.</summary>
    public long DaysPerTickX100 => StepDay256 * 100 / Calendar.DayUnit;
    public CalendarDate Date => Calendar.DateOf(Day256);
    /// <summary>Real hours at speed 3 × 100.</summary>
    public long HoursX100 => Clock.HoursX100AtReference(Tick);
}

/// <summary>
/// Headless pacing run: every nation is a bot, the rules run tick by tick until the leader enters the last era
/// (or a limit). Answers «how long is a game on this preset, and when does the calendar reach each era?».
/// </summary>
public static class Pacing
{
    /// <param name="content">The event deck, when given, deals to the bots as in a real game (its science effects count).</param>
    public static List<EraMark> Run(WorldData w, Data.Nation[] roster, int pacePermille, long maxTicks, CancellationToken cancel = default,
                                    Content.ContentDb content = null)
    {
        var s = NationGen.CreateInitialState(w, roster);
        foreach (var n in s.Nat) n.Control = NationControl.Bot;
        s.Pace = Eras.ClampPace(pacePermille);
        Simulation.Begin(w, s);
        if (content != null) s.Events = new SimEvents(content, w, s);
        var marks = new List<EraMark> { new(0, 0, s.Day256, s.DateStep) };
        int era = 0;
        while (s.Tick < maxTicks && era < Eras.Last)
        {
            var r = Simulation.Step(w, s, null);
            if ((s.Tick & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
            if (!r.EraChanged) continue;
            int lead = Science.LeaderEra(s);
            while (era < lead) marks.Add(new EraMark(++era, s.Tick, s.Day256, s.DateStep));
        }
        return marks;
    }

    /// <summary>A text table: era, calendar date, real time at speed 3, calendar days per tick.</summary>
    public static string Format(string title, IReadOnlyList<EraMark> marks)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        foreach (var m in marks)
        {
            var d = m.Date;
            sb.Append("  ").Append(Eras.Name(m.Era).PadRight(16))
              .Append(Calendar.Text(d, true).PadRight(26))
              .Append(Hours(m.HoursX100).PadRight(13))
              .Append($"{m.DaysPerTickX100 / 100}.{m.DaysPerTickX100 % 100:00} дн/такт").AppendLine();
        }
        return sb.ToString();
    }

    public static string Hours(long hoursX100) => $"{hoursX100 / 100} ч {hoursX100 % 100 * 60 / 100:00} мин";
}
