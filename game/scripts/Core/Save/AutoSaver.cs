using System;
using System.Threading.Tasks;
using Godot;

namespace PaxPixelia.Core.Save;

/// <summary>
/// Child of the Game autoload (it outlives the scenes). Counts the playtime of the running game, writes the calendar
/// autosave — every <see cref="GapYears"/> in-game years but never sooner than <see cref="MinGapMs"/> of real time after
/// the previous one — and saves synchronously when the window is closed (the cross or Alt+F4: AutoAcceptQuit is off,
/// the save takes a few ms, then the game quits). The pause menu's «В главное меню» / «Выйти из игры» call
/// <see cref="SaveOnLeave"/>. Calendar autosaves are off in command-line runs (tests, screenshots) unless --autosave;
/// --autosave-seconds=S and --autosave-years=Y shorten the gaps (the save test).
/// </summary>
public partial class AutoSaver : Node
{
    public const int DefaultGapYears = 10;
    public const long DefaultMinGapMs = 3 * 60 * 1000;
    const long DaysPerYearX100 = 36525;

    /// <summary>Calendar autosaves are written (the saves on leaving the game always are).</summary>
    public bool Enabled { get; set; }
    public int GapYears { get; set; } = DefaultGapYears;
    public long MinGapMs { get; set; } = DefaultMinGapMs;
    /// <summary>A calendar autosave is being written.</summary>
    public bool Busy { get; private set; }
    /// <summary>Calendar autosaves written this session (tests).</summary>
    public int Count { get; private set; }
    /// <summary>The window's close request quits after saving (tests turn it off to check the save).</summary>
    public bool QuitOnClose { get; set; } = true;
    /// <summary>What the last close request saved (tests).</summary>
    public SaveResult LastCloseSave { get; private set; }

    long _lastDay256, _lastSaveMs, _lastFrameMs;
    bool _focused = true;

    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false;   // the window's cross saves the game first (_Notification)
        Enabled = !CommandLineRun() || Cli.Has("autosave");
        if (Cli.Has("autosave-seconds")) MinGapMs = (long)(Math.Max(0f, Cli.Float("autosave-seconds", 180)) * 1000);
        if (Cli.Has("autosave-years")) GapYears = Math.Max(1, Cli.Int("autosave-years", DefaultGapYears));
        Game.I.WorldReady += Restart;
        _lastFrameMs = (long)Time.GetTicksMsec();
    }

    public override void _ExitTree()
    {
        if (Game.I != null) Game.I.WorldReady -= Restart;
    }

    /// <summary>Command-line runs: any user argument other than the front-end's own (--front*, --no-motion, --savedir).</summary>
    static bool CommandLineRun()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (!a.StartsWith("--front") && a != "--no-motion" && !a.StartsWith("--savedir")) return true;
        return false;
    }

    /// <summary>A new or loaded game: the gaps count from now.</summary>
    void Restart()
    {
        var g = Game.I;
        _lastDay256 = g.State?.Day256 ?? 0;
        _lastSaveMs = (long)Time.GetTicksMsec();
    }

    public override void _Process(double delta)
    {
        var g = Game.I;
        long now = (long)Time.GetTicksMsec();
        long frame = now - _lastFrameMs;
        _lastFrameMs = now;
        if (!g.IsReady) return;
        // playtime: real time with the game open, except while the window sits unfocused on a paused game
        if ((_focused || !g.State.Paused) && frame > 0 && frame < 60_000) g.PlaytimeMs += frame;
        if (Enabled && !Busy && Due(g, now)) _ = Autosave();
    }

    bool Due(Game g, long now) =>
        now - _lastSaveMs >= MinGapMs && (g.State.Day256 - _lastDay256) * 100 >= GapYears * DaysPerYearX100 * Sim.Calendar.DayUnit;

    /// <summary>Write the calendar autosave now (rotating slots).</summary>
    public async Task<SaveResult> Autosave()
    {
        var g = Game.I;
        if (!g.IsReady || Busy) return SaveResult.Fail("busy");
        Busy = true;
        Restart();
        try
        {
            var r = await g.SaveAsync(SaveKind.Auto);
            if (r.Ok) Count++;
            return r;
        }
        finally { Busy = false; }
    }

    /// <summary>«В главное меню», «Выйти из игры», loading another save: the running game goes to an autosave slot
    /// (never over <paramref name="keep"/>, the save about to be loaded).</summary>
    public async Task<SaveResult> SaveOnLeave(string keep = null)
    {
        var g = Game.I;
        if (!g.IsReady) return SaveResult.Fail("Партия ещё не началась");
        var r = await g.SaveAsync(SaveKind.Exit, keep: keep);
        Restart();
        return r;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn) _focused = true;
        else if (what == NotificationApplicationFocusOut) _focused = false;
        else if (what == NotificationWMCloseRequest) CloseRequested();
    }

    void CloseRequested()
    {
        var g = Game.I;
        if (g != null && g.IsReady)
        {
            var r = LastCloseSave = g.SaveNow(SaveKind.Exit);
            if (!r.Ok) GD.PushError($"save: the save on closing the window failed: {r.Error}");
        }
        if (QuitOnClose) GetTree().Quit();
    }
}
