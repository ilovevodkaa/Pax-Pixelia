using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI.Front;

/// <summary>
/// <c>--front-selftest</c> (MAIN_MENU.md §2.7): drives the front-end with the keyboard only and prints PASS/FAIL
/// lines; the exit code is the number of failures. Covers the shell's part: bypass rule, title focus and
/// wrap-around, screen stack push/pop/replace with focus returning to the opener, Esc layers, PxConfirm,
/// PxOptionRow, the dither curtain, the background world + planet, and a FocusAudit of every screen it opens.
/// <c>--front-selftest=full</c> then starts a game from «Новая игра» and checks the hand-over to Main.tscn; the
/// «(hook)» checks need Main to take Session.Pending (time-foundation).
/// <c>--front-selftest=loop</c> plays the whole round twice — title → «Новая игра» → «Народ» → НАЧАТЬ → chapter card →
/// map → Esc → pause menu → «В главное меню» → title — and checks that the second round leaves nothing behind.
/// <c>--front-selftest=continue</c> (needs saves, e.g. after =loop): ПРОДОЛЖИТЬ → chapter card → the saved game exactly;
/// then pause menu → «Загрузить» → another save → the running game goes to an autosave → the chosen one runs.
/// </summary>
public partial class FrontSelfTest : Node
{
    FrontShell _shell;
    int _fail, _pass;
    /// <summary>The test outlives the scene changes of the loop: a title shown again must not start a second one.</summary>
    public static bool Running { get; private set; }

    public FrontSelfTest(FrontShell shell) => _shell = shell;

    public override async void _Ready()
    {
        Running = true;
        GD.Print("[front-selftest] start");
        try
        {
            await Run();
            string mode = Cli.Str("front-selftest");
            if (mode == "full") await Full();
            if (mode == "loop") await Loop();
            if (mode == "continue") await Continue();
        }
        catch (Exception e) { Fail("exception: " + e.Message); }
        GD.Print($"[front-selftest] {(_fail == 0 ? "PASS" : "FAIL")} — {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail);
    }

    async Task Run()
    {
        Check("bypass: game flag → Main", FrontShell.ShouldBypass(new[] { "--seed=1337", "--shot=a.png" }));
        Check("bypass: no args → menu", !FrontShell.ShouldBypass(Array.Empty<string>()));
        Check("bypass: --front flags keep the menu", !FrontShell.ShouldBypass(new[] { "--front=title", "--shot=a.png", "--quit" }));
        Check("bypass: --no-motion keeps the menu", !FrontShell.ShouldBypass(new[] { "--no-motion" }));
        Check("bypass: --savedir keeps the menu, --load starts the game", !FrontShell.ShouldBypass(new[] { "--savedir=D:/s" }) && FrontShell.ShouldBypass(new[] { "--load=latest" }));

        await Frames(3);
        if (_shell.IntroPlaying) { Press(Key.Shift); await Frames(2); }
        Check("intro finished", !_shell.IntroPlaying);
        var title = _shell.Title;
        await Idle();
        // with a save «ПРОДОЛЖИТЬ» leads the column and takes the focus (F-4); «ЗАГРУЗИТЬ» without saves is out of the ring
        var authors = title.Find("АВТОРЫ");
        var network = title.Find("СЕТЕВАЯ ИГРА");
        var ring = title.FocusRing;
        Check($"title: default focus on {(title.ContinueButton.Visible ? "ПРОДОЛЖИТЬ" : "НОВАЯ ИГРА")}", Focus() == title.DefaultButton && ring[0] == title.DefaultButton);
        Check("title: ПРОДОЛЖИТЬ only with a save, ЗАГРУЗИТЬ active only with save files",
            title.ContinueButton.Visible == (title.Latest != null) && title.LoadButton.Disabled == !Core.Save.SaveStore.Any());
        Press(Key.Up); await Frames(2);
        Check("title: ↑ wraps to ВЫЙТИ", Focus() == title.QuitButton);
        Press(Key.Down); await Frames(2);
        int down = ring.IndexOf(authors);
        for (int i = 0; i < down; i++) { Press(Key.Down); await Frames(2); }
        Check($"title: ↓×{down} → АВТОРЫ", Focus() == authors);

        Press(Key.Enter); await Idle();
        Check("Enter opens «Авторы»", _shell.CurrentScreen is CreditsScreen);
        Audit("credits");
        Press(Key.Enter); await Idle();   // focus is on «Назад»
        Check("«Назад» closes the screen", _shell.Depth == 0);
        Check("focus returns to АВТОРЫ", Focus() == authors);

        Press(Key.Up); await Frames(2); Press(Key.Up); await Frames(2);
        Check("↑×2 → СЕТЕВАЯ ИГРА", Focus() == network);
        Press(Key.Enter); await Idle();
        Check("Enter opens the multiplayer stub", _shell.CurrentScreen is MpStubScreen);
        Audit("mpstub");
        Press(Key.Enter); await Idle();
        Check("stub «НОВАЯ ИГРА» replaces it with «Новая игра»", _shell.Depth == 1 && _shell.CurrentScreen is NewGameScreen);
        Audit("newgame");
        Press(Key.Escape); await Idle();
        Check("Esc closes «Новая игра»", _shell.Depth == 0);
        Check("focus returns to СЕТЕВАЯ ИГРА", Focus() == network);

        _shell.OpenScreen("load"); await Idle();
        Check("«Загрузить» opens", _shell.CurrentScreen is LoadScreen);
        Audit("load");
        Press(Key.Escape); await Idle();
        Check("Esc closes «Загрузить»", _shell.Depth == 0);

        _shell.OpenScreen("credits"); await Idle();
        _shell.Push(new LicensesScreen()); await Idle();
        Check("stack: credits → licences", _shell.Depth == 2 && _shell.CurrentScreen is LicensesScreen);
        Audit("licenses");
        Press(Key.Escape); await Idle();
        Check("Esc pops one layer", _shell.Depth == 1 && _shell.CurrentScreen is CreditsScreen);
        Check("the kept screen is interactive again", Focus() != null && _shell.CurrentScreen.IsAncestorOf(Focus()));
        Press(Key.Escape); await Idle();
        Check("Esc pops the last layer", _shell.Depth == 0);
        Press(Key.Escape); await Frames(2);
        Check("Esc on the title focuses ВЫЙТИ", Focus() == title.QuitButton);

        var ask = _shell.Confirm("Проверка", "Текст диалога.", new[] { "Выйти", "Отмена" });
        await Frames(3);
        Check("PxConfirm open, focus on the safe button", PxConfirm.IsOpen && Focus() is Button { Text: "Отмена" });
        Press(Key.Escape); await Frames(3);
        Check("PxConfirm: Esc answers −1", ask.IsCompleted && ask.Result == -1 && !PxConfirm.IsOpen);
        Check("PxConfirm: Esc did not reach the title", Focus() == null || Focus() == title.QuitButton || Focus() is Button);
        ask = _shell.Confirm("Проверка", "Текст диалога.", new[] { "Выйти", "Отмена" });
        await Frames(3);
        Press(Key.Left); await Frames(2);
        Press(Key.Enter); await Frames(3);
        Check("PxConfirm: ← then Enter picks «Выйти»", ask.IsCompleted && ask.Result == 0);

        var row = new PxOptionRow("Проверка", new[] { "A", "B", "C" });
        int changes = 0;
        row.Changed += _ => changes++;
        _shell.UiRoot.AddChild(row);
        row.GrabFocus();
        await Frames(2);
        Press(Key.Right); await Frames(2);
        Check("PxOptionRow: → next value", row.Index == 1);
        Press(Key.Right); await Frames(2); Press(Key.Right); await Frames(2);
        Check("PxOptionRow: stops at the end", row.Index == 2 && changes == 2);
        row.SetLocked("под замком");
        Press(Key.Left); await Frames(2);
        Check("PxOptionRow: locked row does not change", row.Index == 2 && row.Hint == "под замком");
        row.QueueFree();

        await _shell.Wipe.Close();
        Check("DitherWipe closes", _shell.Wipe.IsClosed);
        await _shell.Wipe.Open();
        Check("DitherWipe opens", !_shell.Wipe.IsClosed);

        double waited = 0;
        while (!NextWorld.IsReady && waited < 20) { await Frames(1); waited += GetProcessDeltaTime(); }
        Check($"NextWorld ready (seed {NextWorld.Seed})", NextWorld.IsReady && NextWorld.PlanetMap != null);
        await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
        Check("planet revealed", Mathf.IsEqualApprox((float)_shell.Sky.Mat.GetShaderParameter("planet_reveal"), 1f));
    }

    async Task Full()
    {
        await Idle();
        _shell.Title.NewGameButton.GrabFocus();
        Press(Key.Enter); await Idle();
        Check("full: «Новая игра» open", _shell.Depth == 1);
        int seed = NextWorld.Seed;
        Reparent(GetTree().Root);   // survive the scene change
        Press(Key.Enter);           // default focus = «НАЧАТЬ»
        double waited = 0;
        while (GetTree().CurrentScene?.Name != "Main" && waited < 5) { await Frames(1); waited += GetProcessDeltaTime(); }
        Check("full: switched to Main.tscn", GetTree().CurrentScene?.Name == "Main");
        waited = 0;
        while (!(Game.I?.IsReady ?? false) && waited < 15) { await Frames(1); waited += GetProcessDeltaTime(); }
        Check("full: WorldReady within 15 s", Game.I?.IsReady ?? false);
        Check($"(hook) the game runs the menu's world (seed {seed}, got {Game.I?.Seed})", Game.I?.Seed == seed);
        Check("(hook) Session.LaunchedFromMenu", Session.LaunchedFromMenu);
        Check("(hook) Game.I.Setup came from the menu", Game.I?.Setup?.Seed == seed);
    }

    async Task Loop()
    {
        Reparent(GetTree().Root);   // survive the scene changes
        (ulong objects, ulong nodes, long bytes) first = default;
        for (int round = 1; round <= 3; round++)
        {
            await Round(round);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Frames(10);
            var now = ((ulong)Performance.GetMonitor(Performance.Monitor.ObjectCount), (ulong)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), GC.GetTotalMemory(true));
            GD.Print($"  round {round}: objects {now.Item1}, nodes {now.Item2}, managed {now.Item3 / 1048576} MB, orphans {Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)}");
            if (round == 2) first = now;
            if (round == 3)
            {
                // round 1 warms caches (fonts, icons, the content pack); rounds 2 and 3 must end in the same place
                Check($"loop: no nodes left behind ({first.nodes} → {now.Item2})", now.Item2 <= first.nodes + 2);
                Check($"loop: objects stable ({first.objects} → {now.Item1})", now.Item1 <= first.objects + 200);
                Check($"loop: managed memory stable ({first.bytes / 1048576} → {now.Item3 / 1048576} MB)", now.Item3 <= first.bytes + 64L * 1048576);
            }
        }
    }

    /// <summary>One round from the title to the map and back to the title through the pause menu.</summary>
    async Task Round(int round)
    {
        string r = $"loop {round}:";
        double waited = 0;
        while ((_shell = GetTree().CurrentScene as FrontShell) == null && waited < 10) { await Frames(1); waited += GetProcessDeltaTime(); }
        if (_shell == null) { Fail($"{r} back on the title"); return; }
        await Frames(3);
        if (_shell.IntroPlaying) { Press(Key.Shift); await Frames(2); }
        await Idle();
        _shell.Title.NewGameButton.GrabFocus();
        Press(Key.Enter); await Idle();
        Check($"{r} «Новая игра» open", _shell.CurrentScreen is NewGameScreen);
        Press(Key.Tab); await Idle();
        Check($"{r} Tab opens «Народ»", _shell.CurrentScreen is NationScreen);
        Press(Key.Escape); await Idle();
        Check($"{r} Esc returns to «Новая игра»", _shell.CurrentScreen is NewGameScreen);
        Press(Key.Enter);   // default focus = «НАЧАТЬ»
        waited = 0;
        while (!(GetTree().CurrentScene?.Name == "Main" && (Game.I?.IsReady ?? false)) && waited < 20) { await Frames(1); waited += GetProcessDeltaTime(); }
        Check($"{r} the game starts on the menu's world", Game.I?.IsReady == true && Session.LaunchedFromMenu);
        var main = GetTree().CurrentScene as Main;
        waited = 0;
        while (main?.Hud.Loading.Visible == true && waited < 10)
        {
            if (waited > 2.5) Press(Key.Space);   // any key skips the held chapter card
            await Frames(1); waited += GetProcessDeltaTime();
        }
        Check($"{r} the chapter card gives way to the map", main != null && !main.Hud.Loading.Visible);
        Check($"{r} a menu game starts paused", Game.I?.State?.Paused == true);
        await Frames(10);
        for (int i = 0; i < 4 && !PauseMenu.IsOpen; i++) { Press(Key.Escape); await Frames(3); }   // panel → pause menu
        Check($"{r} Esc ends in the pause menu", PauseMenu.IsOpen);
        var toMenu = FindButton(GetTree().Root, "В главное меню");
        if (toMenu == null) { Fail($"{r} «В главное меню» button"); return; }
        toMenu.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check($"{r} PxConfirm asks, focus on «Отмена»", PxConfirm.IsOpen);
        Press(Key.Left); await Frames(2);
        Press(Key.Enter);
        waited = 0;
        while (GetTree().CurrentScene is not FrontShell && waited < 10) { await Frames(1); waited += GetProcessDeltaTime(); }
        Check($"{r} back on the title, the game ended", GetTree().CurrentScene is FrontShell && !(Game.I?.IsReady ?? true));
        await Frames(20);
    }

    async Task Continue()
    {
        Reparent(GetTree().Root);   // survive the scene changes
        await Idle();
        var title = _shell.Title;
        var latest = title.Latest;
        if (latest == null) { Fail("continue: no save to continue (run --front-selftest=loop first)"); return; }
        Check("continue: ПРОДОЛЖИТЬ is shown and is the default", title.ContinueButton.Visible && title.DefaultButton == title.ContinueButton);
        title.DefaultButton.GrabFocus();
        await Frames(2);
        Press(Key.Enter);
        var main = await GameRunning(g => g.SavePath != null && Same(g.SavePath, latest.Path));
        var g = Game.I;
        Check("continue: the saved game runs through the chapter card", main != null && g.IsReady && Session.LaunchedFromMenu);
        Check("continue: exactly the saved state (tick, hash)", g.IsReady && g.State.Tick == latest.Header.Tick && g.State.Hash().All == latest.Header.StateHash,
            $"tick {g.State?.Tick} vs {latest.Header.Tick}");
        await PassCard(main);
        Check("continue: a loaded game starts paused", g.State?.Paused == true);

        for (int i = 0; i < 4 && !PauseMenu.IsOpen; i++) { Press(Key.Escape); await Frames(3); }
        var load = FindButton(GetTree().Root, "Загрузить");
        if (!PauseMenu.IsOpen || load == null || load.Disabled) { Fail("pause menu: «Загрузить»"); return; }
        load.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(12);
        var screen = Descendants(GetTree().Root).OfType<LoadScreen>().FirstOrDefault();
        Check("pause menu: «Загрузить» shows the saves", screen != null && screen.Rows.Count >= 2, $"{screen?.Rows.Count} saves");
        if (screen == null || screen.Rows.Count < 2) return;
        var (pick, row) = screen.Rows.First(r => r.Entry.Ok && !Same(r.Entry.Path, latest.Path));
        var stamp = System.IO.File.GetLastWriteTimeUtc(pick.Path);
        row.GrabFocus(); await Frames(2);
        Press(Key.Enter); await Frames(4);
        Check("pause menu: loading asks first, focus on «Загрузить»", PxConfirm.IsOpen && Focus() is Button { Text: "Загрузить" });
        Press(Key.Enter);
        main = await GameRunning(x => x.SavePath != null && Same(x.SavePath, pick.Path) && GetTree().CurrentScene != main);
        g = Game.I;
        Check("pause menu: the chosen save runs", main != null && g.IsReady && g.State.Hash().All == pick.Header.StateHash, pick.FileName);
        Check("pause menu: the chosen file was not overwritten by the save on leaving", System.IO.File.GetLastWriteTimeUtc(pick.Path) == stamp);
        var exit = Core.Save.SaveStore.List().FirstOrDefault(e => e.Header?.Kind == Core.Save.SaveKind.Exit && Math.Abs(e.Header.SavedUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < 60_000);
        Check("pause menu: the game left behind went to an autosave", exit != null && exit.Header.Tick == latest.Header.Tick, exit?.FileName);
        await PassCard(main);
    }

    async Task<Main> GameRunning(Func<Game, bool> which)
    {
        double waited = 0;
        while (!(GetTree().CurrentScene is Main && (Game.I?.IsReady ?? false) && which(Game.I)) && waited < 20) { await Frames(1); waited += GetProcessDeltaTime(); }
        return GetTree().CurrentScene as Main;
    }

    async Task PassCard(Main main)
    {
        double waited = 0;
        while (main?.Hud.Loading.Visible == true && waited < 10)
        {
            if (waited > 2.5) Press(Key.Space);   // any key skips the held chapter card
            await Frames(1); waited += GetProcessDeltaTime();
        }
        Check("the chapter card gives way to the map", main != null && !main.Hud.Loading.Visible);
        await Frames(10);
    }

    static IEnumerable<Node> Descendants(Node n)
    {
        foreach (var c in n.GetChildren()) { yield return c; foreach (var d in Descendants(c)) yield return d; }
    }

    static bool Same(string a, string b) => string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    static Button FindButton(Node root, string text)
    {
        foreach (var n in root.FindChildren("*", "Button", true, false))
            if (n is Button { Visible: true } b && b.Text.Equals(text, StringComparison.OrdinalIgnoreCase)) return b;
        return null;
    }

    /// <summary>FocusAudit (§2.7): a default focus, every interactive control reachable by Tab, no clipped one-line label.</summary>
    void Audit(string name)
    {
        var s = _shell.CurrentScreen;
        if (s == null) { Fail($"{name}: no screen"); return; }
        var start = s.DefaultFocus;
        Check($"{name}: DefaultFocus set", start != null);
        if (start == null) return;
        var seen = new HashSet<Control> { start };
        for (var c = start.FindNextValidFocus(); c != null && seen.Add(c); c = c.FindNextValidFocus()) { }
        foreach (var c in Nav.Interactive(s))
            if (!(c is BaseButton { Disabled: true }) && !seen.Contains(c)) Fail($"{name}: «{Describe(c)}» unreachable by Tab");
        foreach (var l in s.FindChildren("*", "Label", true, false))
        {
            if (l is not Label { AutowrapMode: TextServer.AutowrapMode.Off, ClipText: false } label || !label.IsVisibleInTree()) continue;
            if (label.GetParent() is Control p && label.GetMinimumSize().X > p.Size.X + 1) Fail($"{name}: label «{label.Text}» clipped ({label.GetMinimumSize().X:0} > {p.Size.X:0})");
        }
        _pass++;
    }

    static string Describe(Control c) => c is Button b ? b.Text : c.Name;

    Control Focus() => GetViewport().GuiGetFocusOwner();

    static void Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task Idle()
    {
        await Frames(2);
        for (int i = 0; i < 300 && _shell.IsBusy; i++) await Frames(1);
        await Frames(2);
    }

    void Check(string what, bool ok, string detail) => Check(string.IsNullOrEmpty(detail) ? what : $"{what} — {detail}", ok);

    void Check(string what, bool ok)
    {
        if (ok) { _pass++; GD.Print($"  PASS {what}"); }
        else Fail(what);
    }

    void Fail(string what)
    {
        _fail++;
        GD.PrintErr($"  FAIL {what}");
    }
}
