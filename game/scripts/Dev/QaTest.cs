using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Map;
using PaxPixelia.Sim;
using PaxPixelia.UI;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Dev;

/// <summary>
/// QA harness (adversarial). --qa=all|actions,scouts,time,camera,ui,resize,det,longrun,soak,regen,edge
///   --qa-shots=DIR   screenshots;  --qa-minutes=N  soak length;  --qa-years=N  fast-forward length
/// Prints "qa PASS|FAIL|INFO name · detail".
/// </summary>
public partial class QaTest : Node
{
    readonly Main _main;
    readonly string _shots = Cli.Str("qa-shots");
    readonly string _groups = Cli.Str("qa", "all");
    int _pass, _fail, _shotNo;

    readonly List<(string icon, string text)> _notes = new();
    readonly List<(string text, ToastKind kind)> _toasts = new();
    int _worldReady, _fogEvents, _provEvents;

    Game G => Game.I;
    Hud Hud => _main.Hud;
    MapView Map => MapView.Current;

    public QaTest(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "QaTest";
        G.Notified += (i, t) => _notes.Add((i, t));
        G.Toast += (t, _, k) => _toasts.Add((t, k));
        G.WorldReady += () => _worldReady++;
        G.FogChanged += _ => _fogEvents++;
        G.ProvincesChanged += _ => _provEvents++;
        if (_shots != null) DirAccess.MakeDirRecursiveAbsolute(_shots);
        Run();
    }

    bool Want(string g) => _groups == "all" ? g != "soak" && g != "det2" : _groups.Split(',').Contains(g);

    async void Run()
    {
        try
        {
            while (!G.IsReady) await Frames(1);
            await Seconds(.8);
            if (Want("edge")) await Guard("edge", Edge);
            if (Want("actions")) await Guard("actions", Actions);
            if (Want("time")) await Guard("time", TimeTests);
            if (Want("scouts")) await Guard("scouts", ScoutTests);
            if (Want("camera")) await Guard("camera", CameraTests);
            if (Want("ui")) await Guard("ui", UiTests);
            if (Want("resize")) await Guard("resize", ResizeTests);
            if (Want("regen")) await Guard("regen", RegenTests);
            if (Want("det")) await Guard("det", Determinism);
            if (Want("det2")) await Guard("det2", DetPrint);
            if (Want("longrun")) await Guard("longrun", LongRun);
            if (Want("soak")) await Guard("soak", Soak);
            if (Want("extra")) await Guard("extra", Extra);
            if (Want("clicklost")) await Guard("clicklost", ClickLost);
            if (Want("cam2")) await Guard("cam2", Cam2);
            if (Want("focus")) await Guard("focus", Focus);
            if (Want("stale")) await Guard("stale", Stale);
        }
        catch (Exception e) { Fail("exception", e.ToString()); }
        GD.Print($"qa: {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail);
    }

    async Task Guard(string name, Func<Task> f)
    {
        GD.Print($"qa ---- {name}");
        try { await f(); }
        catch (Exception e) { Fail($"{name}: exception", e.ToString()); }
        // leave a sane state for the next group
        if (G.IsReady)
        {
            if (G.IsTargeting) G.CancelScoutTargeting();
            if (Hud.Lead.Visible) Hud.DebugToggleLead();
            G.SetFogEnabled(true);
        }
    }

    // =====================================================================================================
    async Task Edge()
    {
        var w = G.World;
        // public API with out-of-range provinces must not throw
        foreach (int q in new[] { -1, w.P, int.MaxValue, int.MinValue })
        {
            void Try(string what, Action a)
            {
                try { a(); Pass($"api {what}({q}) no throw"); }
                catch (Exception e) { Fail($"api {what}({q}) throws", e.GetType().Name + ": " + e.Message); }
            }
            Try("Claim", () => G.Claim(q));
            Try("CanClaim", () => G.CanClaim(q));
            Try("Build", () => G.Build(q, Bld.Farm));
            Try("BuildOptions", () => G.BuildOptions(q));
            Try("Survey", () => G.Survey(q));
            Try("MayHaveOre", () => G.MayHaveOre(q));
            Try("ProvinceTax", () => G.ProvinceTax(q));
            Try("SendScout", () => G.SendScout(q));
            Try("NationMet", () => G.NationMet(q));
            Try("Select", () => { G.Select(q); });
            Try("Hover", () => { G.Hover(q); });
        }
        await Frames(3);
        G.Hover(-1); G.Select(-1);
        await Frames(3);
        // odd enum values
        try { G.Build(Cap, (Bld)99); Pass("Build((Bld)99) no throw"); } catch (Exception e) { Fail("Build((Bld)99) throws", e.GetType().Name); }
        try { G.SetSpeed(0); Check("SetSpeed(0) clamps to 1", G.State.Speed == 1); G.SetSpeed(99); Check("SetSpeed(99) clamps to 5", G.State.Speed == 5); G.SetSpeed(2); }
        catch (Exception e) { Fail("SetSpeed throws", e.Message); }
        G.SetMode((MapMode)42);
        await Frames(3);
        Info("SetMode((MapMode)42)", $"mode={G.Mode}");
        G.SetMode(MapMode.Political);
        await Frames(2);
    }

    // =====================================================================================================
    async Task Actions()
    {
        var w = G.World; var s = G.State; int cap = Cap;
        G.SetPaused(true);
        G.Issue(Cmd.CheatTech(G.Viewer, -1));   // actions under test need the first era's knowledge
        double gold0 = s.Gold;

        // ---- claim when poor
        int p = First(G.CanClaim);
        int price = G.ClaimPrice;
        s.Gold = price - 1;
        int t0 = _toasts.Count;
        G.Select(p); await Frames(3);
        await Shot("claim_poor_panel");
        var claimBtn = FindButton(Hud.Panel, "Присоединить");
        Check("claim poor: panel button disabled", claimBtn != null && claimBtn.Disabled, claimBtn == null ? "no button" : $"disabled={claimBtn.Disabled}");
        G.Claim(p);
        Check("claim poor: refused, gold kept", s.Owner[p] < 0 && s.Gold == price - 1 && ErrorSince(t0, "золота"), ToastsSince(t0));
        // gold arrives while the panel is open → button must enable (live sync)
        s.Gold = price + 5;
        await RealYear(); await Frames(3);
        claimBtn = FindButton(Hud.Panel, "Присоединить");
        Check("claim: button re-enables when gold arrives (year tick)", claimBtn != null && !claimBtn.Disabled);
        s.Gold = price;
        G.Claim(p);
        Check("claim with exactly the cost", s.Owner[p] == 0 && Math.Abs(s.Gold) < 1e-9, $"gold={s.Gold}");
        s.Gold = gold0;

        // ---- claim non-adjacent / unexplored / sea / foreign / own
        int far = First(q => w.PLand[q] == 1 && s.Owner[q] < 0 && s.Explored[q] && !Rules.Borders(w, s, q, 0));
        t0 = _toasts.Count; G.Claim(far);
        Check("claim non-adjacent refused", far >= 0 && s.Owner[far] < 0 && ErrorSince(t0, "далеко"), ToastsSince(t0));
        int unex = First(q => w.PLand[q] == 1 && !s.Explored[q]);
        t0 = _toasts.Count; G.Claim(unex);
        Check("claim unexplored refused", s.Owner[unex] < 0 && ErrorSince(t0, "разведайте"), ToastsSince(t0));
        G.SetFogEnabled(false); await Frames(2);
        t0 = _toasts.Count; G.Claim(unex);
        Check("claim unexplored refused in observer mode", s.Owner[unex] < 0 && ErrorSince(t0, "разведайте"), ToastsSince(t0));
        // unexplored but adjacent (observer shows it; is the panel honest?)
        int unexAdj = First(q => w.PLand[q] == 1 && !s.Explored[q] && s.Owner[q] < 0 && Rules.Borders(w, s, q, 0));
        Info("unexplored province bordering own land", unexAdj < 0 ? "none (initial range 6 hides nothing next door)" : w.PName[unexAdj]);
        G.SetFogEnabled(true); await Frames(2);
        int sea = First(q => w.PLand[q] == 0 && s.Explored[q]);
        t0 = _toasts.Count; G.Claim(sea); Check("claim sea refused", ErrorSince(t0, "Море"), ToastsSince(t0));
        int foreign = First(q => s.Owner[q] > 0 && s.Explored[q]);
        t0 = _toasts.Count; G.Claim(foreign); Check("claim foreign refused", s.Owner[foreign] > 0 && ErrorSince(t0, "хозяин"), ToastsSince(t0));
        t0 = _toasts.Count; G.Claim(cap); Check("claim own capital refused", ErrorSince(t0, "хозяин"), ToastsSince(t0));

        // ---- build
        s.Gold = 100000; s.Materials = 10000;
        if (s.Nat[G.Viewer].Era < 1) s.Nat[G.Viewer].Era = 1;   // granaries and markets come with Древний мир: every plot can be filled
        int bp = p;   // just claimed
        var opts = G.BuildOptions(bp).ToList();
        Info("build target", $"{w.PName[bp]} slots={s.Slots[bp]} built={s.Buildings[bp].Count} options={string.Join(",", opts)}");
        if (opts.Count > 0)
        {
            var b0 = opts[0];
            G.Build(bp, b0);
            t0 = _toasts.Count;
            G.Build(bp, b0);
            int copies = s.Buildings[bp].Count(x => x == b0) + G.State.Builds.Count(j => j.Province == bp && j.Building == b0);
            Check("build duplicate refused", copies == 1 && (ErrorSince(t0, "уже есть") || ErrorSince(t0, "участков")), ToastsSince(t0));
            int guard = 0;
            while (G.BuildOptions(bp).Count > 0 && guard++ < 20) G.Build(bp, G.BuildOptions(bp)[0]);
            Check("slots filled (standing and going up)", Construction.Occupied(G.State, bp) == s.Slots[bp], $"{Construction.Occupied(G.State, bp)}/{s.Slots[bp]}");
            var notBuilt = new List<Bld>(); Rules.TerrainOptions(w, bp, notBuilt);
            notBuilt.RemoveAll(x => s.Buildings[bp].Contains(x) || Construction.Has(G.State, bp, x));
            if (notBuilt.Count > 0)
            {
                t0 = _toasts.Count; int n0 = Construction.Occupied(G.State, bp);
                G.Build(bp, notBuilt[0]);
                Check("build with full slots refused", Construction.Occupied(G.State, bp) == n0 && ErrorSince(t0, "участков"), ToastsSince(t0));
            }
            else Info("build full", "all terrain options built; cannot test NoSlot separately");
            G.Select(bp); await Frames(3); await Shot("build_full");
            Check("panel: no «Свободный участок» when full", FindClickPanel(Hud.Panel) == null);
        }
        int inland = First(q => s.Owner[q] == 0 && w.PCoast[q] == 0 && s.Buildings[q].Count < s.Slots[q]);
        if (inland >= 0)
        {
            t0 = _toasts.Count; G.Build(inland, Bld.Fishery);
            Check("fishery inland refused", !s.Buildings[inland].Contains(Bld.Fishery) && ErrorSince(t0, "Местность"), ToastsSince(t0));
            s.Gold = 10; t0 = _toasts.Count;
            G.Build(inland, Bld.Shrine);
            Check("build poor refused", !s.Buildings[inland].Contains(Bld.Shrine) || ErrorSince(t0, "золота"), ToastsSince(t0));
            s.Gold = 100000; s.Materials = 0; t0 = _toasts.Count;
            G.Build(inland, Bld.Shrine);
            Check("build without materials refused", !s.Buildings[inland].Contains(Bld.Shrine) && ErrorSince(t0, "материалов"), ToastsSince(t0));
            s.Materials = 10000;
        }
        t0 = _toasts.Count; G.Build(foreign, Bld.Shrine);
        Check("build abroad refused", ErrorSince(t0, "своих"), ToastsSince(t0));

        // ---- build menu gold sync: open the build menu while poor, then get rich → buttons stay disabled?
        int bp2 = First(q => s.Owner[q] == 0 && G.BuildOptions(q).Count > 0);
        if (bp2 >= 0)
        {
            s.Gold = 1;
            G.Select(bp2); await Frames(2);
            Hud.Panel.DebugOpenBuild(); await Frames(3);
            s.Gold = 100000; s.Materials = 10000; await RealYear(); await Frames(3);
            var menuBtns = AllButtons(Hud.Panel).Where(b => b.ThemeTypeVariation == "Menu").ToList();
            int disabled = menuBtns.Count(b => b.Disabled);
            Check("build menu: buttons re-enable when gold arrives", menuBtns.Count > 0 && disabled == 0, $"{disabled}/{menuBtns.Count} still disabled with gold {s.Gold:F0}");
            await Shot("build_menu_after_gold");
        }

        // ---- survey
        int sv = First(q => s.Owner[q] == 0 && !s.OreFound[q] && !G.MayHaveOre(q));
        if (sv >= 0)
        {
            double g0 = s.Gold; t0 = _toasts.Count;
            G.Survey(sv);
            Info("survey flat land through the API", $"allowed={s.OreFound[sv]} charged={g0 - s.Gold} (UI hides the button)");
            t0 = _toasts.Count; g0 = s.Gold;
            G.Survey(sv);
            Check("survey twice refused", g0 == s.Gold && ErrorSince(t0, "уже"), ToastsSince(t0));
        }
        int sv2 = First(q => s.Owner[q] == 0 && !s.OreFound[q]);
        if (sv2 >= 0)
        {
            s.Gold = 5; t0 = _toasts.Count;
            G.Survey(sv2);
            Check("survey poor refused", !s.OreFound[sv2] && ErrorSince(t0, "золота"), ToastsSince(t0));
        }
        t0 = _toasts.Count; G.Survey(foreign); Check("survey abroad refused", ErrorSince(t0, "свои"), ToastsSince(t0));
        s.Gold = 100000; s.Materials = 10000;

        // ---- capital queue vs. a building the player already built by hand
        {
            int mk = Array.FindIndex(Simulation.Projects, pr => pr.Building == Bld.Shrine);   // the market waits for Древний мир
            s.Buildings[cap].Remove(Bld.Shrine);
            if (s.Buildings[cap].Count >= s.Slots[cap]) s.Slots[cap]++;
            s.ProjectIndex = mk; s.QueuePct = 99;
            G.Build(cap, Bld.Shrine);
            int notes0 = _notes.Count;
            G.RunTicks(Clock.CycleTicks);
            await Frames(2);
            bool lie = _notes.Skip(notes0).Any(n => n.text == Simulation.Projects[mk].DoneText);
            int markets = s.Buildings[cap].Count(b => b == Bld.Shrine) + G.State.Builds.Count(j => j.Province == cap && j.Building == Bld.Shrine);
            Check("queue: project already built by hand is not «completed» again", !lie, $"notes: {string.Join(" | ", _notes.Skip(notes0).Select(n => n.text))}; markets={markets}");
        }

        // ---- claim in a pause is free of time; gold accounting after many claims
        s.Gold = gold0;
        G.SetPaused(false);
        G.Select(cap);
        await Frames(3);
    }

    // =====================================================================================================
    async Task TimeTests()
    {
        var s = G.State;
        G.SetPaused(true);
        var years = new List<int>();
        foreach (int y in new[] { -2, -1, 1, 2 }) years.Add(Calendar.DateOf(Calendar.DayOfYear(y)).Year);
        years.Add(Calendar.DateOf(Calendar.DayOfYear(1) - Calendar.DayUnit).Year);
        Check("calendar: −2 → −1 → 1 → 2, the day before 1 н. э. is in 1 до н. э. (no year zero)", string.Join(",", years) == "-2,-1,1,2,-1", string.Join(",", years));
        Info("date text", $"{G.DateText} | {GameState.YearText(-1)} | {GameState.YearText(1)}");

        G.SetPaused(false);
        foreach (var (k, want) in new[] { (Key.Kp3, 3), (Key.Key1, 1), (Key.Kp5, 5), (Key.Key2, 2) })
        {
            PressKey(k); await Frames(3);
            Check($"key {k} → speed {want}", s.Speed == want, $"speed={s.Speed}");
        }
        // Space toggles; Space while a UI button was just clicked (focus trap?)
        var trophy = Hud.Top.Trophy;
        ClickAt(Center(trophy)); await Frames(4);
        bool leadOpen = Hud.Lead.Visible;
        bool paused0 = s.Paused;
        PressKey(Key.Space); await Frames(3);
        Check("Space after clicking a top-bar button pauses (no focus trap)", s.Paused != paused0 && Hud.Lead.Visible == leadOpen, $"paused {paused0}→{s.Paused}, lead {leadOpen}→{Hud.Lead.Visible}");
        PressKey(Key.Space); await Frames(3);
        if (Hud.Lead.Visible) { PressKey(Key.Escape); await Frames(3); }

        // speed while paused
        G.SetPaused(true);
        PressKey(Key.Key4); await Frames(3);
        long y1 = s.Tick;
        await Seconds(.6);
        Check("speed key while paused keeps the pause", s.Paused && s.Tick == y1 && s.Speed == 4);
        G.SetPaused(false);

        // speed 5 rate and speed 1 rate
        G.SetSpeed(1); y1 = s.Tick; await Seconds(2.05);
        long r1 = s.Tick - y1;
        G.SetSpeed(5); y1 = s.Tick; await Seconds(2.05);
        long r5 = s.Tick - y1;
        Check("speed 1 = 2 ticks/s and speed 5 = 40 ticks/s", r1 >= 3 && r1 <= 5 && r5 >= 72 && r5 <= 84, $"speed1 {r1} ticks / 2 s, speed5 {r5} ticks / 2 s");
        G.SetSpeed(2);
    }

    // =====================================================================================================
    async Task ScoutTests()
    {
        var w = G.World; var s = G.State; int cap = Cap;
        G.SetPaused(false); G.SetSpeed(2);
        G.Select(cap);
        await Frames(3);

        // targeting + bad picks
        G.BeginScoutTargeting(); await Frames(2);
        int sea = First(q => w.PLand[q] == 0 && s.Fog[q] != 0);
        int t0 = _toasts.Count; G.SendScout(sea);
        Check("scout → sea refused, still targeting", G.IsTargeting && s.Scouts.Count == 0 && ErrorSince(t0, "суше"), ToastsSince(t0));
        t0 = _toasts.Count; G.SendScout(cap);
        Check("scout → capital refused, still targeting", G.IsTargeting && s.Scouts.Count == 0 && ErrorSince(t0, "столице"), ToastsSince(t0));
        int island = First(q => w.PLand[q] == 1 && s.Fog[q] == 0 && !w.SameBody(q, cap));
        t0 = _toasts.Count; G.SendScout(island);
        Check("scout → unexplored island refused without revealing why, still targeting", island >= 0 && G.IsTargeting && s.Scouts.Count == 0 && ErrorSince(t0, "не нашли туда пути"), ToastsSince(t0));
        int islandKnown = First(q => w.PLand[q] == 1 && s.Fog[q] != 0 && !w.SameBody(q, cap));
        if (islandKnown >= 0)
        {
            t0 = _toasts.Count; G.SendScout(islandKnown);
            Check("scout → known island refused", G.IsTargeting && s.Scouts.Count == 0 && ErrorSince(t0, "добраться"), ToastsSince(t0));
        }
        await Shot("targeting_error");
        // BeginScoutTargeting again toggles off
        G.BeginScoutTargeting(); await Frames(2);
        Check("second «Отправить разведчиков» press cancels targeting", !G.IsTargeting);
        // right click cancels
        G.BeginScoutTargeting(); await Frames(2);
        var mapPt = new Vector2(GetViewport().GetVisibleRect().Size.X * .4f, GetViewport().GetVisibleRect().Size.Y * .6f);
        Motion(mapPt); await Frames(2);
        MouseButtonAt(mapPt, MouseButton.Right, true); MouseButtonAt(mapPt, MouseButton.Right, false); await Frames(3);
        Check("right click on the map cancels targeting", !G.IsTargeting);
        // Esc cancels
        G.BeginScoutTargeting(); await Frames(2);
        PressKey(Key.Escape); await Frames(3);
        Check("Esc cancels targeting", !G.IsTargeting && G.Selected == cap, $"selected={G.Selected}");

        // real click on an unexplored province while targeting → scout goes there
        int target = FarUnexplored(cap, 380);
        G.JumpCamera(new Vector2(w.PCX[target], w.PCY[target])); await Seconds(.6);
        G.BeginScoutTargeting(); await Frames(2);
        var sp = ScreenOf(target);
        Motion(sp); await Frames(2);
        ClickAt(sp); await Frames(4);
        Check("real click while targeting sends a party", s.Scouts.Count == 1 && !G.IsTargeting && Scouts.Target(s.Scouts[0]) == Map.ProvinceAt(Map.View.ToWorld(sp)), $"scouts={s.Scouts.Count}");
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); await Seconds(.5);

        bool auto = G.SendScoutAuto();
        t0 = _toasts.Count;
        bool third = G.SendScoutAuto();
        bool third2 = G.SendScout(FarUnexplored(cap, 200));
        G.BeginScoutTargeting();
        Check("third party refused (auto, manual, targeting)", auto && !third && !third2 && !G.IsTargeting && s.Scouts.Count == 2 && ErrorSince(t0, "в пути"), ToastsSince(t0));

        await UnitOrderTests();

        // pause freezes the scouts
        await Seconds(.5);
        G.SetPaused(true);
        await Frames(2);
        var snap = s.Scouts.Select(x => (x.Step, x.Sub, x.Path.Length)).ToList();
        long y0 = s.Tick;
        await Seconds(1.2);
        var snap2 = s.Scouts.Select(x => (x.Step, x.Sub, x.Path.Length)).ToList();
        Check("pause freezes scouts and the clock", snap.SequenceEqual(snap2) && s.Tick == y0, $"{string.Join(";", snap)} → {string.Join(";", snap2)}");
        await Shot("scouts_paused");
        G.SetPaused(false);

        // speed affects walking rate
        G.SetSpeed(1);
        int st0 = s.Scouts.Sum(x => x.Steps); await Seconds(2);
        int r1 = s.Scouts.Sum(x => x.Steps) - st0;
        G.SetSpeed(5);
        st0 = s.Scouts.Sum(x => x.Steps); await Seconds(2);
        int r5 = s.Scouts.Sum(x => x.Steps) - st0;
        Info("scout steps in 2 s (2 parties)", $"speed1={r1} speed5={r5} (expect ≈3.2 and ≈64)");
        G.SetSpeed(2);

        // observer toggles while walking
        for (int i = 0; i < 8; i++) { G.SetFogEnabled(i % 2 == 1); await Frames(4); }
        G.SetFogEnabled(true); await Frames(4);
        int bad = 0;
        for (int q = 0; q < w.P; q++) if ((s.Fog[q] != 0) != s.Explored[q]) bad++;
        Check("fog consistent after observer toggles mid-walk", bad == 0, $"{bad} provinces with Fog≠Explored");
        await Shot("scouts_after_observer");

        // selection of the target shows it in the panel / tooltip without leaking
        // regenerate while walking + targeting
        G.BeginScoutTargeting();
        await Frames(2);
        int ready = _worldReady;
        var oldState = s;
        G.RegenerateWorld(G.Seed);
        var t = Time.GetTicksMsec();
        while (_worldReady == ready && Time.GetTicksMsec() - t < 15000) await Frames(2);
        await Seconds(.8);
        s = G.State;
        Check("regen while walking: clean new state", s != oldState && s.Scouts.Count == 0 && !G.IsTargeting && G.FreeScouts == 2);
        Check("regen: no «отменена» toast leaking into the new world", !_toasts.Skip(Math.Max(0, _toasts.Count - 1)).Any(x => x.text.Contains("отменена")) || Hud.ToastView.Visible == false,
            $"last toast «{_toasts.LastOrDefault().text}» visible={Hud.ToastView.Visible}");
        await Shot("regen_after_walk");

        // scout to an own neighbour: 1 step and back
        w = G.World; cap = Cap;
        int nb = w.Adj[cap].FirstOrDefault(q => w.PLand[q] == 1);
        int n0 = _notes.Count;
        G.SendScout(nb);
        t = Time.GetTicksMsec();
        while (s.Scouts.Count > 0 && Time.GetTicksMsec() - t < 5000) await Frames(3);
        Check("scout to a neighbour returns", s.Scouts.Count == 0 && _notes.Skip(n0).Any(n => n.icon == "map-2"), _notes.Skip(n0).Select(n => n.text).LastOrDefault());

        // auto scout with nothing to find on the continent
        var ex = (bool[])s.Explored.Clone();
        for (int q = 0; q < w.P; q++) if (w.SameBody(q, cap)) s.Explored[q] = true;
        t0 = _toasts.Count;
        bool ok = G.SendScoutAuto();
        Check("auto scout with nothing left refused", !ok && ErrorSince(t0, "не осталось"), ToastsSince(t0));
        Array.Copy(ex, s.Explored, ex.Length);

        // frame-rate dependence of scouts: the same real time at 30, 60 and 144 fps
        ScoutBatching();
    }

    void ScoutBatching()
    {
        var w = G.World;
        string Run(int fps, int offset)
        {
            var st = NationGen.CreateInitialState(w);
            Simulation.Begin(w, st);
            Scouts.Send(w, st, 0, -1, null, out _);
            for (int k = 0; k < offset; k++) Scouts.Tick(w, st, null);
            Scouts.Send(w, st, 0, -1, null, out _);
            var pump = new TickPump();
            long frame = TickPump.MicrosPerSecond / fps, left = 60 * TickPump.MicrosPerSecond;
            for (; left > 0; left -= frame)
                for (int n = pump.Advance(Math.Min(frame, left), Clock.TicksPerSecond[3]); n > 0; n--) Scouts.Tick(w, st, null);
            var h = new Hash();
            foreach (var e in st.Explored) h.Add(e ? 1 : 0);
            foreach (var sc in st.Scouts) { h.Add(sc.Step); h.Add(sc.Sub); foreach (var q in sc.Path) h.Add(q); }
            return $"{h.Value:X16} explored={st.Explored.Count(e => e)} scouts={st.Scouts.Count}";
        }
        foreach (int off in new[] { 1, 3 })
        {
            string a = Run(30, off), b = Run(60, off), c = Run(144, off);
            Check($"scouts reproducible at any frame rate (offset {off})", a == b && b == c, $"30 fps {a} | 60 fps {b} | 144 fps {c}");
        }
    }

    // =====================================================================================================
    async Task CameraTests()
    {
        var w = G.World; int cap = Cap;
        G.Select(-1);
        var scr = GetViewport().GetVisibleRect().Size;
        await ZoomTo(3);

        // zoom glide interrupted by a jump
        G.RequestZoom(+1);
        await Frames(2);
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap]));
        await Seconds(1);
        Check("zoom glide + jump: settles on an integer zoom", Math.Abs(Map.View.Zoom - G.ZoomLevel) < 1e-4, $"view zoom {Map.View.Zoom} level {G.ZoomLevel}");
        await Shot("zoom_then_jump");
        // recover
        G.RequestZoom(-1); await Seconds(.4); G.RequestZoom(+1); await Seconds(.4);

        // zoom glide interrupted by a drag
        await ZoomTo(3);
        var mid = scr / 2;
        Motion(mid); await Frames(1);
        MouseButtonAt(mid, MouseButton.WheelUp, true); MouseButtonAt(mid, MouseButton.WheelUp, false);
        await Frames(1);
        var o0 = Map.View.Origin;
        MouseButtonAt(mid, MouseButton.Left, true);
        for (int i = 1; i <= 8; i++) { Motion(mid + new Vector2(20 * i, 0), MouseButtonMask.Left); await Frames(1); }
        MouseButtonAt(mid + new Vector2(160, 0), MouseButton.Left, false);
        await Seconds(.5);
        Info("drag during zoom glide", $"origin moved {(Map.View.Origin - o0)} (a drag of 160 px), zoom {Map.View.Zoom}");

        // rapid zoom in/out every frame
        for (int i = 0; i < 40; i++) { G.RequestZoom(i % 3 == 2 ? -1 : 1); await Frames(1); }
        await Seconds(.6);
        Check("rapid zoom: integer and consistent", Math.Abs(Map.View.Zoom - G.ZoomLevel) < 1e-4 && MapCamera.Levels.Contains(G.ZoomLevel), $"view {Map.View.Zoom} level {G.ZoomLevel}");
        for (int i = 0; i < 20; i++) { G.RequestZoom(+1); }
        await Seconds(.5);
        Check("zoom clamps at ×8", G.ZoomLevel == 8 && Math.Abs(Map.View.Zoom - 8) < 1e-4);
        for (int i = 0; i < 20; i++) { G.RequestZoom(-1); }
        await Seconds(.5);
        Check("zoom clamps at ×½ (the atlas level)", G.ZoomLevel == 0 && Math.Abs(Map.View.Zoom - .5f) < 1e-4, $"level {G.ZoomLevel}, view {Map.View.Zoom}");

        // seam jumps
        G.SetFogEnabled(false);
        await ZoomTo(3);
        foreach (float x in new[] { 0f, -3f, w.W - 1f, w.W + 5f, 2 * w.W + 17f })
        {
            G.JumpCamera(new Vector2(x, w.H / 2f)); await Seconds(.5);
            float cx = CamCenterX();
            float want = Mathf.PosMod(x, w.W);
            float d = Math.Abs(cx - want); d = Math.Min(d, w.W - d);
            var probe = scr / 2;
            int prov = Map.ProvinceAt(Map.View.ToWorld(probe));
            int wantProv = w.Prov[(int)(w.H / 2f) * w.W + (int)Mathf.PosMod(Mathf.Floor(Map.View.ToWorld(probe).X), w.W)];
            Check($"jump to x={x}: centre wraps", d < 1.5f && prov == wantProv, $"centre {cx} want {want}");
        }
        await Shot("seam");

        // short way round: from x=10 to x=W-10 must travel 20 px, not the whole map
        G.JumpCamera(new Vector2(10, w.H / 2f)); await Seconds(.6);
        float prev = CamCenterX(), maxStep = 0, travel = 0;
        G.JumpCamera(new Vector2(w.W - 10, w.H / 2f));
        for (int i = 0; i < 40; i++)
        {
            await Frames(1);
            float c = CamCenterX(), dd = c - prev; if (dd > w.W / 2f) dd -= w.W; if (dd < -w.W / 2f) dd += w.W;
            maxStep = Math.Max(maxStep, Math.Abs(dd)); travel += dd; prev = c;
        }
        Check("jump across the seam takes the short way", Math.Abs(travel + 20) < 2 && maxStep < 10, $"travel {travel:F1} max step {maxStep:F1}");

        // keyboard pan across the seam at ×1: no jumps
        await ZoomTo(1);
        G.JumpCamera(new Vector2(200, w.H / 2f)); await Seconds(.6);
        prev = CamCenterX(); maxStep = 0; travel = 0;
        Input.ActionPress("map_pan_left");
        var t0 = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t0 < 700)
        {
            await Frames(1);
            float c = CamCenterX(), dd = c - prev; if (dd > w.W / 2f) dd -= w.W; if (dd < -w.W / 2f) dd += w.W;
            maxStep = Math.Max(maxStep, Math.Abs(dd)); travel += dd; prev = c;
        }
        Input.ActionRelease("map_pan_left");
        Check("keyboard pan over the seam is continuous", travel < -300 && maxStep < 100, $"travel {travel:F0} max step {maxStep:F1} centre {CamCenterX():F0}");
        await Shot("seam_pan_x1");

        // wheel zoom anchored on a point right at the seam
        await ZoomTo(2);
        G.JumpCamera(new Vector2(0, w.H / 2f)); await Seconds(.6);
        var pt = new Vector2(scr.X / 2 + 3, scr.Y / 2);
        Motion(pt); await Frames(2);
        var before = Map.View.ToWorld(pt);
        MouseButtonAt(pt, MouseButton.WheelUp, true); MouseButtonAt(pt, MouseButton.WheelUp, false);
        await Seconds(.5);
        var after = Map.View.ToWorld(pt);
        float drift = Math.Abs(Mathf.PosMod(after.X - before.X + w.W / 2f, w.W) - w.W / 2f) + Math.Abs(after.Y - before.Y);
        Check("wheel zoom at the seam keeps the anchor", drift < 1f, $"before {before} after {after}");
        // drag across the seam
        var o1 = Map.View.Origin;
        MouseButtonAt(pt, MouseButton.Left, true);
        for (int i = 1; i <= 12; i++) { Motion(pt + new Vector2(30 * i, 0), MouseButtonMask.Left); await Frames(1); }
        MouseButtonAt(pt + new Vector2(360, 0), MouseButton.Left, false);
        await Frames(3);
        Info("drag across seam", $"origin {o1} → {Map.View.Origin}, centre {CamCenterX():F1}");

        // vertical clamp at every zoom
        foreach (int lv in new[] { 1, 3, 8 })
        {
            await ZoomTo(lv);
            G.JumpCamera(new Vector2(500, -5000)); await Seconds(.5);
            var r = G.CameraRect;
            // the camera may show up to ~94 screen px above the map so its top edge clears the top bar
            Check($"top clamp at ×{lv}", r.Position.Y > -100 / (float)lv && r.Position.Y < 60, $"rect {r}");
            G.JumpCamera(new Vector2(500, 99999)); await Seconds(.5);
            r = G.CameraRect;
            Check($"bottom clamp at ×{lv}", r.End.Y < w.H + 60 && r.End.Y > w.H - 60, $"rect {r}");
        }
        G.SetFogEnabled(true);
        await ZoomTo(3);
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); await Seconds(.5);
    }

    float CamCenterX() { var r = G.CameraRect; return Mathf.PosMod(r.Position.X + r.Size.X / 2, G.World.W); }

    // =====================================================================================================
    async Task UiTests()
    {
        var w = G.World; var s = G.State; int cap = Cap;
        await ZoomTo(3);
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); await Seconds(.5);
        G.Select(cap); await Seconds(.4);
        var panel = Hud.Panel.GetGlobalRect();
        Info("panel rect", panel.ToString());

        // click on the panel header
        var pt = panel.Position + new Vector2(panel.Size.X * .4f, 26);
        int under = Map.ProvinceAt(Map.View.ToWorld(pt));
        Motion(pt); await Frames(2);
        Check("hover over the panel clears map hover", G.Hovered == -1, $"hovered={G.Hovered}");
        ClickAt(pt); await Frames(3);
        Check("click on the panel does not reach the map", G.Selected == cap, $"selected={G.Selected} (province under panel {under})");

        // targeting: clicks on UI must not send scouts
        G.BeginScoutTargeting(); await Frames(2);
        int sc0 = s.Scouts.Count;
        ClickAt(panel.Position + new Vector2(panel.Size.X * .5f, panel.Size.Y * .5f)); await Frames(3);
        ClickAt(new Vector2(GetViewport().GetVisibleRect().Size.X * .5f, 20)); await Frames(3);   // top bar middle
        var noteRect = Hud.Notes.GetGlobalRect();
        Check("targeting: clicks on panel/top bar send nothing", s.Scouts.Count == sc0 && G.IsTargeting, $"scouts {sc0}→{s.Scouts.Count} targeting={G.IsTargeting}");
        var toastRect = Hud.ToastView.Visible ? Hud.ToastView.GetGlobalRect() : new Rect2();
        if (toastRect.Size.X > 0)
        {
            ClickAt(toastRect.GetCenter()); await Frames(3);
            Info("targeting: click on the pick toast", $"scouts={s.Scouts.Count} targeting={G.IsTargeting} (toast blocks the map under it: {toastRect})");
        }
        var mini = Hud.Mini.View.GetGlobalRect();
        var cam0 = G.CameraRect;
        ClickAt(mini.Position + new Vector2(mini.Size.X * .25f, mini.Size.Y * .5f)); await Seconds(.6);
        Check("targeting: minimap click jumps, sends nothing", s.Scouts.Count == sc0 && G.CameraRect != cam0 && G.IsTargeting);
        G.CancelScoutTargeting(); await Frames(2);
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); await Seconds(.5);

        // wheel over UI
        int z0 = G.ZoomLevel;
        foreach (var (name, r) in new[] { ("panel", panel), ("top bar", Hud.Top.GetGlobalRect()), ("mode strip", ModesRect()) })
        {
            var c = r.GetCenter();
            Motion(c); await Frames(1);
            MouseButtonAt(c, MouseButton.WheelDown, true); MouseButtonAt(c, MouseButton.WheelDown, false);
            await Seconds(.3);
            Check($"wheel over the {name} does not zoom the map", G.ZoomLevel == z0, $"zoom {z0}→{G.ZoomLevel}");
            if (G.ZoomLevel != z0) await ZoomTo(z0);
        }

        // drag from the map into the panel
        var scr = GetViewport().GetVisibleRect().Size;
        var start = new Vector2(panel.Position.X - 200, scr.Y * .5f);
        var o0 = Map.View.Origin;
        Motion(start); await Frames(1);
        MouseButtonAt(start, MouseButton.Left, true); await Frames(1);
        for (int i = 1; i <= 10; i++) { Motion(start + new Vector2(30 * i, 0), MouseButtonMask.Left); await Frames(1); }
        MouseButtonAt(start + new Vector2(300, 0), MouseButton.Left, false); await Frames(3);
        Check("drag from map over the panel pans, keeps selection", (Map.View.Origin - o0).X > 250 && G.Selected == cap, $"moved {(Map.View.Origin - o0)} selected={G.Selected}");
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); await Seconds(.5);

        // the free-slot ClickPanel and build menu buttons rebuild the panel under the cursor
        s.Gold = 100000; s.Materials = 10000;
        int own = First(q => s.Owner[q] == 0 && s.Buildings[q].Count < s.Slots[q] && G.BuildOptions(q).Count > 0);
        if (own >= 0)
        {
            G.Select(own); await Seconds(.4);
            var slot = FindClickPanel(Hud.Panel);
            if (slot != null)
            {
                Hud.Panel.DebugScroll(0); await Frames(2);
                var sr = slot.GetGlobalRect();
                if (sr.End.Y > GetViewport().GetVisibleRect().Size.Y) { Hud.Panel.DebugScroll((int)(sr.End.Y - 500)); await Frames(3); sr = slot.GetGlobalRect(); }
                under = Map.ProvinceAt(Map.View.ToWorld(sr.GetCenter()));
                ClickAt(sr.GetCenter()); await Frames(4);
                var menu = AllButtons(Hud.Panel).Where(b => b.ThemeTypeVariation == "Menu").ToList();
                Check("free-slot click opens the build menu, selection kept", menu.Count > 0 && G.Selected == own, $"menu {menu.Count} selected={G.Selected} under={under}");
                await Shot("build_menu_real_click");
                if (menu.Count > 0)
                {
                    int nb = s.Buildings[own].Count;
                    var br = menu[0].GetGlobalRect();
                    ClickAt(br.GetCenter()); await Frames(4);
                    Check("build menu click builds, selection kept", s.Buildings[own].Count == nb + 1 && G.Selected == own, $"buildings {nb}→{s.Buildings[own].Count} selected={G.Selected}");
                }
            }
            else Info("free slot", "no ClickPanel found");
        }

        // claim button via real click
        int cl = First(G.CanClaim);
        if (cl >= 0)
        {
            G.JumpCamera(new Vector2(w.PCX[cl], w.PCY[cl])); G.Select(cl); await Seconds(.5);
            var btn = FindButton(Hud.Panel, "Присоединить");
            if (btn != null)
            {
                ClickAt(btn.GetGlobalRect().GetCenter()); await Frames(5);
                Check("claim via real click, panel shows own province", s.Owner[cl] == 0 && G.Selected == cl && Hud.Panel.Province == cl);
            }
        }

        // notification click dismisses and doesn't select the map
        G.Notify("cat", "Проверка: кот уснул на карте");
        await Seconds(.4);
        int sel = G.Selected;
        var nr = Hud.Notes.GetChild<Control>(Hud.Notes.GetChildCount() - 1).GetGlobalRect();
        ClickAt(nr.GetCenter()); await Seconds(.3);
        Check("notification click does not reach the map", G.Selected == sel, $"selected {sel}→{G.Selected}");
        // click again at the same spot right after (the dying card must swallow it too)
        ClickAt(nr.GetCenter()); await Frames(2);
        Info("second click on a dying note", $"selected {sel}→{G.Selected}");

        // Esc precedence: targeting > leaderboard > panel
        G.Select(cap); await Frames(2);
        Hud.DebugToggleLead(); G.BeginScoutTargeting(); await Frames(2);
        PressKey(Key.Escape); await Frames(2);
        bool a = !G.IsTargeting && Hud.Lead.Visible && Hud.Panel.Visible;
        PressKey(Key.Escape); await Frames(2);
        bool b = !Hud.Lead.Visible && Hud.Panel.Visible;
        PressKey(Key.Escape); await Frames(2);
        bool c2 = !Hud.Panel.Visible && G.Selected == -1;
        Check("Esc order: targeting → leaderboard → panel", a && b && c2, $"{a} {b} {c2}");

        // tooltip over a fog province does not leak; over stale shows current owner?
        int stale = First(q => s.Fog[q] == 1 && w.PLand[q] == 1 && s.Owner[q] > 0);
        Info("stale foreign province exists", stale >= 0 ? w.PName[stale] : "none");
        G.Select(cap); await Frames(2);
    }

    Rect2 ModesRect()
    {
        var p = Hud.Mini.GetParent<Control>();
        return p.GetChild<Control>(0).GetGlobalRect();
    }

    // =====================================================================================================
    async Task ResizeTests()
    {
        var win = GetWindow();
        var old = win.Size;
        G.Select(Cap); await Frames(3);
        foreach (var size in new[] { new Vector2I(1024, 600), new Vector2I(800, 600), new Vector2I(640, 480), new Vector2I(1366, 768), new Vector2I(1920, 1080), new Vector2I(2400, 1000) })
        {
            win.Size = size;
            await Seconds(.6);
            var scr = GetViewport().GetVisibleRect().Size;
            var top = Hud.Top.GetCombinedMinimumSize();
            var pr = Hud.Panel.GetGlobalRect();
            var mini = Hud.Mini.GetGlobalRect();
            Check($"{size}: viewport follows", scr == (Vector2)size, $"{scr}");
            Check($"{size}: camera covers the screen", (G.CameraRect.Size * G.ZoomLevel - scr).Length() < 1.5f, $"{G.CameraRect.Size * G.ZoomLevel}");
            Check($"{size}: top bar fits", top.X <= scr.X + .5f, $"top bar min width {top.X}");
            Check($"{size}: panel inside the window", pr.End.Y <= scr.Y + .5f && pr.Position.X >= 0, $"panel {pr}");
            Check($"{size}: panel clear of the minimap", !pr.Intersects(mini), $"panel {pr} minimap {mini}");
            await Shot($"size_{size.X}x{size.Y}");
        }
        // minimise / restore
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
        await Seconds(.8);
        Info("minimised viewport", GetViewport().GetVisibleRect().Size.ToString());
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        await Seconds(.8);
        win.Size = old;
        await Seconds(.6);
        var scr2 = GetViewport().GetVisibleRect().Size;
        Check("restore after minimise: camera covers the screen", (G.CameraRect.Size * G.ZoomLevel - scr2).Length() < 1.5f, $"{scr2} {G.CameraRect}");
        await Shot("restored");
    }

    // =====================================================================================================
    async Task RegenTests()
    {
        // spam «Новый мир» through the real button, 5 times within a few frames
        var regen = (BaseButton)Hud.Mini.DebugTarget("regen");
        int ready = _worldReady;
        for (int i = 0; i < 5; i++) { regen.EmitSignal(BaseButton.SignalName.Pressed); await Frames(1); }
        var t = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t < 8000) await Frames(5);
        Check("5× «Новый мир»: exactly one WorldReady", _worldReady == ready + 1 && G.IsReady, $"ready events {_worldReady - ready} seed {G.Seed}");
        Check("regen: loading screen gone", !Hud.Loading.Visible);

        // edge seeds
        foreach (int seed in new[] { 0, -1, int.MaxValue, int.MinValue, 999999 })
        {
            ready = _worldReady;
            G.RegenerateWorld(seed);
            t = Time.GetTicksMsec();
            while (_worldReady == ready && Time.GetTicksMsec() - t < 20000) await Frames(3);
            await Seconds(.3);
            var w = G.World; var s = G.State;
            int cap = Cap;
            int land = 0; for (int p = 0; p < w.P; p++) if (w.PLand[p] == 1) land++;
            int ownLand = 0; for (int p = 0; p < w.P; p++) if (s.Owner[p] == 0) ownLand++;
            int body = 0; for (int p = 0; p < w.P; p++) if (w.PLand[p] == 1 && w.SameBody(p, cap)) body++;
            Check($"seed {seed}: world ready", G.IsReady && G.World.Seed == G.Seed, $"seed used {G.Seed} P={w.P} land={land} own={ownLand} capital body={body} provinces, {G.LastGenerationMs} ms");
            await Shot($"seed_{seed}");
        }
        // regen during a pending UI action: build menu open, leaderboard, observer
        G.Select(Cap); await Frames(2);
        Hud.Panel.DebugOpenBuild();
        G.SetFogEnabled(false);
        Hud.DebugToggleLead();
        ready = _worldReady;
        regen.EmitSignal(BaseButton.SignalName.Pressed);
        t = Time.GetTicksMsec();
        while (_worldReady == ready && Time.GetTicksMsec() - t < 15000) await Frames(3);
        await Seconds(.8);
        Check("regen from observer mode: the new world has fog on", G.State.FogEnabled, $"FogEnabled={G.State.FogEnabled}");
        var mode = Hud.Mini;   // mode strip eye icon consistent?
        await Shot("regen_from_observer");
    }

    // =====================================================================================================
    /// <summary>Same seed → same world, same state after N years (in-process, through two regenerations).</summary>
    async Task Determinism()
    {
        var a = await DetRun(4321, 400);
        var b = await DetRun(4321, 400);
        var c = await DetRun(4322, 50);
        Check("same seed → same world", a.world == b.world, $"{a.world:X} {b.world:X}");
        Check("same seed → same initial state", a.init == b.init, $"{a.init:X} {b.init:X}");
        Check("same seed → same events and state after 400 years", a.events == b.events && a.final == b.final, $"events {a.events:X}/{b.events:X} final {a.final:X}/{b.final:X} ({a.count} notes)");
        Check("different seed → different world", a.world != c.world);

        // leak check: stale provinces change owners in plain sight
        var w = G.World; var s = G.State;
        G.SetPaused(true);
        var staleOwner = new Dictionary<int, int>();
        for (int p = 0; p < w.P; p++) if (s.Fog[p] == 1 && w.PLand[p] == 1) staleOwner[p] = s.Owner[p];
        var rec = new Recorder();
        for (int y = 0; y < 300 * Clock.CycleTicks; y++) Simulation.Step(w, s, rec);
        int changed = staleOwner.Count(kv => s.Fog[kv.Key] == 1 && s.Owner[kv.Key] != kv.Value);
        int metNotes = rec.Notes.Count(n => n.icon == "affiliate");
        Info("stale provinces whose owner changed while stale (visible on the map / tooltip / panel)", $"{changed} of {staleOwner.Count}; «Встречена новая держава» notes: {metNotes}");
        G.RaiseProvincesChanged(null); G.RaiseFogChanged(null);
        G.SetMode(MapMode.Political);
        await Seconds(.5);
        await Shot("stale_after_300y");
        G.SetPaused(false);
    }

    async Task DetPrint()
    {
        var r = await DetRun(Cli.Int("seed", 1337), Cli.Int("qa-years", 400));
        GD.Print($"qa DET seed={G.Seed} world={r.world:X16} init={r.init:X16} events={r.events:X16} final={r.final:X16} notes={r.count}");
    }

    async Task<(ulong world, ulong init, ulong events, ulong final, int count)> DetRun(int seed, int years)
    {
        int ready = _worldReady;
        if (G.Seed != seed || _worldReady > 1 || true)
        {
            G.RegenerateWorld(seed);
            var t = Time.GetTicksMsec();
            while (_worldReady == ready && Time.GetTicksMsec() - t < 20000) await Frames(2);
        }
        G.SetPaused(true);
        await Frames(2);
        var w = G.World; var s = G.State;
        ulong wh = HashWorld(w), ih = HashState(s);
        var rec = new Recorder();
        for (int y = 0; y < years * Clock.CycleTicks; y++) Simulation.Step(w, s, rec);
        var eh = new Hash(); foreach (var (i, t) in rec.Notes) { eh.Add(i); eh.Add(t); }
        ulong fh = HashState(s);
        G.SetPaused(false);
        return (wh, ih, eh.Value, fh, rec.Notes.Count);
    }

    sealed class Recorder : ISimSink
    {
        public readonly List<(string icon, string text)> Notes = new();
        public void Notify(string icon, string text) => Notes.Add((icon, text));
        public void RaiseProvincesChanged(IReadOnlyList<int> provinces) { }
        public void RaiseFogChanged(IReadOnlyList<int> provinces) { }
    }

    static ulong HashWorld(WorldData w)
    {
        var h = new Hash();
        h.Add(w.P); h.Add(w.W); h.Add(w.H);
        foreach (var v in w.Prov) h.Add(v);
        foreach (var v in w.Height) h.Add(BitConverter.SingleToInt32Bits(v));
        foreach (var v in w.BaseColor) h.Add(v);
        foreach (var n in w.PName) h.Add(n);
        foreach (var a in w.Adj) foreach (var q in a) h.Add(q);
        foreach (var f in w.PFert) h.Add(BitConverter.SingleToInt32Bits(f));
        return h.Value;
    }

    static ulong HashState(GameState s)
    {
        var h = new Hash();
        h.Add(s.Tick); h.Add(s.Day256); h.Add(BitConverter.DoubleToInt64Bits(s.Gold)); h.Add(s.EventCount); h.Add(s.ProjectIndex); h.Add(s.QueuePct);
        foreach (var v in s.Owner) h.Add(v);
        foreach (var v in s.Pop) h.Add(v);
        foreach (var v in s.Religion) h.Add(v);
        foreach (var v in s.Mood) h.Add(v);
        foreach (var v in s.Slots) h.Add(v);
        foreach (var l in s.Buildings) { h.Add(l.Count); foreach (var b in l) h.Add((int)b); }
        foreach (var v in s.Ore) h.Add(v);
        foreach (var v in s.Fog) h.Add(v);
        foreach (var v in s.Met) h.Add(v ? 1 : 0);
        foreach (var r in s.Routes) foreach (var q in r) h.Add(q);
        foreach (var c in s.NationCapital) h.Add(c);
        return h.Value;
    }

    struct Hash
    {
        ulong _v;
        public ulong Value => _v;
        public void Add(long x) { if (_v == 0) _v = 1469598103934665603UL; unchecked { _v = (_v ^ (ulong)x) * 1099511628211UL; } }
        public void Add(string t) { foreach (char ch in t) Add(ch); Add(-7); }
    }

    // =====================================================================================================
    async Task LongRun()
    {
        int years = Cli.Int("qa-years", 3000);
        var w = G.World; var s = G.State;
        G.SetPaused(true);
        G.SendScoutAuto(); G.SendScoutAuto();
        var (o0, n0, m0) = Footprint();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double worstBatch = 0;
        int y0 = s.Year;
        int problems = 0;
        string firstProblem = null;
        for (int k = 0; k < years; k += 25)
        {
            var bsw = System.Diagnostics.Stopwatch.StartNew();
            G.RunTicks(25 * Clock.CycleTicks);
            worstBatch = Math.Max(worstBatch, bsw.Elapsed.TotalMilliseconds);
            await Frames(1);
            if (k % 250 == 0)
            {
                string pb = Invariants(w, s);
                if (pb != null) { problems++; firstProblem ??= $"year {s.Year}: {pb}"; }
                if (s.Scouts.Count == 0) { G.SendScoutAuto(); }
                G.SetMode((MapMode)(k / 250 % 5));
                GD.Print($"qa longrun {GameState.YearText(s.Year)} gold={s.Gold:F0} income={s.LastIncome:F1} own={s.Owner.Count(o => o == 0)} unowned land={Enumerable.Range(0, w.P).Count(p => w.PLand[p] == 1 && s.Owner[p] < 0)} events={s.EventCount} notesChildren={Hud.Notes.GetChildCount()} batch25={bsw.Elapsed.TotalMilliseconds:F0}ms");
            }
        }
        var (o1, n1, m1) = Footprint();
        Check($"longrun {years} years: invariants hold", problems == 0, firstProblem);
        Info("longrun", $"{s.Year - y0} years in {sw.ElapsedMilliseconds} ms incl. frames; worst 25-year batch {worstBatch:F0} ms; objects {o0}→{o1} nodes {n0}→{n1} managed {m0:F0}→{m1:F0} MB");
        Check("longrun: no node/object pile-up", n1 - n0 < 50 && o1 - o0 < 1000, $"objects {o0}→{o1} nodes {n0}→{n1}");
        G.SetMode(MapMode.Political);
        await ZoomTo(1);
        G.SetFogEnabled(false);
        await Seconds(.5);
        await Shot("longrun_observer");
        G.SetFogEnabled(true);
        await Seconds(.3);
        await Shot("longrun_fog");
        G.Select(Cap); await Seconds(.4);
        await Shot("longrun_capital_panel");
        Hud.DebugToggleLead(); await Seconds(.4);
        await Shot("longrun_leaderboard");
        Hud.DebugToggleLead();
        G.SetPaused(false);
    }

    static string Invariants(WorldData w, GameState s)
    {
        if (double.IsNaN(s.Gold) || double.IsInfinity(s.Gold)) return $"gold {s.Gold}";
        if (s.Gold < 0) return $"negative gold {s.Gold:F1}";
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] == 1 && (float.IsNaN(s.Pop[p]) || float.IsInfinity(s.Pop[p]) || s.Pop[p] < 0)) return $"pop[{p}]={s.Pop[p]}";
            if (s.Mood[p] > 100) return $"mood[{p}]={s.Mood[p]}";
            if (s.Owner[p] >= s.Nations.Length) return $"owner[{p}]={s.Owner[p]}";
            if (s.Owner[p] >= 0 && w.PLand[p] != 1) return $"sea owned {p}";
            if (s.Buildings[p].Count > s.Slots[p]) return $"buildings {s.Buildings[p].Count} > slots {s.Slots[p]} in {p}";
            if (s.Buildings[p].Distinct().Count() != s.Buildings[p].Count) return $"duplicate building in {p}";
            if ((s.Fog[p] != 0) != s.Explored[p]) return $"fog {s.Fog[p]} vs explored {s.Explored[p]} in {p}";
            if (s.Pop[p] > 1e7) return $"pop explosion {s.Pop[p]} in {p}";
        }
        return null;
    }


    // =====================================================================================================
    async Task Extra()
    {
        var w = G.World; var s = G.State; int cap = Cap;
        var scr = GetViewport().GetVisibleRect().Size;
        await ZoomTo(3);

        // wheel over various UI cards
        async Task WheelOver(string name, Vector2 at)
        {
            int z0 = G.ZoomLevel;
            Motion(at); await Frames(2);
            var hc = GetViewport().GuiGetHoveredControl();
            MouseButtonAt(at, MouseButton.WheelDown, true); MouseButtonAt(at, MouseButton.WheelDown, false);
            await Seconds(.3);
            Check($"wheel over {name} leaves the map zoom alone", G.ZoomLevel == z0, $"zoom {z0}→{G.ZoomLevel}, hovered control {hc?.GetType().Name}");
            if (G.ZoomLevel != z0) await ZoomTo(z0);
        }
        int sea = First(q => w.PLand[q] == 0 && s.Fog[q] != 0);
        G.Select(sea); await Seconds(.4);
        var pr = Hud.Panel.GetGlobalRect();
        await Shot("sea_panel");
        await WheelOver("a short (sea) panel", pr.Position + new Vector2(pr.Size.X / 2, pr.Size.Y - 20));
        G.Select(cap); await Seconds(.3);
        G.Notify("cat", "Проверка колеса мыши над заметкой");
        await Seconds(.4);
        await WheelOver("a notification", Hud.Notes.GetChild<Control>(Hud.Notes.GetChildCount() - 1).GetGlobalRect().GetCenter());
        Hud.DebugToggleLead(); await Seconds(.4);
        var lr = Hud.Lead.GetChild<Control>(0).GetGlobalRect();
        await WheelOver("the leaderboard", lr.GetCenter());
        Hud.DebugToggleLead(); await Frames(2);
        G.ShowToast("Проверка тоста", 5); await Seconds(.3);
        await WheelOver("a toast", Hud.ToastView.GetGlobalRect().GetCenter());
        var mini = Hud.Mini.GetGlobalRect();
        await WheelOver("the minimap button bar", new Vector2(mini.Position.X + 170, mini.End.Y - 20));

        // drag right after an off-centre wheel zoom (glide still running)
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); await Seconds(.5);
        await ZoomTo(3);
        var p0 = new Vector2(scr.X * .25f, scr.Y * .55f);
        Motion(p0); await Frames(2);
        MouseButtonAt(p0, MouseButton.WheelUp, true); MouseButtonAt(p0, MouseButton.WheelUp, false);
        await Frames(1);
        MouseButtonAt(p0, MouseButton.Left, true); await Frames(1);
        var cs = new List<float>();
        for (int i = 1; i <= 30; i++) { Motion(p0 + new Vector2(6 * i, 0), MouseButtonMask.Left); await Frames(1); cs.Add(CamCenterX()); }
        MouseButtonAt(p0 + new Vector2(180, 0), MouseButton.Left, false); await Frames(2);
        float maxJump = 0; for (int i = 1; i < cs.Count; i++) { float d = Math.Abs(cs[i] - cs[i - 1]); if (d > w.W / 2f) d = w.W - d; maxJump = Math.Max(maxJump, d); }
        Check("drag during an off-centre zoom glide has no jump", maxJump < 6, $"max per-frame centre step {maxJump:F1} world px (a 6 px/frame drag at ×4 is 1.5); steps {string.Join(",", cs.Zip(cs.Skip(1), (a, b) => (b - a).ToString("F1")))}");

        // cost of the observer toggle and a full fog change
        await ZoomTo(3);
        foreach (bool on in new[] { false, true, false, true })
        {
            await Frames(3);
            ulong t0 = Time.GetTicksUsec();
            G.SetFogEnabled(on);
            await Frames(1);
            ulong t1 = Time.GetTicksUsec();
            await Frames(1);
            ulong t2 = Time.GetTicksUsec();
            Info($"observer {(on ? "off→on" : "on→off")} frame cost", $"{(t1 - t0) / 1000.0:F1} ms + next frame {(t2 - t1) / 1000.0:F1} ms");
        }
        // cost of a scout step (explored changes: full fog texture upload)
        G.SetSpeed(5); G.SetPaused(false);
        G.SendScoutAuto(); G.SendScoutAuto();
        double worst = 0; int n = 0; double sum = 0;
        var tt = Time.GetTicksMsec();
        ulong prev = Time.GetTicksUsec();
        while (Time.GetTicksMsec() - tt < 4000) { await Frames(1); ulong now = Time.GetTicksUsec(); double d = (now - prev) / 1000.0; prev = now; worst = Math.Max(worst, d); sum += d; n++; }
        Info("frames while 2 scouts walk at speed 5", $"avg {sum / n:F2} ms worst {worst:F1} ms over {n} frames");
        G.SetSpeed(2);

        // capital queue repeats walls and roads forever
        G.SetPaused(true);
        var rec = new Recorder();
        for (int y = 0; y < 400 * Clock.CycleTicks; y++) Simulation.Step(w, s, rec);
        var walls = rec.Notes.Count(x => x.text == Simulation.Projects[1].DoneText);
        var roads = rec.Notes.Count(x => x.text == Simulation.Projects[4].DoneText);
        Info("capital projects over 400 years", $"walls built {walls}×, road paved {roads}× ; all notes: {rec.Notes.Count(x => x.icon == "hammer")} hammer");
        G.SetPaused(false);
    }

    // =====================================================================================================
    /// <summary>A human click (press … ~120 ms … release) on «Авто» while one party walks at speed 5.</summary>
    async Task ClickLost()
    {
        var w = G.World; var s = G.State; int cap = Cap;
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); G.Select(cap); await Seconds(.5);
        G.SetSpeed(Cli.Int("qa-speed", 5)); G.SetPaused(false);
        int trials = 12, lost = 0, hoverLost = 0;
        for (int k = 0; k < trials; k++)
        {
            while (s.Scouts.Count > 1) { s.Scouts.RemoveAt(s.Scouts.Count - 1); G.RaiseScoutsChanged(); }
            if (s.Scouts.Count == 0) { G.SendScoutAuto(); }
            await Frames(3);
            var auto = FindButton(Hud.Panel, "Авто");
            if (auto == null || auto.Disabled) { Info("clicklost", "no enabled «Авто»"); continue; }
            var at = auto.GetGlobalRect().GetCenter();
            Motion(at); await Frames(2);
            MouseButtonAt(at, MouseButton.Left, true);
            var t = Time.GetTicksMsec();
            bool rebuilt = false;
            while (Time.GetTicksMsec() - t < 130) { await Frames(1); if (!IsInstanceValid(auto) || !auto.IsInsideTree()) rebuilt = true; }
            MouseButtonAt(at, MouseButton.Left, false);
            await Frames(3);
            if (s.Scouts.Count < 2) lost++;
            if (rebuilt) hoverLost++;
        }
        Check($"clicks on «Авто» while a party walks at speed {s.Speed} are not lost", lost == 0, $"{lost} of {trials} clicks lost; the button was rebuilt under the cursor during {hoverLost} of them");
        G.SetSpeed(2);
    }

    async Task Cam2()
    {
        var w = G.World; int cap = Cap;
        var scr = GetViewport().GetVisibleRect().Size;
        await ZoomTo(3);
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); await Seconds(.6);
        // minimap click right after a wheel notch (real input)
        var mid = scr / 2;
        Motion(mid); await Frames(1);
        MouseButtonAt(mid, MouseButton.WheelUp, true); MouseButtonAt(mid, MouseButton.WheelUp, false);
        await Frames(2);
        var mini = Hud.Mini.View.GetGlobalRect();
        ClickAt(mini.Position + new Vector2(mini.Size.X * .7f, mini.Size.Y * .5f));
        await Seconds(1);
        Check("wheel then minimap click: integer zoom", Math.Abs(Map.View.Zoom - G.ZoomLevel) < 1e-4, $"view zoom {Map.View.Zoom} label ×{G.ZoomLevel}");
        await Shot("wheel_then_minimap");
        G.RequestZoom(-1); await Seconds(.4);
        await ZoomTo(3);
        // jump, then a wheel notch while flying
        G.JumpCamera(new Vector2(w.PCX[cap], w.PCY[cap])); await Seconds(.6);
        var target = new Vector2(Mathf.PosMod(w.PCX[cap] + 900, w.W), w.PCY[cap]);
        G.JumpCamera(target);
        await Frames(4);
        G.RequestZoom(+1);
        await Seconds(1);
        float dx = Math.Abs(CamCenterX() - target.X); dx = Math.Min(dx, w.W - dx);
        Check("jump then zoom: the jump still arrives", dx < 20, $"centre x {CamCenterX():F0}, target {target.X:F0} (off by {dx:F0} world px)");
    }

    async Task Focus()
    {
        var w = G.World; var s = G.State; int cap = Cap;
        G.Select(cap); await Seconds(.5);
        var pr = Hud.Panel.GetGlobalRect();
        ClickAt(pr.Position + new Vector2(pr.Size.X * .3f, pr.Size.Y * .6f)); await Frames(3);
        var focus = GetViewport().GuiGetFocusOwner();
        Info("focus owner after clicking the panel body", focus?.GetType().Name ?? "none");
        bool p0 = s.Paused;
        PressKey(Key.Space); await Frames(3);
        Check("Space after clicking the panel body toggles pause", s.Paused != p0);
        if (s.Paused) { PressKey(Key.Space); await Frames(2); }
        // arrow keys: map pans; does the focused panel scroll too?
        Hud.Panel.DebugScroll(0); await Frames(2);
        var c0 = G.CameraRect;
        var ev = new InputEventKey { Keycode = Key.Down, PhysicalKeycode = Key.Down, Pressed = true };
        Input.ParseInputEvent(ev);
        await Seconds(.4);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Down, PhysicalKeycode = Key.Down, Pressed = false });
        await Frames(3);
        var sc = Hud.Panel.GetChild(0);
        Info("Down arrow with the panel focused", $"camera y {c0.Position.Y:F0}→{G.CameraRect.Position.Y:F0}, focus {GetViewport().GuiGetFocusOwner()?.GetType().Name ?? "none"}");
        // Leaderboard click then Space
        Hud.DebugToggleLead(); await Seconds(.3);
        var lr = Hud.Lead.GetChild<Control>(0).GetGlobalRect();
        ClickAt(lr.GetCenter()); await Frames(3);
        p0 = s.Paused;
        PressKey(Key.Space); await Frames(3);
        Check("Space after clicking the leaderboard toggles pause", s.Paused != p0, $"focus {GetViewport().GuiGetFocusOwner()?.GetType().Name ?? "none"}");
        if (s.Paused) { PressKey(Key.Space); await Frames(2); }
        Hud.DebugToggleLead();
    }

    async Task Stale()
    {
        var w = G.World; var s = G.State;
        G.SetPaused(true);
        // explore a lot with two auto parties (pure sim, no real time)
        for (int round = 0; round < 3; round++)
        {
            Scouts.Send(w, s, 0, -1, G, out _); Scouts.Send(w, s, 0, -1, G, out _);
            for (int i = 0; i < 1000 && s.Scouts.Count > 0; i++) Scouts.Tick(w, s, G);
        }
        var snap = new Dictionary<int, int>();
        for (int p = 0; p < w.P; p++) if (s.Fog[p] == 1 && w.PLand[p] == 1) snap[p] = s.Owner[p];
        int metBefore = s.Met.Count(m => m);
        var rec = new Recorder();
        for (int y = 0; y < 600 * Clock.CycleTicks; y++) Simulation.Step(w, s, rec);
        var changed = snap.Where(kv => s.Fog[kv.Key] == 1 && s.Owner[kv.Key] != kv.Value).Select(kv => kv.Key).ToList();
        int metAfter = s.Met.Count(m => m);
        var metNotes = rec.Notes.Where(n => n.icon == "affiliate").Select(n => n.text).ToList();
        Info("stale land whose owner changed out of sight in 600 years", $"{changed.Count} of {snap.Count} stale land provinces; nations met {metBefore}→{metAfter}; notes: {string.Join(" | ", metNotes)}");
        G.RaiseProvincesChanged(null); G.RaiseFogChanged(null);
        if (changed.Count > 0)
        {
            int p = changed[0];
            G.JumpCamera(new Vector2(w.PCX[p], w.PCY[p]));
            G.Select(p);
            Hud.FakeMouse = ScreenOf(p) + new Vector2(0, 0);
            await Seconds(.6);
            G.Hover(p);
            Hud.FakeMouse = ScreenOf(p);
            await Seconds(.3);
            await Shot("stale_owner_changed");
            Info("stale example", $"{w.PName[p]}: was {(snap[p] < 0 ? "ничья" : s.Nations[snap[p]].Name)}, now shown as {s.Nations[s.Owner[p]].Name}");
            Hud.FakeMouse = null;
        }
        G.SetPaused(false);
    }

    // =====================================================================================================
    /// <summary>Real-time run at speed 5: frame times, memory, exceptions.</summary>
    async Task Soak()
    {
        double minutes = Cli.Float("qa-minutes", 3);
        var s = G.State; var w = G.World;
        G.SetSpeed(5); G.SetPaused(false);
        int y0 = s.Year;
        var (o0, n0, m0) = Footprint();
        long ws0 = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        var t0 = Time.GetTicksMsec();
        double worst = 0, worstAll = 0; int over20 = 0, over50 = 0, frames = 0; double sum = 0;
        ulong lastReport = t0;
        int modeK = 0;
        while (Time.GetTicksMsec() - t0 < minutes * 60000)
        {
            await Frames(1);
            double d = GetProcessDeltaTime() * 1000;
            frames++; sum += d; worst = Math.Max(worst, d); worstAll = Math.Max(worstAll, d);
            if (d > 20) over20++; if (d > 50) over50++;
            if (s.Scouts.Count < 2 && G.FreeScouts > 0) G.SendScoutAuto();
            ulong now = Time.GetTicksMsec();
            if (now - lastReport >= 10000)
            {
                lastReport = now;
                modeK++;
                if (modeK % 2 == 0) G.SetMode((MapMode)(modeK / 2 % 5));
                if (modeK % 3 == 0) G.SetFogEnabled(!s.FogEnabled);
                if (modeK % 4 == 0) G.RequestZoom(modeK % 8 == 0 ? 1 : -1);
                GD.Print($"qa soak t={(now - t0) / 1000}s {GameState.YearText(s.Year)} fps={Engine.GetFramesPerSecond()} avg={sum / frames:F2}ms worst10s={worst:F1}ms >20ms={over20} >50ms={over50} managed={GC.GetTotalMemory(false) / 1048576.0:F0}MB static={OS.GetStaticMemoryUsage() / 1048576.0:F0}MB ws={System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576}MB nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} objs={Performance.GetMonitor(Performance.Monitor.ObjectCount)} gold={s.Gold:F0} scouts={s.Scouts.Count}");
                worst = 0;
            }
        }
        var (o1, n1, m1) = Footprint();
        long ws1 = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        Info("soak", $"{minutes} min real time: {s.Year - y0} years, {frames} frames, avg {sum / frames:F2} ms, worst {worstAll:F1} ms, >20ms {over20}, >50ms {over50}; objects {o0}→{o1} nodes {n0}→{n1} managed {m0:F0}→{m1:F0} MB, working set {ws0 / 1048576}→{ws1 / 1048576} MB");
        string pb = Invariants(w, s);
        Check("soak: invariants hold", pb == null, pb);
        Check("soak: no pile-up", n1 - n0 < 50 && o1 - o0 < 1000, $"objects {o0}→{o1} nodes {n0}→{n1}");
        await Shot("soak_end");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Unpause until one rules cycle ticks through Game._Process (raises CycleTick), then pause again.</summary>
    async Task RealYear()
    {
        long y = G.State.Tick + Clock.CycleTicks; bool was = G.State.Paused;
        G.SetPaused(false);
        var t = Time.GetTicksMsec();
        while (G.State.Tick < y && Time.GetTicksMsec() - t < 5000) await Frames(1);
        G.SetPaused(was);
    }

    int Cap => G.State.NationCapital[GameState.LocalPlayer];
    int First(Func<int, bool> pred) { for (int p = 0; p < G.World.P; p++) if (pred(p)) return p; return -1; }

    int FarUnexplored(int cap, float maxDist)
    {
        var w = G.World; var s = G.State; int target = -1; float best = 0;
        for (int q = 0; q < w.P; q++)
            if (w.PLand[q] == 1 && s.Fog[q] == 0 && w.SameBody(q, cap))
            {
                float d = Simulation.Distance(w, q, cap);
                if (d < maxDist && d > best) { best = d; target = q; }
            }
        return target;
    }

    /// <summary>RTS orders with real mouse events: a left click on a walking party picks it, a right click on the map
    /// turns it there (a journaled ScoutMove), Esc drops the pick, a right click with nothing picked orders nothing.</summary>
    async Task UnitOrderTests()
    {
        var w = G.World; var s = G.State;
        if (s.Scouts.Count == 0) { Fail("unit orders", "no party walking"); return; }
        G.SetPaused(true);
        G.Select(-1);   // the province panel covers the middle of a small window
        await Frames(2);
        var party = s.Scouts[0];
        int at = Scouts.Current(party), sel = G.Selected;
        G.JumpCamera(new Vector2(w.PCX[at], w.PCY[at])); await Seconds(.7);
        // the figure: search the overlay's hit boxes around the party's province
        var c = ScreenOf(at);
        Vector2? fig = null;
        for (int r = 0; r <= 90 && fig == null; r += 4)
            for (int k = -r; k <= r && fig == null; k += 4)
                foreach (var d in new[] { new Vector2(k, -r), new Vector2(k, r), new Vector2(-r, k), new Vector2(r, k) })
                    if (Map.UnitAt(c + d) == new UnitRef(UnitSel.Scout, party.Id)) { fig = c + d; break; }
        if (fig == null) { Fail("unit orders: the party's figure is clickable", $"none near {c}"); G.SetPaused(false); return; }
        Motion(fig.Value); await Frames(2);
        var over = GetViewport().GuiGetHoveredControl();
        if (over != null) { Info("unit orders", $"the party is under {over.GetPath()}: skipped"); G.SetPaused(false); return; }
        ClickAt(fig.Value); await Frames(3);
        Check("left click on a walking party picks it", G.SelectedUnit == new UnitRef(UnitSel.Scout, party.Id),
            $"{G.SelectedUnit} at {fig.Value}, under the mouse: {(over == null ? "the map" : over.GetPath().ToString())}");
        Check("picking a party keeps the province panel", G.Selected == sel, $"selected {sel} → {G.Selected}");
        // land of the party's continent on the left part of the screen (clear of the panel and the bars), under the
        // clouds or not: a party may walk into the unknown
        var view = GetViewport().GetVisibleRect().Size;
        int dest = -1;
        for (int q = 0; q < w.P && dest < 0; q++)
        {
            if (q == at || w.PLand[q] != 1 || !w.SameBody(q, at)) continue;
            var qp = ScreenOf(q);
            if (qp.X > 60 && qp.X < view.X * .55f && qp.Y > 120 && qp.Y < view.Y - 200 && qp.DistanceTo(fig.Value) > 60) dest = q;
        }
        if (dest < 0) { Fail("unit orders: a target on screen", "no land of the party's continent on screen"); G.DeselectUnit(); G.SetPaused(false); return; }
        var dp = ScreenOf(dest);
        Motion(dp); await Frames(3);
        await Shot("unit_order_preview");
        int j0 = G.Journal.Count;
        MouseButtonAt(dp, MouseButton.Right, true); MouseButtonAt(dp, MouseButton.Right, false); await Frames(3);
        int hit = Map.ProvinceAt(Map.View.ToWorld(dp));
        Check("right click turns the picked party there (journaled)", Scouts.Target(party) == hit && G.Journal.Skip(j0).Any(x => x.Type == CmdType.ScoutMove) && G.SelectedUnit.Any,
            $"target {Scouts.Target(party)} vs {hit}");
        PressKey(Key.Escape); await Frames(3);
        Check("Esc drops the picked unit first, the panel stays", !G.SelectedUnit.Any && G.Selected == sel);
        int j1 = G.Journal.Count;
        MouseButtonAt(dp, MouseButton.Right, true); MouseButtonAt(dp, MouseButton.Right, false); await Frames(3);
        Check("right click with nothing picked orders nothing", G.Journal.Count == j1 && Scouts.Target(party) == hit);
        G.SetPaused(false);
    }

    Vector2 ScreenOf(int p)
    {
        var v = Map.View; var w = G.World;
        float x = v.FirstX(w.PCX[p] + .5f, 0);
        while (x < 0) x += v.WZ;
        return new Vector2(x, v.ScreenY(w.PCY[p] + .5f));
    }

    static Vector2 Center(Control c) => c.GetGlobalRect().GetCenter();

    static IEnumerable<Button> AllButtons(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is Button b && b.IsVisibleInTree()) yield return b;
            foreach (var d in AllButtons(c)) yield return d;
        }
    }

    static Button FindButton(Node n, string contains) =>
        AllButtons(n).FirstOrDefault(b => (b is TextButton tb ? tb.Caption : b.Text)?.Contains(contains) == true);

    static ClickPanel FindClickPanel(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is ClickPanel cp && cp.IsVisibleInTree()) return cp;
            var d = FindClickPanel(c); if (d != null) return d;
        }
        return null;
    }

    bool ErrorSince(int t0, string contains) => _toasts.Skip(t0).Any(t => t.kind == ToastKind.Error && t.text.Contains(contains));
    string ToastsSince(int t0) => string.Join(" | ", _toasts.Skip(t0).Select(t => $"{t.kind}:{t.text}"));

    void Check(string name, bool ok, string detail = null) { if (ok) Pass(name, detail); else Fail(name, detail); }
    void Pass(string name, string detail = null) { _pass++; GD.Print($"qa PASS {name}{(detail != null ? "  · " + detail : "")}"); }
    void Fail(string name, string detail = null) { _fail++; GD.PrintErr($"qa FAIL {name}{(detail != null ? "  · " + detail : "")}"); }
    void Info(string name, string detail) => GD.Print($"qa INFO {name}  · {detail}");

    static void PressKey(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    static void Motion(Vector2 at, MouseButtonMask held = 0) =>
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = held });

    static void MouseButtonAt(Vector2 at, MouseButton b, bool down) =>
        Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = b, Pressed = down, ButtonMask = down && b == MouseButton.Left ? MouseButtonMask.Left : 0 });

    static void ClickAt(Vector2 at)
    {
        Motion(at);
        MouseButtonAt(at, MouseButton.Left, true);
        MouseButtonAt(at, MouseButton.Left, false);
    }

    async Task ZoomTo(int level)
    {
        for (int i = 0; i < 12 && G.ZoomLevel != level; i++) { G.RequestZoom(G.ZoomLevel < level ? 1 : -1); await Seconds(.2); }
        await Seconds(.25);
    }

    static (int objects, int nodes, double managedMb) Footprint()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return ((int)Performance.GetMonitor(Performance.Monitor.ObjectCount), (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), GC.GetTotalMemory(true) / 1048576.0);
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
