using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Content;
using PaxPixelia.Core;
using PaxPixelia.Core.Audio;
using PaxPixelia.Sim;
using PaxPixelia.UI;

namespace PaxPixelia.Dev;

/// <summary>
/// --sfxprobe: every sound event of the game once, on a paused game so the simulation adds nothing, each checked
/// through <see cref="Sfx.PlayedSound"/> to make exactly the expected sounds (map select / deselect, zoom in / out
/// by pitch, panning silent, map mode, fog eye, pause / unpause / speed and the Space key, gold, claim, refusal, build,
/// geologists, scouts out / targeting / cancel, scouts back, construction complete, new nation, a system note, a
/// toast, deck events of each sound class, a choice window opened / answered, the era fanfare after its breath of
/// silence, HUD hover / click / tab, the pause menu without a latch). Then 20 s at speed 5 with scouts, panning and
/// zoom steps: no sound faster than its interval, no bursts. PASS/FAIL lines; the exit code is the number of failures.
/// Add --sfxlog to see every sound. Runs headless: ... --headless --audio-driver Dummy --path game -- --sfxprobe
/// </summary>
public partial class SfxProbe : Node
{
    readonly Main _main;
    readonly List<Sfx.Played> _played = new();
    readonly List<(string cause, string key, string reason)> _dropped = new();
    int _pass, _fail;

    Game G => Game.I;
    Hud Hud => _main.Hud;
    int Me => G.Viewer;
    int Cap => G.State.NationCapital[Me];

    public SfxProbe(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "SfxProbe";
        ProcessMode = ProcessModeEnum.Always;
        Sfx.I.PlayedSound += p => _played.Add(p);
        Sfx.I.DroppedSound += (c, k, r) => _dropped.Add((c, k, r));
        Run();
    }

    async void Run()
    {
        try
        {
            while (!G.IsReady) await Frames(1);
            await Seconds(1.2);
            Check("boot: no piece / zoom / time sound for what the code sets up", !_played.Any(p => p.Key is "piece" or "zoom" or "pause" or "unpause" || p.Key.StartsWith("speed_")),
                Tally(_played));
            Check("boot: buses Master → Music, Ambience, SFX → UI, World, Stingers", BusesOk(), BusList());
            await Pause(true);
            await MapFlow();
            await TimeFlow();
            await CommandFlow();
            await NoteFlow();
            await EventFlow();
            await EraFlow();
            await UiFlow();
            await MenuFlow();
            await SpamFlow();
        }
        catch (Exception e) { Fail("exception", e.ToString()); }
        await Seconds(1.5);   // let the last sounds end: a playback cut by the quit is reported as a leak
        GD.Print($"sfxprobe: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail);
    }

    // ------------------------------------------------------------------ flows

    async Task MapFlow()
    {
        var w = G.World;
        int p = First(q => w.PLand[q] == 1 && q != Cap && G.State.Fog[q] != 0);
        await Expect("map: select a province → «фишка»", () => G.Select(p), "piece");
        await Expect("map: the same province again → «фишка» again", () => G.Select(p), "piece");
        await Expect("map: deselect → panel close", () => G.Select(-1), "close");
        await Expect("map: deselect with nothing selected → silence", () => G.Select(-1));
        await Expect("map: zoom in → one tick", () => G.RequestZoom(+1), "zoom");
        Check("map: zoom in sounds higher", _played.Any(x => x.Key == "zoom" && x.Pitch > 1f), Pitches("zoom"));
        await Expect("map: zoom out → one tick", () => G.RequestZoom(-1), "zoom");
        Check("map: zoom out sounds lower", _played.Any(x => x.Key == "zoom" && x.Pitch < 1f), Pitches("zoom"));
        await Expect("map: a camera flight (panning) → silence", () => G.JumpCamera(new Vector2(w.PCX[p], w.PCY[p])), wait: .8);
        await Expect("map: terrain mode → tab", () => G.SetMode(MapMode.Terrain), "tab");
        await Expect("map: political mode → tab", () => G.SetMode(MapMode.Political), "tab");
        await Expect("map: observer mode → toggle off (its toast merged)", () => G.SetFogEnabled(false), "toggle_off");
        await Expect("map: fog back → toggle on", () => G.SetFogEnabled(true), "toggle_on");
    }

    async Task TimeFlow()
    {
        await Expect("time: unpause → latch", () => G.SetPaused(false), "unpause");
        int speed = G.State.Speed == 3 ? 4 : 3;
        await Expect($"time: speed {speed} → its switch", () => G.SetSpeed(speed), $"speed_{speed}");
        await Expect("time: the same speed again → silence", () => G.SetSpeed(speed));
        await Expect("time: pause → latch", () => G.SetPaused(true), "pause");
        await Expect("time: Space → unpause (not two sounds)", () => PressKey(Key.Space), "unpause");
        await Expect("time: Space → pause, its toast merged", () => PressKey(Key.Space), "pause");
    }

    async Task CommandFlow()
    {
        var w = G.World; var s = G.State;
        G.Issue(Cmd.CheatTech(Me, -1));   // building and geologists need the first era's knowledge
        await Settle();
        await Expect("gold: cheat gold → coins", () => G.Issue(Cmd.CheatGold(Me, 2000)), "coins");
        int p = First(G.CanClaim);
        if (p >= 0) await Expect("claim → the claim stinger, no note", () => G.Claim(p), "claim");
        else Fail("claim", "nothing claimable");
        int sea = First(q => w.PLand[q] == 0);
        await Expect("claim the sea → refusal", () => G.Claim(sea), "error");
        var options = G.BuildOptions(Cap);
        if (options.Count > 0) await Expect("build → hammer, no note", () => G.Build(Cap, options[0]), "build");
        else Fail("build", "no build options in the capital");
        int dig = First(q => s.Owner[q] == Me && Rules.CheckSurvey(s, q, Me) == SurveyError.None && G.MayHaveOre(q));
        if (dig < 0) dig = First(q => s.Owner[q] == Me && Rules.CheckSurvey(s, q, Me) == SurveyError.None);
        if (dig >= 0)
        {
            await Expect("geologists → pick, no note", () => G.Survey(dig), s.Ore[dig] >= 0 ? "ore" : "survey");
            Check("geologists: ore rings, nothing found digs", _played.Any(x => x.Key == (s.Ore[dig] >= 0 ? "ore" : "survey")), $"ore={s.Ore[dig]}");
        }
        else Fail("geologists", "nowhere to survey");
        await Expect("scouts out (auto) → footsteps, toast merged", () => G.SendScoutAuto(), "scouts_out");
        await Expect("scout targeting → question", () => G.BeginScoutTargeting(), "question");
        await Expect("targeting cancelled → toast", () => G.CancelScoutTargeting(), "toast");
    }

    async Task NoteFlow()
    {
        await Expect("note: scouts back", () => G.Notify("map-2", "Разведчики вернулись (проба)"), "scouts_back");
        await Expect("note: construction complete", () => G.Notify("hammer", "Проект завершён (проба)"), "built");
        await Expect("note: new nation met → stinger", () => G.Notify("affiliate", "Встречена новая держава (проба)"), "stinger_meet");
        await Expect("note: a neighbour's claim → soft note", () => G.Notify("flag", "Провинция у наших границ (проба)"), "note");
        await Expect("toast → glass", () => G.ShowToast("Проба звука"), "toast");
    }

    async Task EventFlow()
    {
        var s = G.State; var ev = s.Events;
        if (ev == null) { Fail("events", "no deck"); return; }
        var db = ev.Db;
        (bool choice, string key)[] wants =
        {
            (false, "page"), (false, "coins"), (false, "stinger_disaster"), (false, "stinger_eureka"), (false, "stinger_egg"),
            (false, "stinger_religion"), (false, "toast_important"),
        };
        foreach (var (_, key) in wants)
        {
            int e = -1;
            for (int i = 0; i < db.Events.Length && e < 0; i++)
            {
                var rt = db.Events[i];
                if (rt.IsChoice || Class(rt).Key != key) continue;
                _played.Clear();
                if (ev.Force(s, Me, i, G)) e = i;
            }
            if (e < 0) { GD.Print($"sfxprobe SKIP event → {key}: no chronicle event of this class can fire now"); continue; }
            await Settle();
            Match($"event «{db.Events[e].Def.Id}» → {key}", new[] { key });
        }
        // a choice window: the book opens (with its stinger, if any) and closes on the answer, the result note merged
        foreach (bool stinger in new[] { false, true })
        {
            int e = -1;
            for (int i = 0; i < db.Events.Length && e < 0; i++)
            {
                var rt = db.Events[i];
                if (!rt.IsChoice || Class(rt).Stinger != stinger || ev.Pending(Me) != null) continue;
                _played.Clear();
                if (ev.Force(s, Me, i, G)) e = i;
            }
            if (e < 0) { GD.Print($"sfxprobe SKIP choice (stinger={stinger}): none can fire now"); continue; }
            await Settle();
            var cls = Class(db.Events[e]);
            Match($"choice «{db.Events[e].Def.Id}» opens → book{(stinger ? " + " + cls.Key : "")}", stinger ? new[] { "book_open", cls.Key } : new[] { "book_open" });
            await Expect($"choice «{db.Events[e].Def.Id}» answered → book closes, result note merged", () => G.ChooseOption(G.PendingChoice().Options[0].Index), "book_close");
        }
    }

    async Task EraFlow()
    {
        int era = G.EraIndex + 1;
        string key = $"stinger_era_{SoundBank.EraGroup(era)}";
        double t0 = Time.GetTicksMsec() / 1000.0;
        await Expect($"era {era} → {key}, the era note silent", () => G.Issue(Cmd.CheatEra(Me, era)), key);
        var fanfare = _played.FirstOrDefault(x => x.Key == key);
        Check("era: a breath of silence (≥ 0.35 s) before the fanfare", fanfare.Key != null && fanfare.Time - t0 >= .35, Inv($"{fanfare.Time - t0:0.00} s"));
        // the sounds with a material follow the era group: the pause latch of Глина now
        await Seconds(.3);
        bool was = G.State.Paused;
        // checked on pausing: a running world may chime in with its own news right after an unpause (rumours, firsts)
        if (was) { G.SetPaused(false); await Settle(); }
        await Expect("era skin: pause sounds of its era group", () => G.SetPaused(true), "pause");
        var latch = _played.LastOrDefault(x => x.Key is "pause" or "unpause");
        Check("era skin: the file is the group's own", latch.File != null && latch.File.Contains($"skin/g{SoundBank.EraGroup(era)}_"), latch.File ?? "none");
        G.SetPaused(was);
    }

    async Task UiFlow()
    {
        var mode = (BaseButton)Hud.DebugTarget("mode");   // the political-map button
        var trophy = (BaseButton)Hud.DebugTarget("trophy");
        G.SetMode(MapMode.Terrain);
        await Seconds(.3);
        await Expect("hud: a still cursor under a new button → silence", () => mode.EmitSignal(Control.SignalName.MouseEntered), .5);
        await Expect("hud: the mouse moves onto a button → hover", () => Move(mode.GetGlobalRect().GetCenter()), "hover");
        await Expect("hud: press a mode button → tab (the click merged)", () => mode.EmitSignal(BaseButton.SignalName.Pressed), "tab");
        Check("hud: the merged click is reported", _dropped.Any(d => d.key == "click" && d.reason == "merged:tab"), string.Join(", ", _dropped.Select(d => d.key + ":" + d.reason)));
        await Expect("hud: press the trophy → click (leaderboard)", () => trophy.EmitSignal(BaseButton.SignalName.Pressed), "click");
        await Expect("hud: press it again → click", () => trophy.EmitSignal(BaseButton.SignalName.Pressed), "click");
    }

    async Task MenuFlow()
    {
        G.Select(-1);
        await Seconds(.3);
        await Expect("pause menu: Esc opens it → open, no latch", () => PressKey(Key.Escape), "open");
        Check("pause menu: open", PauseMenu.IsOpen);
        await Expect("pause menu: Esc closes it → close, no latch", () => PressKey(Key.Escape), "close");
        Check("pause menu: closed", !PauseMenu.IsOpen);
    }

    /// <summary>Speed 5 with scouts out, a wandering camera and a zoom step every 0.5 s: no sound comes faster than its interval.</summary>
    async Task SpamFlow()
    {
        var w = G.World;
        await Settle();
        G.SetPaused(false);
        G.SetSpeed(5);
        await Seconds(.4);
        G.SendScoutAuto();
        G.SendScoutAuto();
        await Seconds(.4);
        _played.Clear(); _dropped.Clear();
        double t0 = Now, next = 0;
        int zooms = 0, dir = 1, frame = 0;
        var rng = new RandomNumberGenerator { Seed = 7 };
        int start = Cap;
        while (Now - t0 < 20)
        {
            await Frames(1);
            frame++;
            if (frame % 2 == 0)   // pan: a camera jump a little further every other frame
                G.JumpCamera(new Vector2(w.PCX[start] + (float)Math.Sin(frame * .02) * 300, w.PCY[start] + (float)Math.Cos(frame * .015) * 200));
            if (Now - t0 >= next)
            {
                next += .5;
                int before = G.ZoomLevel;
                G.RequestZoom(dir);
                await Frames(1);
                if (G.ZoomLevel != before) zooms++;
                else { dir = -dir; }
                if (rng.Randf() < .3f) dir = -dir;
            }
        }
        G.SetPaused(true);
        await Settle();
        var list = _played.Where(p => p.Time >= t0).ToList();
        GD.Print($"sfxprobe speed 5, 20 s: {list.Count} sounds · {Tally(list)} · dropped {_dropped.Count}: {Tally(_dropped.Select(d => d.key + ":" + d.reason.Split(':')[0]))}");
        int zoomSounds = list.Count(p => p.Key == "zoom"), zoomMerged = _dropped.Count(d => d.key == "zoom" && d.reason.StartsWith("merged:"));
        // a step that lands in the same frame as a weightier sound (construction done…) merges into it
        Check("spam: one tick per zoom step, none for panning", zoomSounds + zoomMerged == zooms && zoomSounds >= zooms - 3,
            $"{zoomSounds} ticks + {zoomMerged} merged for {zooms} steps");
        var others = list.Where(p => p.Key != "zoom" && p.Key != "pause").ToList();
        int worst = 0;
        foreach (var p in others) worst = Math.Max(worst, others.Count(q => q.Time >= p.Time && q.Time < p.Time + 1));
        Check("spam: at most 4 game sounds in any second", worst <= 4, $"worst second {worst}, {others.Count} in 20 s");
        Check("spam: at most 1.5 game sounds a second on average", others.Count <= 30, Inv($"{others.Count / 20.0:0.00}/s"));
        string fast = null;
        foreach (var g in list.GroupBy(p => p.Key))
        {
            int min = SoundBank.Defs.TryGetValue(g.Key, out var d) ? d.MinMs : 0;
            var ts = g.Select(p => p.Time).OrderBy(t => t).ToList();
            for (int i = 1; i < ts.Count; i++)
                if ((ts[i] - ts[i - 1]) * 1000 < min - 1) fast ??= Inv($"{g.Key} {1000 * (ts[i] - ts[i - 1]):0} ms < {min} ms");
        }
        Check("spam: no sound faster than its interval", fast == null, fast);
        Check("spam: no piece / time sounds nobody asked for", !list.Any(p => p.Key is "piece" or "unpause" || p.Key.StartsWith("speed_")), Tally(list));
    }

    // ------------------------------------------------------------------ helpers

    static EventSounds.Sound Class(EventRt rt) =>
        EventSounds.Classify(rt.Def.Id, rt.Def.Icon, rt.Tone == Tone.Joke, rt.Kind == EventKind.World, rt.Def.Importance);

    /// <summary>Clear the record, act, wait for the sounds (stingers included) and compare with the expected keys (each exactly once, nothing else).</summary>
    async Task Expect(string name, Action act, params string[] keys) => await Expect(name, act, .35, keys);

    async Task Expect(string name, Action act, double wait, params string[] keys)
    {
        await Settle();
        _played.Clear(); _dropped.Clear();
        act();
        await Seconds(wait);
        await Settle();
        Match(name, keys);
    }

    void Match(string name, string[] keys)
    {
        var want = keys.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        // in a window the real cursor may rest on a HUD button that a rebuild slides under it: its hover is not ours
        var got = _played.Where(p => p.Key != "hover" || want.ContainsKey("hover")).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Count());
        bool ok = want.Count == got.Count && want.All(kv => got.TryGetValue(kv.Key, out int n) && n == kv.Value);
        Check(name, ok, $"want [{string.Join(", ", keys)}], got [{Tally(_played)}]");
    }

    async Task Settle()
    {
        double t0 = Now;
        while (Now - t0 < 6 && !(Stingers.I?.Idle ?? true)) await Frames(1);
        await Frames(2);
    }

    async Task Pause(bool on)
    {
        if (G.State.Paused != on) G.SetPaused(on);
        await Seconds(.3);
    }

    static string Tally(IEnumerable<Sfx.Played> list) => Tally(list.Select(p => p.Key));
    static string Tally(IEnumerable<string> keys) => string.Join(", ", keys.GroupBy(k => k).OrderByDescending(g => g.Count()).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key));
    string Pitches(string key) => string.Join(", ", _played.Where(p => p.Key == key).Select(p => p.Pitch.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)));

    static bool BusesOk()
    {
        string Send(string b) => AudioServer.GetBusIndex(b) < 0 ? null : AudioServer.GetBusSend(AudioServer.GetBusIndex(b));
        return Send(AudioBuses.Music) == "Master" && Send(AudioBuses.Ambience) == "Master" && Send(AudioBuses.Sfx) == "Master"
            && Send(AudioBuses.Ui) == AudioBuses.Sfx && Send(AudioBuses.World) == AudioBuses.Sfx && Send(AudioBuses.Stingers) == AudioBuses.Sfx;
    }

    static string BusList() => string.Join(", ", Enumerable.Range(0, AudioServer.BusCount).Select(i => $"{AudioServer.GetBusName(i)}→{AudioServer.GetBusSend(i)}"));

    static void Move(Vector2 at) => Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = new Vector2(4, 0) });

    static void PressKey(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    int First(Func<int, bool> pred) { for (int p = 0; p < G.World.P; p++) if (pred(p)) return p; return -1; }
    static double Now => Time.GetTicksMsec() / 1000.0;
    static string Inv(FormattableString f) => FormattableString.Invariant(f);
    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s, true, false, true), SceneTreeTimer.SignalName.Timeout);

    void Check(string name, bool ok, string detail = null) { if (ok) Pass(name, detail); else Fail(name, detail); }
    void Pass(string name, string detail = null) { _pass++; GD.Print($"sfxprobe PASS {name}{(detail != null ? "  · " + detail : "")}"); }
    void Fail(string name, string detail = null) { _fail++; GD.Print($"sfxprobe FAIL {name}{(detail != null ? "  · " + detail : "")}"); }
}
