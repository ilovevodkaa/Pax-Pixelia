using System;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// Wave-2 contract: game setup / end, the calendar (months and days instead of whole years), eras, science and the
/// per-game nation roster. UI, map and front-end code against these members; keep them source-compatible.
/// </summary>
public partial class Game
{
    /// <summary>Setup of the running game (null before the first game).</summary>
    public GameSetup Setup { get; private set; }

    /// <summary>Raised after EndGame (return to the main menu).</summary>
    public event Action GameEnded;

    /// <summary>Raised when the calendar day changed (at most once per frame) and after WorldReady: re-read DateText.</summary>
    public event Action DateChanged;

    /// <summary>Raised when the local player's nation entered a new era (arg = the new EraIndex); at most once per frame.</summary>
    public event Action<int> EraChanged;

    /// <summary>The nation this client plays and looks through (fog, leaderboard, top bar).</summary>
    public int Viewer => GameState.LocalPlayer;

    /// <summary>Nations of the running game (player's own design at [0]); use instead of the static Data.Nations.</summary>
    public Data.Nation[] Nations => State?.Nations ?? Data.Nations;

    // ------------------------------------------------------------------ calendar

    /// <summary>Russian date text: «март 3200 до н. э.» while a tick spans weeks, «12 марта 1893» once days can be told apart.</summary>
    public string DateText
    {
        get
        {
            if (State == null) return "";
            long day = State.Day256 / Calendar.DayUnit;
            bool withDay = Calendar.WantsDay(State.DateStep, _dateWithDay);
            if (day != _dateDay || withDay != _dateWithDay || _dateText == null)
            {
                _dateDay = day; _dateWithDay = withDay;
                _dateText = Calendar.Text(State.Date, withDay);
            }
            return _dateText;
        }
    }
    long _dateDay = -1; bool _dateWithDay; string _dateText;

    /// <summary>The calendar date (year negative = до н. э.).</summary>
    public CalendarDate Date => State?.Date ?? default;

    /// <summary>Calendar days per tick right now, ×100 (tooltips: «1 такт ≈ 34 дня»).</summary>
    public int DaysPerTickX100 => State == null ? 0 : (int)(State.DateStep * 100 / Calendar.DayUnit);

    void RaiseDateChanged() => DateChanged?.Invoke();

    // ------------------------------------------------------------------ eras and science

    /// <summary>Current era of the local player (0 = Первобытная … 10 = Будущее) and its name.</summary>
    public int EraIndex => State == null ? 0 : State.Nat[Viewer].Era;
    public string EraName => Eras.Name(EraIndex);
    /// <summary>Name of the next era («» in the last one).</summary>
    public string NextEraName => EraIndex >= Eras.Last ? "" : Eras.Name(EraIndex + 1);
    /// <summary>Way through the current era, 0…1000 (1000 while the stock has passed it but knowledge holds the era back).</summary>
    public int EraProgressPermille => State == null ? 0
        : Eras.EraOf(State.Nat[Viewer].Progress, State.Pace) > State.Nat[Viewer].Era ? 1000
        : Eras.FractionPermille(State.Nat[Viewer].Progress, State.Pace);
    /// <summary>The era the calendar follows (the most advanced nation).</summary>
    public int LeaderEraIndex => State == null ? 0 : Science.LeaderEra(State);

    /// <summary>The local player's science stock (progress points) and its gain per rules cycle.</summary>
    public long ScienceStock => State == null ? 0 : State.Nat[Viewer].Progress;
    public int ScienceRate => State == null ? 0 : State.Nat[Viewer].ScienceRate;
    /// <summary>Research points a cycle: science plus what known technologies add (only the studies go faster).</summary>
    public int ResearchRate => State == null ? 0 : Techs.ResearchRate(State.Nat[Viewer]);
    /// <summary>Where the science comes from (same formula as the rules): sages, lands, shrines, catch-up.</summary>
    public ScienceParts ScienceParts => State == null ? default : Science.Of(State, Viewer);

    /// <summary>Rules cycles per real minute at a speed (turn «per cycle» numbers into «per minute» for tooltips).</summary>
    public static int CyclesPerMinute(int speed) => Clock.TicksPerSecond[Math.Clamp(speed, Clock.MinSpeed, Clock.MaxSpeed)] * 60 / Clock.CycleTicks;
}
