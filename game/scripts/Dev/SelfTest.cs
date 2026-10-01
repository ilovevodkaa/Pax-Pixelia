using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Map;
using PaxPixelia.Sim;
using PaxPixelia.UI;

namespace PaxPixelia.Dev;

/// <summary>
/// --selftest: drives the real game (world, map, UI, sim) through the player flows and prints one PASS/FAIL line per
/// check, then quits with the number of failures as the exit code.
///   --selftest-shots=DIR   also save a screenshot at each checkpoint (DIR/NN_name.png)
/// Flows: boot → map labels (nothing overlaps at ×2–×6) → map modes → zoom ×½/×1/×3/×6/×8 + wrap seam + a jump during
/// a zoom glide → hover tooltip → panel variants → claim → build → survey → scouts (targeting, manual + auto, fog reveal,
/// stale provinces keep their last seen owner) → pause / speed keys → observer → leaderboard → «Новый мир» (mid-action,
/// double click, then three more worlds checked for leaks) → 1280×720 window.
/// </summary>
public partial class SelfTest : Node
{
    readonly Main _main;
    readonly string _shots = Cli.Str("selftest-shots");
    int _pass, _fail, _shotNo;

    // event recorders
    readonly List<(string icon, string text)> _notes = new();
    readonly List<(string text, ToastKind kind)> _toasts = new();
    readonly List<IReadOnlyList<int>> _provChanges = new();
    int _fogEvents, _worldReady, _dateEvents;

    Game G => Game.I;
    Hud Hud => _main.Hud;

    public SelfTest(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "SelfTest";
        var g = G;
        g.Notified += (i, t) => _notes.Add((i, t));
        g.Toast += (t, _, k) => _toasts.Add((t, k));
        g.ProvincesChanged += ps => _provChanges.Add(ps);
        g.FogChanged += _ => _fogEvents++;
        g.WorldReady += () => _worldReady++;
        g.DateChanged += () => _dateEvents++;
        if (_shots != null) DirAccess.MakeDirRecursiveAbsolute(_shots);
        Run();
    }

    async void Run()
    {
        try
        {
            while (!G.IsReady) await Frames(1);
            await Seconds(.8);                         // loading fade + deferred capital selection
            if (G.IsNomad)
            {
                await NomadFlow();                     // --selftest --nomad: the tribe start, then done
                GD.Print($"selftest: {_pass} passed, {_fail} failed");
                GetTree().Quit(_fail);
                return;
            }
            await Boot();
            await Labels();
            await Modes();
            await Zoom();
            await HoverTip();
            await Panels();
            await Claim();
            await BuildAndSurvey();
            await ScoutFlow();
            await TimeControl();
            await Observer();
            await LeaderboardFlow();
            await PolicyFlow();
            await WondersFlow();
            await EventChoiceFlow();
            await PauseMenuFlow();
            await SkinFlow();
            await Regenerate();
            await SmallWindow();
        }
        catch (Exception e)
        {
            Fail("exception", e.ToString());
        }
        GD.Print($"selftest: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail);
    }

    // ------------------------------------------------------------------ flows

    async Task Boot()
    {
        var s = G.State; int cap = Cap;
        Check("boot: world ready", G.IsReady && G.World.P > 1000, $"P={G.World.P}, generation {G.LastGenerationMs} ms");
        Check("boot: loading screen gone", !Hud.Loading.Visible);
        Check("boot: capital selected, panel open", G.Selected == cap && Hud.Panel.Visible && Hud.Panel.Province == cap, $"selected={G.Selected} capital={cap}");
        Check("boot: panel title is the capital", Hud.Panel.TitleText == G.World.PName[cap], Hud.Panel.TitleText);
        Check("boot: map has the world", MapView.Current is { HasWorld: true });
        Check("boot: camera published", G.CameraRect.Size.X > 0 && G.ZoomLevel == 3, $"rect={G.CameraRect} zoom={G.ZoomLevel}");
        Check("boot: capital visible, some land explored", s.Fog[cap] == 2 && Explored() > 30, $"explored={Explored()}");
        Check("boot: the player's queue is not already built", !(Simulation.Projects[s.ProjectIndex].Building is Data.Bld b && s.Buildings[cap].Contains(b)), s.QueueName);
        Check("boot: calendar starts in 4000 до н. э., month format", G.DateText.EndsWith("4000 до н. э.") && !char.IsDigit(G.DateText[0]), G.DateText);
        Check("boot: era and science", G.EraName == Eras.Name(G.EraIndex) && G.ScienceRate > 0, $"{G.EraName}, +{G.ScienceRate} science");
        Check("boot: the roster comes from the setup", G.Nations.Length == G.State.Nat.Length && G.Setup != null, $"{G.Nations.Length} nations");
        await Shot("start");
    }

    /// <summary>The label plan's own audit at every level with city or province names, plus the player's name at ×3.</summary>
    async Task Labels()
    {
        var plan = MapView.Current.Labels;
        foreach (int level in new[] { 2, 3, 4, 5, 6 })
        {
            int bad = plan.CountOverlaps(level);
            Check($"labels ×{level}: nothing overlaps", bad == 0, $"{bad} overlaps");
        }
        Check("labels ×3: the player's nation is named", plan.Get(3).Nations[GameState.LocalPlayer].Show);
        await Frames(1);
    }

    async Task Modes()
    {
        foreach (var (m, tag) in new[] { (MapMode.Terrain, "ter"), (MapMode.Religion, "rel"), (MapMode.Trade, "trd"), (MapMode.Fertility, "fer"), (MapMode.Political, "pol") })
        {
            G.SetMode(m);
            await Frames(3);
            Check($"mode {tag}", G.Mode == m);
            await Shot("mode_" + tag);
        }
    }

    async Task Zoom()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        await ZoomTo(0);
        Check("zoom ×½ atlas: the whole world height fits", G.ZoomLevel == 0 && G.CameraRect.Size.Y >= G.World.H, $"level={G.ZoomLevel} rect={G.CameraRect.Size}");
        await Shot("zoom_atlas");
        await ZoomTo(1);
        Check("zoom ×1", G.ZoomLevel == 1 && (G.CameraRect.Size - screen).Length() < 1.5f, $"level={G.ZoomLevel} rect={G.CameraRect.Size}");
        await Shot("zoom1");
        await ZoomTo(6);
        Check("zoom ×6", G.ZoomLevel == 6 && (G.CameraRect.Size * 6 - screen).Length() < 1.5f, $"level={G.ZoomLevel}");
        await Shot("zoom6");
        await ZoomTo(8);
        Check("zoom ×8 (max)", G.ZoomLevel == 8);
        G.RequestZoom(+1); await Seconds(.3);
        Check("zoom stays at ×8", G.ZoomLevel == 8);
        await ZoomTo(3);
        // wrap seam: centre the view on x = 0 (fog off, so the terrain on both sides shows); the rect must straddle it
        int cap = Cap;
        G.SetFogEnabled(false);
        G.JumpCamera(new Vector2(0, G.World.PCY[cap]));
        await Seconds(.6);
        var r = G.CameraRect;
        float cx = Mathf.PosMod(r.Position.X + r.Size.X / 2 + G.World.W / 2f, G.World.W) - G.World.W / 2f;
        Check("wrap: view centred on the seam", Mathf.Abs(cx) < 2, $"rect={r}");
        await Shot("wrap_seam");
        G.SetFogEnabled(true);
        G.JumpCamera(new Vector2(G.World.PCX[cap], G.World.PCY[cap]));
        await Seconds(.6);
        // a jump (minimap click) right after a wheel notch must still land on a whole zoom level
        G.RequestZoom(+1);
        await Frames(2);
        G.JumpCamera(new Vector2(G.World.PCX[cap] + 200, G.World.PCY[cap]));
        await Seconds(.6);
        var v = MapView.Current.View;
        Check("zoom glide + jump: lands on a level", v.AtRest && v.Level == G.ZoomLevel, $"view zoom {v.Zoom}, level ×{G.ZoomLevel}");
        await ZoomTo(3);
        G.JumpCamera(new Vector2(G.World.PCX[cap], G.World.PCY[cap]));
        await Seconds(.6);
    }

    async Task ZoomTo(int level)
    {
        for (int i = 0; i < 10 && G.ZoomLevel != level; i++)
        {
            G.RequestZoom(G.ZoomLevel < level ? 1 : -1);
            await Seconds(.22);
        }
        await Seconds(.25);
    }

    async Task HoverTip()
    {
        int p = FirstVisible(q => G.State.Owner[q] > 0) ;
        if (p < 0) p = FirstVisible(q => G.World.PLand[q] == 1 && q != Cap);
        Hud.FakeMouse = new Vector2(700, 450);
        G.Hover(p);
        await Frames(3);
        Check("hover: province tooltip shown", Hud.Tip.Visible, $"p={p} {G.World.PName[p]}");
        await Shot("hover_tip");
        G.Hover(-1);
        await Frames(2);
        Check("hover: tooltip hides", !Hud.Tip.Visible);
        Hud.FakeMouse = null;
    }

    async Task Panels()
    {
        var w = G.World; var s = G.State;
        int foreign = FirstKnown(q => s.Owner[q] > 0);
        int unowned = FirstKnown(q => w.PLand[q] == 1 && s.Owner[q] < 0);
        int sea = FirstKnown(q => w.PLand[q] == 0);
        int fog = First(q => w.PLand[q] == 1 && s.Fog[q] == 0 && w.SameBody(q, Cap));
        await ShowPanel("foreign", foreign, p => w.PName[p]);
        await ShowPanel("unowned", unowned, p => w.PName[p]);
        await ShowPanel("sea", sea, p => w.PName[p]);
        await ShowPanel("fog", fog, _ => "Неизведанные земли");
        G.Select(-1); await Frames(2);
        Check("panel: deselect closes it", !Hud.Panel.Visible);
    }

    async Task ShowPanel(string tag, int p, Func<int, string> titleOf)
    {
        if (p < 0) { Pass($"panel {tag} (skipped: none known in this world)"); return; }
        string title = titleOf(p);
        G.Select(p);
        G.JumpCamera(new Vector2(G.World.PCX[p], G.World.PCY[p]));
        await Seconds(.45);
        Check($"panel {tag}", Hud.Panel.Visible && Hud.Panel.Province == p && Hud.Panel.TitleText == title, $"p={p} title={Hud.Panel.TitleText}");
        await Shot("panel_" + tag);
    }

    async Task Claim()
    {
        var w = G.World; var s = G.State;
        int p = First(G.CanClaim);
        if (p < 0) { Fail("claim", "nothing claimable"); return; }
        int notes = _notes.Count, changes = _provChanges.Count;
        G.Select(p);
        G.JumpCamera(new Vector2(w.PCX[p], w.PCY[p]));
        await Seconds(.4);
        double gold = s.Gold;
        int claimPrice = G.ClaimPrice;
        G.Claim(p);
        double charged = gold - s.Gold;   // read now: a year may tick during the frames below
        await Frames(3);
        Check("claim: province joins", s.Owner[p] == GameState.LocalPlayer, w.PName[p]);
        Check("claim: gold charged", Math.Abs(charged - claimPrice) < .01, $"−{charged:F0} of {claimPrice}");
        Check("claim: chronicle entry", _notes.Skip(notes).Any(n => n.icon == "flag"));
        Check("claim: ProvincesChanged raised", _provChanges.Skip(changes).Any(ps => ps != null && ps.Contains(p)));
        Check("claim: panel shows own province", Hud.Panel.Visible && Hud.Panel.Province == p && Hud.Panel.TitleText == w.PName[p]);
        // capture fill (GDD): the colour spreads over ~0.9 s real time; names, city colours and the minimap switch at its end
        bool snap = Settings.I?.ReducedMotion == true, filling = G.CaptureFillsRunning, held = G.ShownOwner(p) != GameState.LocalPlayer;
        await Shot("claimed");
        await Seconds(ProvinceTransitions.Duration + .25);
        Check("claim: capture fill runs, then shows the new owner", (snap || (filling && held)) && G.ShownOwner(p) == GameState.LocalPlayer,
            $"running {filling}, old owner held {held}{(snap ? ", «меньше анимации»: snaps" : "")}");

        int far = First(q => w.PLand[q] == 1 && s.Owner[q] < 0 && s.Explored[q] && !Rules.Borders(w, s, q, GameState.LocalPlayer));
        int toasts = _toasts.Count;
        if (far >= 0) G.Claim(far);
        Check("claim refused far away (red toast)", far >= 0 && s.Owner[far] < 0 && _toasts.Skip(toasts).Any(t => t.kind == ToastKind.Error));
    }

    async Task BuildAndSurvey()
    {
        var w = G.World; var s = G.State;
        // the tribe knows nothing yet: buildings are locked until the first technology
        int lockedAt = First(q => s.Owner[q] == GameState.LocalPlayer && G.LockedBuildOptions(q).Count > 0);
        Check("techs: buildings locked before the first technology", lockedAt >= 0 && G.BuildOptions(lockedAt).Count == 0 && G.ResearchIdle,
            lockedAt >= 0 ? G.BuildProblem(lockedAt, G.LockedBuildOptions(lockedAt)[0]) : "no plot");
        Hud.DebugToggleTech();
        await Frames(3);
        Check("techs: the card opens from the atom", Hud.Tech.Visible);
        await Shot("techs");
        int firstStep = Techs.Index("gathering");
        G.Research(firstStep);
        await Frames(2);
        Check("techs: a study chosen (a journaled command)", G.Researching == firstStep && G.Journal.Any(c => c.Type == CmdType.Research));
        Hud.DebugToggleTech();
        G.Issue(Cmd.CheatTech(G.Viewer, -1));   // the rest of the test builds and surveys
        await Frames(2);
        Check("techs: learned → the build menu opens", lockedAt < 0 || G.BuildOptions(lockedAt).Count > 0);

        int p = First(q => s.Owner[q] == GameState.LocalPlayer && G.BuildOptions(q).Count > 0);
        if (p < 0) { Fail("build", "no free plot"); return; }
        var b = G.BuildOptions(p)[0];
        int notes = _notes.Count;
        G.Select(p);
        await Frames(2);
        double gold = s.Gold;
        int buildPrice = G.BuildCost(b);
        G.Build(p, b);
        double charged = gold - s.Gold;
        await Frames(3);
        Check("build: building added", s.Buildings[p].Contains(b), $"{Data.BldName[(int)b]} in {w.PName[p]}");
        Check("build: gold charged", Math.Abs(charged - buildPrice) < .01, $"−{charged:F0}");
        Check("build: chronicle entry", _notes.Skip(notes).Any(n => n.icon == "hammer"));
        await Shot("built");

        int toasts = _toasts.Count;
        int foreign = First(q => s.Owner[q] > 0);
        G.Build(foreign, Data.Bld.Shrine);
        Check("build refused abroad (red toast)", !s.Buildings[foreign].Contains(Data.Bld.Shrine) || _toasts.Skip(toasts).Any(t => t.kind == ToastKind.Error));

        int ore = First(q => s.Owner[q] == GameState.LocalPlayer && !s.OreFound[q] && G.MayHaveOre(q));
        if (ore < 0) ore = ClaimTowardsHills();
        if (ore < 0) { Pass("survey (skipped: no hills within reach)"); return; }
        notes = _notes.Count;
        G.Select(ore);
        await Frames(2);
        G.Survey(ore);
        await Frames(3);
        Check("survey: done", s.OreFound[ore] && _notes.Skip(notes).Any(n => n.icon == "shovel"), _notes.LastOrDefault().text);
        await Shot("surveyed");
    }

    /// <summary>No hills at home: claim a chain of explored tribal land up to the nearest hill province.</summary>
    int ClaimTowardsHills()
    {
        var w = G.World; var s = G.State;
        var prev = new int[w.P]; Array.Fill(prev, -2);
        var q = new Queue<int>();
        for (int p = 0; p < w.P; p++) if (s.Owner[p] == GameState.LocalPlayer) { prev[p] = -1; q.Enqueue(p); }
        while (q.Count > 0)
        {
            int p = q.Dequeue();
            if (s.Owner[p] < 0 && G.MayHaveOre(p))
            {
                var chain = new List<int>();
                for (int c = p; c >= 0 && s.Owner[c] < 0; c = prev[c]) chain.Add(c);
                chain.Reverse();
                if (chain.Count * G.ClaimPrice > s.Gold) return -1;   // too far to afford
                foreach (int c in chain) { if (!G.CanClaim(c)) return -1; G.Claim(c); }
                return p;
            }
            foreach (int n in w.Adj[p])
                if (prev[n] == -2 && w.PLand[n] == 1 && s.Owner[n] < 0 && s.Explored[n]) { prev[n] = p; q.Enqueue(n); }
        }
        return -1;
    }

    async Task ScoutFlow()
    {
        var w = G.World; var s = G.State; int cap = Cap;
        G.Select(cap);
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap]));
        await ZoomTo(2);
        int toasts = _toasts.Count;
        G.BeginScoutTargeting();
        await Frames(2);
        Check("scouts: targeting on, pick toast", G.IsTargeting && _toasts.Skip(toasts).Any(t => t.kind == ToastKind.Pick));
        await Shot("targeting");
        int sea = First(q => w.PLand[q] == 0);
        toasts = _toasts.Count;
        G.SendScout(sea);
        Check("scouts: sea refused, still targeting", G.IsTargeting && s.Scouts.Count == 0 && _toasts.Skip(toasts).Any(t => t.kind == ToastKind.Error));
        PressKey(Key.Escape);
        await Frames(3);
        Check("scouts: Esc cancels targeting", !G.IsTargeting);

        // a far unexplored target on the home continent
        int target = -1; float best = 0;
        for (int q = 0; q < w.P; q++)
            if (w.PLand[q] == 1 && s.Fog[q] == 0 && w.SameBody(q, cap))
            {
                float d = Simulation.Distance(w, q, cap);
                if (d < 420 && d > best) { best = d; target = q; }
            }
        if (target < 0) { Fail("scouts: target", "no unexplored land on the continent"); return; }
        G.BeginScoutTargeting();
        bool sent = G.SendScout(target);
        bool auto = G.SendScoutAuto();
        toasts = _toasts.Count;
        bool third = G.SendScoutAuto();
        await Frames(2);
        Check("scouts: manual + auto sent", sent && auto && s.Scouts.Count == 2 && !G.IsTargeting);
        Check("scouts: third party refused", !third && _toasts.Skip(toasts).Any(t => t.kind == ToastKind.Error));

        int explored0 = Explored(), fog0 = _fogEvents, notes = _notes.Count;
        G.SetSpeed(5);
        await Seconds(1.2);
        await Shot("scouts_walking");
        var t0 = Time.GetTicksMsec();
        while (s.Scouts.Count > 0 && Time.GetTicksMsec() - t0 < 45000) await Frames(10);
        G.SetSpeed(2);
        Check("scouts: both parties returned", s.Scouts.Count == 0, $"{(Time.GetTicksMsec() - t0) / 1000.0:F1} s at speed 5");
        Check("scouts: fog revealed", Explored() > explored0 && _fogEvents > fog0, $"explored {explored0} → {Explored()}, {_fogEvents - fog0} fog events");
        Check("scouts: target now on the map", s.Fog[target] != 0, w.PName[target]);
        Check("scouts: two «вернулись» notes", _notes.Skip(notes).Count(n => n.icon == "map-2") == 2);
        await ZoomTo(1);
        await Shot("scouts_done");
        StaleMemory();
        await ZoomTo(3);
    }

    /// <summary>A stale province shows the owner last seen, whatever happened there since.</summary>
    void StaleMemory()
    {
        var w = G.World; var s = G.State;
        int p = First(q => w.PLand[q] == 1 && s.Fog[q] == 1 && s.CapitalOf[q] < 0);
        if (p < 0) { Pass("stale memory (skipped: no stale land)"); return; }
        short real = s.Owner[p];
        int shown = s.VisibleOwner(p);
        s.Owner[p] = (short)(shown == 1 ? 2 : 1);   // a bot takes it out of sight
        Check("stale province keeps its last seen owner", s.VisibleOwner(p) == shown, $"{w.PName[p]}: shown {shown}, now owned by {s.Owner[p]}");
        s.Owner[p] = real;
    }

    async Task TimeControl()
    {
        var s = G.State;
        G.SetPaused(true);
        long tick = s.Tick; string date = G.DateText;
        await Seconds(1.1);
        Check("pause: the clock and the date stand still", s.Tick == tick && G.DateText == date && s.Paused, date);
        await Shot("paused");
        PressKey(Key.Space);
        await Frames(2);
        Check("Space resumes", !s.Paused);
        PressKey(Key.Key5);
        await Frames(2);
        Check("key 5 → speed 5", s.Speed == 5);
        tick = s.Tick; var d0 = s.Date; int dates = _dateEvents;
        await Seconds(1.05);
        Check("speed 5: 40 ticks a second", s.Tick - tick >= 34 && s.Tick - tick <= 44, $"{s.Tick - tick} ticks");
        Check("speed 5: months pass and DateChanged fires", s.Date.MonthIndex > d0.MonthIndex && _dateEvents > dates, $"{Calendar.Text(d0, true)} → {G.DateText}, {_dateEvents - dates} events");
        PressKey(Key.Key2);
        await Frames(2);
        Check("key 2 → speed 2", s.Speed == 2);
    }

    async Task Observer()
    {
        int metBefore = G.Leaderboard().Count;
        G.SetFogEnabled(false);
        await Frames(3);
        Check("observer: every nation listed", G.Leaderboard().Count == G.Nations.Length && G.UnmetNations == 0);
        await ZoomTo(1);
        await Shot("observer");
        G.SetFogEnabled(true);
        await Frames(3);
        Check("observer off: back to met nations", G.Leaderboard().Count == metBefore, $"{metBefore} met");
        await ZoomTo(3);
    }

    async Task LeaderboardFlow()
    {
        Hud.DebugToggleLead();
        await Seconds(.3);
        Check("leaderboard opens", Hud.Lead.Visible);
        await Shot("leaderboard");
        PressKey(Key.Escape);
        await Frames(3);
        Check("Esc closes the leaderboard", !Hud.Lead.Visible);
    }

    /// <summary>The deck of fates: a forced choice opens the event window, its first button answers through a command.</summary>
    async Task EventChoiceFlow()
    {
        var s = G.State;
        var ev = s.Events;
        Check("events: the content deck is attached", ev != null, ev == null ? "no deck" : $"{ev.Db.Events.Length} events, {s.Nat[GameState.LocalPlayer].EventCount} dealt so far");
        if (ev == null) return;
        for (int e = 0; e < ev.Db.Events.Length && ev.Pending(GameState.LocalPlayer) == null; e++)
            if (ev.Db.Events[e].IsChoice) ev.Force(s, GameState.LocalPlayer, e, G);
        await Frames(2);
        var info = G.PendingChoice();
        Check("events: a choice opens the event window", info != null && Hud.Events.Visible && info.Options.Length > 0, info?.Title);
        if (info == null) return;
        await Shot("event");
        int notes = _notes.Count, journal = G.Journal.Count;
        Button first = null;
        foreach (var b in Hud.Events.FindChildren("*", "Button", true, false)) { first = (Button)b; break; }
        first?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(3);
        Check("events: the first button answers and closes the window", G.PendingChoice() == null && !Hud.Events.Visible && _notes.Count > notes,
            _notes.Count > notes ? _notes[^1].text : "no chronicle entry");
        Check("events: the answer is a journaled command", G.Journal.Count == journal + 1 && G.Journal[^1].Type == CmdType.Choose);
    }

    /// <summary>Esc with nothing else open ends in the pause menu; Esc again closes it and gives the time back.</summary>
    async Task PauseMenuFlow()
    {
        G.Select(-1);
        await Frames(2);
        bool paused = G.State.Paused;
        PressKey(Key.Escape);
        await Frames(3);
        Check("pause menu: Esc with nothing open opens it and pauses", PauseMenu.IsOpen && G.State.Paused);
        await Shot("pausemenu");
        PressKey(Key.Escape);
        await Frames(3);
        Check("pause menu: Esc closes it and restores the clock", !PauseMenu.IsOpen && G.State.Paused == paused);
        G.Select(G.State.NationCapital[GameState.LocalPlayer]);
        await Frames(2);
    }

    async Task Regenerate()
    {
        var old = G.World; int ready = _worldReady;
        // leave the old world busy: scouts out, targeting on, foreign selection, leaderboard open, paused, religion map
        G.SendScoutAuto();
        G.Select(First(q => G.State.Owner[q] > 0));
        Hud.DebugToggleLead();
        G.SetMode(MapMode.Religion);
        G.SetPaused(true);
        G.BeginScoutTargeting();
        await Frames(3);

        var regen = (BaseButton)Hud.Mini.DebugTarget("regen");
        regen.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check("regen: loading screen shown", Hud.Loading.Visible && !G.IsReady);
        await Shot("regen_loading");
        regen.EmitSignal(BaseButton.SignalName.Pressed);   // an impatient second click supersedes the first
        var t0 = Time.GetTicksMsec();
        while (_worldReady == ready && Time.GetTicksMsec() - t0 < 15000) await Frames(2);
        await Seconds(.8);
        Check("regen: exactly one new world", _worldReady == ready + 1 && G.IsReady && G.World != old && G.World.Seed == G.Seed, $"ready events {_worldReady - ready}, seed {G.Seed}");
        var s = G.State; int cap = Cap;
        Check("regen: clean state", s.Scouts.Count == 0 && !G.IsTargeting && !s.Paused && s.Year <= -3990 && G.Journal.Count == 0, $"{G.DateText}, scouts {s.Scouts.Count}, journal {G.Journal.Count}");
        Check("regen: capital selected again", G.Selected == cap && Hud.Panel.Visible && Hud.Panel.TitleText == G.World.PName[cap]);
        Check("regen: overlays closed", !Hud.Lead.Visible && !Hud.Loading.Visible);
        Check("regen: map rebuilt", MapView.Current is { HasWorld: true } && G.CameraRect.Size.X > 0);
        Check("regen: fog of the new world", s.Fog[cap] == 2 && Explored() < G.World.P / 2);
        G.SetMode(MapMode.Political);
        await Frames(3);
        await Shot("regen_done");

        // three more worlds in a row: nodes, objects and memory must not pile up
        var (objects0, nodes0, managed0) = Footprint();
        for (int k = 0; k < 3; k++)
        {
            ready = _worldReady;
            G.RegenerateWorld(4242 + k);
            t0 = Time.GetTicksMsec();
            while (_worldReady == ready && Time.GetTicksMsec() - t0 < 15000) await Frames(2);
            await Seconds(.6);
        }
        var (objects1, nodes1, managed1) = Footprint();
        Check("regen ×3: nothing piles up", objects1 - objects0 < 200 && nodes1 - nodes0 < 20 && managed1 - managed0 < 40,
            $"objects {objects0} → {objects1}, nodes {nodes0} → {nodes1}, managed {managed0:F0} → {managed1:F0} MB");
    }

    static (int objects, int nodes, double managedMb) Footprint()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return ((int)Performance.GetMonitor(Performance.Monitor.ObjectCount), (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            GC.GetTotalMemory(true) / (1024.0 * 1024.0));
    }

    async Task SmallWindow()
    {
        var win = GetWindow();
        var old = win.Size;
        win.Size = new Vector2I(1280, 720);
        await Seconds(.5);
        var screen = GetViewport().GetVisibleRect().Size;
        Check("1280×720: viewport follows the window", screen == new Vector2(1280, 720), $"{screen}");
        Check("1280×720: camera covers the screen", (G.CameraRect.Size * G.ZoomLevel - screen).Length() < 1.5f, $"{G.CameraRect.Size}");
        Check("1280×720: panel fits", Hud.Panel.Visible && Hud.Panel.GetGlobalRect().End.Y <= 720 && Hud.Panel.GetGlobalRect().End.X <= 1280);
        await Shot("small_window");
        win.Size = old;
        await Frames(4);
    }

    // ------------------------------------------------------------------ helpers

    int Cap => G.State.NationCapital[GameState.LocalPlayer];
    int Explored() { int n = 0; foreach (var e in G.State.Explored) if (e) n++; return n; }

    int First(Func<int, bool> pred) { for (int p = 0; p < G.World.P; p++) if (pred(p)) return p; return -1; }
    int FirstKnown(Func<int, bool> pred) => First(p => G.State.Fog[p] != 0 && pred(p));
    int FirstVisible(Func<int, bool> pred) => First(p => G.State.Fog[p] == 2 && pred(p));

    void Check(string name, bool ok, string detail = null) { if (ok) Pass(name, detail); else Fail(name, detail); }
    void Pass(string name, string detail = null) { _pass++; GD.Print($"selftest PASS {name}{(detail != null ? "  · " + detail : "")}"); }
    void Fail(string name, string detail = null) { _fail++; GD.PrintErr($"selftest FAIL {name}{(detail != null ? "  · " + detail : "")}"); }

    static void PressKey(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    async Task Shot(string name)
    {
        if (_shots == null) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng($"{_shots}/{++_shotNo:00}_{name}.png");
    }
}
