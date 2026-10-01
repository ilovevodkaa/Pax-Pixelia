using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Content;
using PaxPixelia.Sim;
using PaxPixelia.UI;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// The game's sounds, hung on Game.I events so no module has to know about audio (child of the Sfx autoload):
/// <list type="bullet">
/// <item>map — province select «фишка» (a wooden piece on the board), deselect = panel close, zoom steps (the pitch
/// follows the direction), map mode = tab, the fog eye = toggle;</item>
/// <item>time — pause / unpause (a metal latch) and speed 1–5 (switches from light to heavy); none while the pause menu
/// opens or closes (it pauses the game itself); the ambience «freezes» behind a low-pass while paused;</item>
/// <item>the player's commands, read from <see cref="Game.Journal"/> (only executed ones get there): claim (a short
/// stinger), build (hammer), geologists (pick; ore rings), scouts out, cheat gold;</item>
/// <item>chronicle notes (<see cref="Game.Notified"/>): deck events by <see cref="EventSounds"/> (the event is found by
/// the viewer's FiredCount moving), scouts back, construction complete, a new nation met (stinger), era (stinger with a
/// breath of silence, <see cref="Game.EraChanged"/>), anything else a soft note;</item>
/// <item>the event window (<see cref="Game.ChoiceChanged"/>): a book opens / closes; its stinger plays on opening;</item>
/// <item>toasts: refusal = error, the scout target prompt = question, info = glass.</item>
/// </list>
/// Nothing sounds for 0.6 s after WorldReady (the capital is selected and the camera placed by code). At speed 5 the
/// per-sound intervals, the per-frame merge in Sfx and the stinger queue keep bursts to one sound.
/// </summary>
public partial class GameSounds : Node
{
    const double QuietAfterWorld = .6;
    const ulong MenuGraceFrames = 2;

    Game _g;
    GameState _state;   // the game the fields below describe
    int _journal, _era, _zoom = -1, _speed, _selected = -1, _choice = -1;
    bool _paused, _fog;
    int[] _fired = Array.Empty<int>();
    double _quietUntil;
    ulong _cmdFrame, _menuFrame;
    CmdType _cmdType;

    static double Now => Time.GetTicksMsec() / 1000.0;
    bool Quiet => Now < _quietUntil;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Callable.From(Attach).CallDeferred();   // the Game autoload is registered after Sfx
    }

    void Attach()
    {
        _g = Game.I;
        if (_g == null) return;
        _g.WorldReady += OnWorldReady;
        _g.GameEnded += OnGameEnded;
        _g.ProvinceSelected += OnSelected;
        _g.CameraMoved += OnCamera;
        _g.TimeControlChanged += OnTime;
        _g.MapModeChanged += OnMode;
        _g.FogChanged += OnFog;
        _g.Notified += OnNotified;
        _g.Toast += OnToast;
        _g.ChoiceChanged += OnChoice;
        _g.EraChanged += OnEra;
        Live();
    }

    public override void _ExitTree()
    {
        if (_g == null) return;
        _g.WorldReady -= OnWorldReady;
        _g.GameEnded -= OnGameEnded;
        _g.ProvinceSelected -= OnSelected;
        _g.CameraMoved -= OnCamera;
        _g.TimeControlChanged -= OnTime;
        _g.MapModeChanged -= OnMode;
        _g.FogChanged -= OnFog;
        _g.Notified -= OnNotified;
        _g.Toast -= OnToast;
        _g.ChoiceChanged -= OnChoice;
        _g.EraChanged -= OnEra;
        _g = null;
    }

    public override void _Process(double delta)
    {
        if (PauseMenu.IsOpen) _menuFrame = Engine.GetProcessFrames();
        if (Live()) PollJournal();
    }

    static void Cue(string key, string cause, float pitch = 1f) => Sfx.I?.Play(key, pitch, 0f, cause);
    static void Sting(string key, int priority, string cause, bool silence = false) => Stingers.I?.Enqueue(key, priority, cause, silence);

    // ------------------------------------------------------------------ game lifecycle

    /// <summary>
    /// Inside a running game? The first event of a new game (WorldReady, or a CLI action raised before our WorldReady
    /// handler) takes the baseline, so nothing the new world starts with is mistaken for a change.
    /// </summary>
    bool Live()
    {
        if (_g == null || !_g.IsReady) return false;
        if (_g.State != _state) Baseline();
        return true;
    }

    void OnWorldReady() => Live();

    void Baseline()
    {
        var s = _state = _g.State;
        _journal = 0;   // the journal starts empty with every game: whatever is in it already was done in this one
        _era = _g.EraIndex;
        _zoom = -1;
        _selected = -1;
        _paused = s.Paused;
        _speed = s.Speed;
        _fog = s.FogEnabled;
        SyncFired();
        _choice = PendingEvent();
        _quietUntil = Now + QuietAfterWorld;
        AudioBuses.SetPaused(this, _paused);
    }

    void OnGameEnded()
    {
        _state = null;
        Stingers.I?.Clear();
        AudioBuses.SetPaused(this, false);
        _choice = -1;
        _selected = -1;
    }

    // ------------------------------------------------------------------ map

    void OnSelected(int p)
    {
        if (!Live()) return;
        int was = _selected;
        _selected = p;
        if (Quiet) return;
        if (p >= 0) Cue("piece", "map.select");
        else if (was >= 0) Cue("close", "map.deselect");
    }

    void OnCamera()
    {
        if (!Live()) return;
        int z = _g.ZoomLevel;
        if (_zoom < 0 || Quiet) { _zoom = z; return; }
        if (z == _zoom) return;
        int dir = Math.Sign(z - _zoom);
        _zoom = z;
        // in = higher, out = lower; a shade by the level so a run of steps climbs (×½ … ×8 → 0.94 … 1.10)
        float pitch = (dir > 0 ? 1.08f : .92f) * (1f + (z - 3) * .02f);
        Cue("zoom", dir > 0 ? "map.zoom-in" : "map.zoom-out", pitch);
    }

    void OnMode(MapMode m)
    {
        if (Live() && !Quiet) Cue("tab", "map.mode");
    }

    void OnFog(IReadOnlyList<int> changed)
    {
        if (changed != null || !Live() || _g.State.FogEnabled == _fog) return;
        _fog = _g.State.FogEnabled;
        if (!Quiet) Cue(_fog ? "toggle_on" : "toggle_off", "map.fog");
    }

    // ------------------------------------------------------------------ time

    void OnTime(bool paused, int speed)
    {
        if (!Live()) return;
        PollJournal();
        if (paused == _paused && speed == _speed) return;
        bool pauseChanged = paused != _paused;
        _paused = paused;
        _speed = speed;
        if (pauseChanged) AudioBuses.SetPaused(this, paused);
        if (Quiet) return;
        // the pause menu pauses and restores the game itself: its own open/close sound speaks for it
        Callable.From(() =>
        {
            if (PauseMenu.IsOpen || Engine.GetProcessFrames() - _menuFrame <= MenuGraceFrames) return;
            if (pauseChanged) Cue(paused ? "pause" : "unpause", paused ? "time.pause" : "time.unpause", paused ? 1f : 1.12f);
            else Cue($"speed_{Math.Clamp(speed, 1, 5)}", $"time.speed{speed}");
        }).CallDeferred();
    }

    // ------------------------------------------------------------------ the player's commands

    /// <summary>Sounds for commands executed since the last look (the journal holds only what really happened).</summary>
    void PollJournal()
    {
        var j = _g.Journal;
        if (j.Count < _journal) _journal = 0;
        for (; _journal < j.Count; _journal++)
        {
            var c = j[_journal];
            if (c.Nation != _g.Viewer) continue;
            string key = c.Type switch
            {
                CmdType.Claim => "claim",
                CmdType.Build => "build",
                CmdType.Survey => (uint)c.A < (uint)_g.State.Ore.Length && _g.State.Ore[c.A] >= 0 ? "ore" : "survey",
                CmdType.ScoutTo or CmdType.ScoutAuto or CmdType.ScoutMove => "scouts_out",
                CmdType.CheatGold => "coins",
                _ => null,
            };
            if (key == null) continue;
            _cmdType = c.Type;
            _cmdFrame = Engine.GetProcessFrames();
            Cue(key, "cmd." + c.Type.ToString().ToLowerInvariant());
        }
    }

    /// <summary>The note that the player's own command writes in this frame (claim → «flag», build → «hammer»…).</summary>
    bool FromCommand(params CmdType[] types) => Engine.GetProcessFrames() == _cmdFrame && Array.IndexOf(types, _cmdType) >= 0;

    // ------------------------------------------------------------------ chronicle, events, eras

    void OnNotified(string icon, string text)
    {
        if (!Live()) return;
        PollJournal();
        int e = TakeFired(icon);
        if (Quiet) return;
        if (e >= 0) { EventSound(e, "event:"); return; }
        // the open choice was answered (or its deadline came): its result note comes just before ChoiceChanged,
        // and the book closing speaks for it
        if (_choice >= 0 && PendingEvent() != _choice && (Def(_choice)?.Icon ?? "feather") == icon) return;
        switch (icon)
        {
            case "history":
                if (_g.EraIndex != _era) return;   // our own era: EraChanged plays the fanfare
                break;
            case "affiliate":
                Sting("stinger_meet", Stingers.Discovery, "note.met");
                return;
            case "map-2":
                Cue("scouts_back", "note.scouts-back");
                return;
            case "hammer":
                if (FromCommand(CmdType.Build)) return;
                Cue("built", "note.built");
                return;
            case "flag":
                if (FromCommand(CmdType.Claim)) return;
                break;
            case "shovel":
                if (FromCommand(CmdType.Survey)) return;
                break;
        }
        Cue("note", "note." + icon);
    }

    void OnChoice()
    {
        if (!Live()) return;
        PollJournal();
        int e = PendingEvent();
        if (e == _choice) return;
        int was = _choice;
        _choice = e;
        SyncFired();   // an opening choice moved its FiredCount without a note
        if (Quiet) return;
        if (e >= 0)
        {
            Cue("book_open", "event.open:" + Def(e)?.Id);
            var s = Classify(e);
            if (s.Stinger) Sting(s.Key, s.Priority, "event:" + Def(e)?.Id);
        }
        else if (was >= 0) Cue("book_close", "event.close:" + Def(was)?.Id);
    }

    void OnEra(int era)
    {
        if (!Live()) return;
        int was = _era;
        _era = era;
        if (era <= was || Quiet) return;
        Sting($"stinger_era_{SoundBank.EraGroup(era)}", Stingers.Era, $"era.{era}", silence: true);
    }

    void OnToast(string text, float seconds, ToastKind kind)
    {
        if (!Live()) return;
        PollJournal();   // «Разведчики выступили…» comes right after the command: let the footsteps win the frame
        if (Quiet) return;
        switch (kind)
        {
            case ToastKind.Error: Cue("error", "toast.refusal"); break;
            case ToastKind.Pick: Cue("question", "toast.pick"); break;
            default: Cue("toast", "toast"); break;
        }
    }

    void EventSound(int e, string cause)
    {
        var s = Classify(e);
        cause += Def(e)?.Id;
        if (s.Stinger) Sting(s.Key, s.Priority, cause); else Cue(s.Key, cause);
    }

    // ------------------------------------------------------------------ the deck

    static EventDef Def(int e) => Game.Content is { } db && (uint)e < (uint)db.Events.Length ? db.Events[e].Def : null;

    static EventSounds.Sound Classify(int e)
    {
        if (Def(e) == null) return new("page", false, 0);
        var rt = Game.Content.Events[e];
        return EventSounds.Classify(rt.Def.Id, rt.Def.Icon, rt.Tone == Tone.Joke, rt.Kind == EventKind.World, rt.Def.Importance);
    }

    int[] FiredNow() => _g.State?.Events is { } ev && (uint)_g.Viewer < (uint)ev.Mem.Length ? ev.Mem[_g.Viewer].FiredCount : null;

    /// <summary>Copy the viewer's FiredCount (never keep the live array).</summary>
    void SyncFired()
    {
        var now = FiredNow();
        if (now == null) { _fired = Array.Empty<int>(); return; }
        if (_fired.Length != now.Length) _fired = new int[now.Length];
        Array.Copy(now, _fired, now.Length);
    }

    int PendingEvent() => _g.State?.Events?.Pending(_g.Viewer) is { } r ? r.Event : -1;

    /// <summary>The deck event behind this note: one whose FiredCount moved since the last note and whose icon matches.</summary>
    int TakeFired(string icon)
    {
        var now = FiredNow();
        if (now == null) return -1;
        if (_fired.Length != now.Length) _fired = new int[now.Length];
        int found = -1;
        for (int i = 0; i < now.Length && found < 0; i++)
            if (now[i] > _fired[i] && (Def(i)?.Icon ?? "feather") == icon) found = i;
        Array.Copy(now, _fired, now.Length);
        return found;
    }
}
