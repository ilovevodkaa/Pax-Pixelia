using System;
using Godot;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// «Умное время» (Settings → Интерфейс): when nothing happens for <see cref="QuietSeconds"/> — no chronicle line, no
/// open choice, a study chosen, no click or key — the clock runs at speed 5; the first news, choice or touch brings it
/// back to the player's speed. Only the local clock's rate changes (TickPump): no command, nothing in the journal, the
/// same ticks — so replays, saves and the lockstep do not notice.
/// </summary>
public partial class Game
{
    public const double QuietSeconds = 4;

    double _quiet;   // real seconds since the last thing worth slowing down for

    /// <summary>Raised when the hurry starts or stops (the speed pips light up).</summary>
    public event Action<bool> HurryChanged;

    /// <summary>On by the setting; command-line runs (tests that time the clock) only with --smarttime.</summary>
    public bool SmartTimeOn => Settings.CliMode ? Cli.Has("smarttime") : Settings.I?.Get<bool>(Settings.Ui, "smart_time") ?? false;

    /// <summary>The clock is hurrying through a quiet stretch.</summary>
    public bool Hurrying { get; private set; }

    /// <summary>Something happened: back to the player's speed for a while.</summary>
    void Calm()
    {
        _quiet = 0;
        if (Hurrying)
        {
            Hurrying = false; HurryChanged?.Invoke(false);
            if (Settings.CliMode) GD.Print($"smart time: news, back to speed {State.Speed} at tick {State.Tick}");
        }
    }

    /// <summary>The tick rate this frame runs at (the player's speed, or 5 in a quiet stretch).</summary>
    int ClockSpeed(double delta)
    {
        bool can = SmartTimeOn && State.Speed < Clock.MaxSpeed && !BlitzOver && PendingChoice() == null
                   && !(State.Nat[Viewer].Researching < 0 && Techs.HasOpen(State.Nat[Viewer]));
        if (!can) { Calm(); return State.Speed; }
        _quiet += delta;
        bool hurry = _quiet >= QuietSeconds;
        if (hurry != Hurrying)
        {
            Hurrying = hurry; HurryChanged?.Invoke(hurry);
            if (Settings.CliMode) GD.Print($"smart time: {(hurry ? "quiet, speed 5" : "back to speed " + State.Speed)} at tick {State.Tick}");
        }
        return hurry ? Clock.MaxSpeed : State.Speed;
    }

    public override void _Input(InputEvent e)
    {
        // the player's hand on the mouse or the keys: they are playing, not waiting
        if (e is InputEventMouseButton { Pressed: true } or InputEventKey { Pressed: true }) Calm();
    }
}
