using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.UI.Front;

/// <summary>
/// Root of Front.tscn (MAIN_MENU.md §2.1): the «Горизонт эпох» backdrop, the title menu, a stack of screens in
/// an overlay panel (port of Mr. President's main.gd), keyboard rules (§2.5), the dither curtain and the hand-over
/// to Main.tscn. Any game CLI flag without a --front* flag skips the menu straight to Main.tscn (§2.7), so every
/// existing test and screenshot command keeps working.
/// </summary>
public partial class FrontShell : Control
{
    public const string MainScene = "res://scenes/Main.tscn";
    public const string FrontScene = "res://scenes/Front.tscn";
    const float OverlayAlpha = .78f;
    static readonly (string, string)[] TitleHints = { ("Up Down", "выбор"), ("Enter", "принять") };

    public static FrontShell Current { get; private set; }
    /// <summary>UI scale for a window size; the settings module may replace it (Settings.UiScale).</summary>
    public static Func<Vector2, float> UiScale = AutoUiScale;

    sealed record Entry(FrontScreen Screen, PanelContainer Panel, Control Opener);

    readonly List<Entry> _stack = new();
    SkyBackdrop _sky;
    CanvasLayer _uiLayer;
    Control _uiRoot, _overlay;
    CenterContainer _center, _hintHost;
    TitleMenu _title;
    DitherWipe _wipe;
    Tween _introTween;
    bool _busy, _bypassed, _leaving;
    int _savedMaxFps = -1;
    Action _unsubscribeSettings;

    public SkyBackdrop Sky => _sky;
    public TitleMenu Title => _title;
    public DitherWipe Wipe => _wipe;
    /// <summary>Scaled UI root: parent for PxConfirm and other overlays of the front-end.</summary>
    public Control UiRoot => _uiRoot;
    public FrontScreen CurrentScreen => _stack.Count > 0 ? _stack[^1].Screen : null;
    public int Depth => _stack.Count;
    /// <summary>A transition is running (pushes/pops are ignored meanwhile).</summary>
    public bool IsBusy => _busy;
    public bool IntroPlaying => _introTween != null && _introTween.IsValid() && _introTween.IsRunning();

    /// <summary>§2.7: skip the menu when a user argument other than --front*/--no-motion is given, unless a --front* flag keeps us here.</summary>
    public static bool ShouldBypass(IEnumerable<string> args)
    {
        bool front = false, other = false;
        foreach (var a in args)
        {
            if (a.StartsWith("--front")) front = true;
            else if (a != "--no-motion") other = true;
        }
        return other && !front;
    }

    /// <summary>The settings module's scale when present, else by window height (100 / 150 / 200 %).</summary>
    public static float AutoUiScale(Vector2 window) => SettingsBridge.UiScale ?? (window.Y < 1300 ? 1f : window.Y < 2000 ? 1.5f : 2f);

    public override void _Ready()
    {
        if (ShouldBypass(OS.GetCmdlineUserArgs()))
        {
            _bypassed = true;
            CallDeferred(MethodName.GoToMain);
            return;
        }
        Current = this;
        FrontClock.Reduced = SettingsBridge.ReducedMotion ?? false;
        FrontClock.ApplyCli();
        _unsubscribeSettings = SettingsBridge.OnChanged(OnSettingChanged);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _sky = new SkyBackdrop { Name = "SkyBackdrop" };
        AddChild(_sky);
        _uiLayer = new CanvasLayer { Name = "UiLayer", Layer = 10 };
        AddChild(_uiLayer);
        _uiRoot = new Control { Name = "UiRoot", Theme = PixelTheme.Build(), MouseFilter = MouseFilterEnum.Ignore };
        _uiLayer.AddChild(_uiRoot);
        _title = new TitleMenu(this) { Name = "Title" };
        _uiRoot.AddChild(_title);
        _title.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _hintHost = new CenterContainer { Name = "Hints", MouseFilter = MouseFilterEnum.Ignore, AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetTop = -44, OffsetBottom = -12 };
        _uiRoot.AddChild(_hintHost);
        ShowHints(TitleHints);

        var front = Cli.Str("front");
        bool direct = front != null && front != "1";
        bool intro = !direct && !FrontClock.Reduced;
        _wipe = new DitherWipe(startClosed: intro || Session.ReturnedFromGame) { Name = "Wipe" };
        AddChild(_wipe);
        GetViewport().SizeChanged += OnResized;
        OnResized();
        if (Cli.Has("noinput")) AddChild(new InputShield { Name = "InputShield" });

        StartNextWorld(direct || !intro);
        bool returning = Session.ReturnedFromGame;
        Session.ReturnedFromGame = false;
        if (direct) OpenDirect(front);
        else if (intro) PlayIntro(returning);
        else { _ = _wipe.Open(); _title.NewGameButton.GrabFocus(); }

        if (Cli.Has("front-selftest") && !FrontSelfTest.Running) AddChild(new FrontSelfTest(this) { Name = "FrontSelfTest" });
        if (Cli.Str("shot") is { } shot) TakeShot(shot);
    }

    public override void _ExitTree()
    {
        if (_bypassed) return;
        GetViewport().SizeChanged -= OnResized;
        _unsubscribeSettings?.Invoke();
        // whoever launched the game took the world through Session.Pending; the menu makes a fresh one next time
        NextWorld.Release();
        if (_savedMaxFps >= 0) Engine.MaxFps = _savedMaxFps;
        foreach (var e in _stack) if (!e.Panel.IsInsideTree()) e.Panel.Free();   // screens hidden under the top one
        _stack.Clear();
        if (Current == this) Current = null;
    }

    public override void _Process(double delta)
    {
        if (!_bypassed) FrontClock.Tick(delta);
    }

    public override void _Notification(int what)
    {
        if (_bypassed) return;
        // an unfocused menu does not need 60+ fps of sky
        if (what == NotificationApplicationFocusOut && _savedMaxFps < 0) { _savedMaxFps = Engine.MaxFps; Engine.MaxFps = 15; }
        else if (what == NotificationApplicationFocusIn && _savedMaxFps >= 0) { Engine.MaxFps = _savedMaxFps; _savedMaxFps = -1; }
    }

    public override void _Input(InputEvent e)
    {
        // any key or click during the intro finishes it (and is eaten, so an invisible button is not pressed)
        if (!IntroPlaying || e is not (InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true })) return;
        SkipIntro();
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_bypassed || _leaving) return;
        if (e.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            if (_busy || PxConfirm.IsOpen) return;
            if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) { GetViewport().GuiReleaseFocus(); return; }
            if (_stack.Count > 0) CurrentScreen.GoBack();
            else _title.QuitButton.GrabFocus();
            return;
        }
        bool nav = e.IsActionPressed("ui_down") || e.IsActionPressed("ui_up") || e.IsActionPressed("ui_focus_next");
        if (nav && GetViewport().GuiGetFocusOwner() == null && !_busy)
        {
            var target = _stack.Count > 0 ? CurrentScreen.DefaultFocus ?? Nav.FirstFocusable(_stack[^1].Panel) : _title.NewGameButton;
            target?.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---- screens ----------------------------------------------------------------------------------------

    /// <summary>Screen by id: newgame, nation, settings, credits, licenses, mpstub.</summary>
    public static FrontScreen CreateScreen(string id) => id switch
    {
        "newgame" => new NewGameScreen(),
        "nation" => new NationScreen(),
        "settings" => new SettingsScreen(),
        "credits" => new CreditsScreen(),
        "licenses" => new LicensesScreen(),
        "mpstub" => new MpStubScreen(),
        _ => null,
    };

    public void OpenScreen(string id, string section = null) => Push(CreateScreen(id), section);

    public async void Push(FrontScreen screen, string section = null)
    {
        if (_busy || screen == null) return;
        _busy = true;
        var opener = GetViewport().GuiGetFocusOwner();
        if (_overlay == null) CreateOverlay();
        else if (_stack.Count > 0) await HidePanel(_stack[^1].Panel, keep: true);
        PixelKit.Sfx?.Invoke("open", 1f);
        var panel = BuildPanel(screen, _stack.Select(e => e.Screen).Append(screen));
        _stack.Add(new Entry(screen, panel, opener));
        if (section != null) screen.SelectSection(section);
        await ShowPanel(panel);
        Shown(screen);
    }

    public async void Pop()
    {
        if (_busy || _stack.Count == 0) return;
        _busy = true;
        PixelKit.Sfx?.Invoke("close", 1f);
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        await HidePanel(top.Panel, keep: false);
        if (_stack.Count > 0)
        {
            var under = _stack[^1];
            _center.AddChild(under.Panel);
            await ShowPanel(under.Panel);
            _busy = false;
            ShowHints(under.Screen.Hints);
            Refocus(top.Opener, under.Screen.DefaultFocus);
            return;
        }
        await CloseOverlay();
        _busy = false;
        ShowHints(TitleHints);
        Refocus(top.Opener, _title.NewGameButton);
    }

    /// <summary>Swap the top screen for another one (e.g. the multiplayer stub → «Новая игра»).</summary>
    public async void Replace(FrontScreen screen)
    {
        if (_busy || screen == null) return;
        if (_stack.Count == 0) { Push(screen); return; }
        _busy = true;
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        await HidePanel(top.Panel, keep: false);
        PixelKit.Sfx?.Invoke("open", 1f);
        var panel = BuildPanel(screen, _stack.Select(e => e.Screen).Append(screen));
        _stack.Add(new Entry(screen, panel, top.Opener));
        await ShowPanel(panel);
        Shown(screen);
    }

    /// <summary>Close every screen and return to the title.</summary>
    public async void CloseAll()
    {
        if (_busy || _stack.Count == 0) return;
        _busy = true;
        PixelKit.Sfx?.Invoke("close", 1f);
        var bottomOpener = _stack[0].Opener;
        await HidePanel(_stack[^1].Panel, keep: false);
        foreach (var e in _stack) if (IsInstanceValid(e.Panel) && !e.Panel.IsQueuedForDeletion()) e.Panel.QueueFree();
        _stack.Clear();
        await CloseOverlay();
        _busy = false;
        ShowHints(TitleHints);
        Refocus(bottomOpener, _title.NewGameButton);
    }

    /// <summary>
    /// Hand the game over to Main.tscn: Session.Pending = (setup, world), dither curtain forward, scene change.
    /// Without an explicit world the ready <see cref="NextWorld"/> is reused when its seed matches.
    /// </summary>
    public async void StartGame(GameSetup setup, WorldData world = null)
    {
        if (_leaving) return;
        _leaving = _busy = true;
        PixelKit.Sfx?.Invoke("confirm", 1f);
        world ??= NextWorld.IsReady && NextWorld.Seed == setup.Seed ? NextWorld.World : null;
        Session.Pending = new PendingGame(setup, world);
        NextWorld.Release();   // the game owns the world now; the menu will make a fresh one next time
        await _wipe.Close(1);
        GetTree().ChangeSceneToFile(MainScene);
    }

    public async void Quit()
    {
        if (_leaving) return;
        _leaving = _busy = true;
        await _wipe.Close();
        GetTree().Quit();
    }

    /// <summary>«Меньше анимации»: the sky freezes on one frame, the title stops shimmering, panels appear at once.</summary>
    public void SetReducedMotion(bool on)
    {
        FrontClock.Reduced = on;
        _sky.Motion = !on;
        _title.Shimmer = !on;
    }

    /// <summary>The shared dialog on top of the front-end UI (see PxConfirm.Ask).</summary>
    public Task<int> Confirm(string title, string text, string[] buttons, int countdown = 0, int focus = -1, int danger = 0) =>
        PxConfirm.Ask(_uiRoot, title, text, buttons, countdown, focus, danger);

    /// <summary>Screen panel: dark glass, 2 px AccentDark frame with a 3 px Accent top edge, hard shadow.</summary>
    public static PanelContainer MakePanel(float padX = 34, float padY = 24)
    {
        var p = new PanelContainer();
        var style = PixelKit.Padded(PixelKit.Framed(new Color(PixelKit.Panel, .94f), PixelKit.AccentDark), padX, padY);
        style.ContentMarginTop = padY + 3;
        p.AddThemeStyleboxOverride("panel", style);
        p.Draw += () => p.DrawRect(new Rect2(0, 0, p.Size.X, 3), PixelKit.Accent);
        return p;
    }

    // ---- internals ------------------------------------------------------------------------------------------

    void GoToMain() => GetTree().ChangeSceneToFile(MainScene);

    /// <summary>"section/key" of one setting, "ui/*" after a section reset, "*" after «Не сохранять» reverted everything.</summary>
    void OnSettingChanged(string key)
    {
        if (key == "*" || key.StartsWith("ui/"))
        {
            OnResized();
            if (!Cli.Has("no-motion")) SetReducedMotion(SettingsBridge.ReducedMotion ?? FrontClock.Reduced);
        }
    }

    void OnResized()
    {
        var size = GetViewportRect().Size;
        float scale = UiScale(size);
        _uiLayer.Scale = Vector2.One * scale;
        _uiRoot.Position = Vector2.Zero;
        _uiRoot.Size = size / scale;
        _wipe.Cell = Mathf.Max(2, (int)(size.Y / 225));
        foreach (var e in _stack) e.Panel.CustomMinimumSize = new Vector2(PanelWidth(e.Screen), 0);
    }

    float PanelWidth(FrontScreen s) => Mathf.Min(s.PanelWidth, _uiRoot.Size.X - 48);

    void StartNextWorld(bool now)
    {
        int seed = Cli.Has("front-seed") ? Cli.Int("front-seed", Main.DefaultSeed) : FrontClock.Frozen ? Main.DefaultSeed : NextWorld.RandomSeed();
        if (now) NextWorld.EnsureStarted(seed);
        else GetTree().CreateTimer(.45).Timeout += () => NextWorld.EnsureStarted(seed);   // let the intro's first frames breathe
    }

    void PlayIntro(bool returning)
    {
        if (returning)
        {
            _title.PlayIntro(true);
            _ = _wipe.Open(2);
            _introTween = CreateTween();
            _introTween.TweenInterval(.45);
            _introTween.TweenCallback(Callable.From(EndIntro));
            return;
        }
        _sky.Rise = 12;
        _title.PlayIntro(false);
        _ = _wipe.Open();
        _introTween = CreateTween();
        foreach (var rise in new[] { 8, 4, 0 })
        {
            _introTween.TweenInterval(rise == 8 ? .4 : .2);
            _introTween.TweenCallback(Callable.From(() => _sky.Rise = rise));
        }
        _introTween.TweenInterval(.9);
        _introTween.TweenCallback(Callable.From(EndIntro));
    }

    void SkipIntro()
    {
        _introTween?.Kill();
        _sky.Rise = 0;
        _title.FinishIntro();
        _wipe.SetProgress(0);
        EndIntro();
    }

    void EndIntro()
    {
        _introTween = null;
        if (_stack.Count == 0 && GetViewport().GuiGetFocusOwner() == null) _title.NewGameButton.GrabFocus();
    }

    void OpenDirect(string front)
    {
        _wipe.SetProgress(0);
        var parts = front.Split(':', 2);
        switch (parts[0])
        {
            case "title": _title.NewGameButton.GrabFocus(); break;
            case "confirm":
                _ = Confirm("Выйти в главное меню?", "Сохранений пока нет — партия будет потеряна.", new[] { "Выйти", "Отмена" });
                break;
            default: OpenScreen(parts[0], parts.Length > 1 ? parts[1] : null); break;
        }
    }

    void CreateOverlay()
    {
        _title.SetInteractive(false);
        GetViewport().GuiReleaseFocus();
        _overlay = new Control { Name = "Overlay", MouseFilter = MouseFilterEnum.Ignore };
        _uiRoot.AddChild(_overlay);
        _uiRoot.MoveChild(_overlay, _title.GetIndex() + 1);
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f, OverlayAlpha), MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.AddChild(shade);
        if (!FrontClock.Reduced)
        {
            shade.Modulate = new Color(1, 1, 1, 0);
            shade.CreateTween().TweenProperty(shade, "modulate:a", 1f, .25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }
        _center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.AddChild(_center);
    }

    async Task CloseOverlay()
    {
        var overlay = _overlay;
        _overlay = null;
        _center = null;
        if (!FrontClock.Reduced)
        {
            var t = overlay.CreateTween();
            t.TweenProperty(overlay, "modulate:a", 0f, .2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            await ToSignal(t, Tween.SignalName.Finished);
        }
        overlay.QueueFree();
        _title.SetInteractive(true);
    }

    PanelContainer BuildPanel(FrontScreen screen, IEnumerable<FrontScreen> path)
    {
        screen.Shell = this;
        var panel = MakePanel();
        panel.CustomMinimumSize = new Vector2(PanelWidth(screen), 0);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 14);
        panel.AddChild(layout);

        var header = new VBoxContainer { Name = "Header" };
        header.AddThemeConstantOverride("separation", 6);
        layout.AddChild(header);
        var crumbs = PixelKit.Kicker("PAX PIXELIA › " + string.Join(" › ", path.Select(s => s.Crumb.ToUpperInvariant())), PixelKit.Secondary, 11);
        crumbs.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddChild(crumbs);
        var title = PixelKit.Label(screen.Title.ToUpperInvariant(), 33, Colors.White, HorizontalAlignment.Center);
        title.AddThemeFontOverride("font", PixelKit.Spaced(3));
        PixelKit.TextShadow(title, 1.2f);
        header.AddChild(title);
        header.AddChild(PixelKit.Divider(56));
        if (screen.Subtitle.Length > 0)
        {
            var sub = PixelKit.Paragraph(screen.Subtitle, 16);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            header.AddChild(sub);
        }
        layout.AddChild(screen);
        _center.AddChild(panel);
        screen.Build();
        Nav.Unify(panel);
        return panel;
    }

    async Task ShowPanel(PanelContainer panel)
    {
        PixelKit.KeepPivotCentered(panel);
        if (FrontClock.Reduced) { panel.Modulate = Colors.White; panel.Scale = Vector2.One; return; }
        panel.Modulate = new Color(1, 1, 1, 0);
        panel.Scale = Vector2.One * .9f;
        var t = panel.CreateTween().SetParallel(true);
        t.TweenProperty(panel, "modulate:a", 1f, .2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        t.TweenProperty(panel, "scale", Vector2.One, .42f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        float delay = .08f;
        foreach (var part in panel.GetChild(0).GetChildren())
            foreach (var item in part.GetChildren())
                if (item is Control c && c.Visible) { PixelKit.PopIn(c, delay, .94f, .32f); delay += .04f; }
        await ToSignal(t, Tween.SignalName.Finished);
    }

    async Task HidePanel(PanelContainer panel, bool keep)
    {
        GetViewport().GuiReleaseFocus();
        if (!FrontClock.Reduced)
        {
            var t = panel.CreateTween().SetParallel(true);
            t.TweenProperty(panel, "modulate:a", 0f, .15f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            t.TweenProperty(panel, "scale", Vector2.One * .95f, .15f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            await ToSignal(t, Tween.SignalName.Finished);
        }
        panel.GetParent()?.RemoveChild(panel);
        if (!keep) panel.QueueFree();
    }

    void Shown(FrontScreen screen)
    {
        _busy = false;
        ShowHints(screen.Hints);
        screen.OnShown();
    }

    static void Refocus(Control opener, Control fallback)
    {
        if (opener != null && IsInstanceValid(opener) && opener.IsVisibleInTree() && opener.FocusMode != FocusModeEnum.None) opener.GrabFocus();
        else fallback?.GrabFocus();
    }

    void ShowHints((string key, string text)[] hints)
    {
        foreach (var c in _hintHost.GetChildren()) c.QueueFree();
        if (hints is { Length: > 0 }) _hintHost.AddChild(KeyHint.Bar(hints));
    }

    async void TakeShot(string path)
    {
        await ToSignal(GetTree().CreateTimer(Cli.Float("shot-delay", 2f)), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot saved: {path}");
        if (Cli.Has("quit")) GetTree().Quit();
    }
}
