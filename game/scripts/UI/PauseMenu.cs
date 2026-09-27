using System;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.UI.Front;

namespace PaxPixelia.UI;

/// <summary>
/// In-game pause menu (MAIN_MENU.md §3.7): the end of the HUD's Esc chain. Pauses the game (and restores the previous
/// state on close) without pausing the scene tree, so the map keeps drawing under the 0.78 shade. Продолжить ·
/// Настройки (the front-end settings screen in the same overlay) · В главное меню · Выйти из игры (both confirmed:
/// there are no saves yet). While open it takes every key except F12. Debug: --pausemenu opens it after WorldReady.
/// </summary>
public partial class PauseMenu : Control
{
    public const string FrontScene = "res://scenes/Front.tscn";
    static PauseMenu _live;

    public static bool IsOpen => _live is { Visible: true };

    /// <summary>Opens the menu of the running game (no-op before the world is ready).</summary>
    public static void Open() => _live?.OpenMenu();

    ColorRect _shade;
    CenterContainer _center;
    Control _panel;
    Label _date, _session;
    Button _continue;
    bool _wasPaused, _leaving;

    public PauseMenu()
    {
        Name = "PauseMenu";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Theme = PixelTheme.Build();
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        _shade = new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f, 0.78f), MouseFilter = MouseFilterEnum.Ignore };
        _shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_shade);
        _center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_center);
    }

    public override void _EnterTree() => _live = this;
    public override void _ExitTree() { if (_live == this) _live = null; }

    public override void _Ready()
    {
        if (Cli.Has("pausemenu")) Game.I.WorldReady += OpenForShot;
    }

    void OpenForShot()
    {
        Game.I.WorldReady -= OpenForShot;
        GetTree().CreateTimer(.3).Timeout += OpenMenu;
    }

    void OpenMenu()
    {
        if (!Game.I.IsReady || Visible || _leaving) return;
        _wasPaused = Game.I.State.Paused;
        Game.I.SetPaused(true);
        Visible = true;
        GetParent()?.MoveChild(this, -1);   // topmost: first to get input
        ShowPanel(BuildMain());
        _shade.Modulate = new Color(1, 1, 1, 0);
        _shade.CreateTween().TweenProperty(_shade, "modulate:a", 1f, .25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        PixelKit.Sfx?.Invoke("open", 1f);
    }

    public void CloseMenu()
    {
        if (!Visible || _leaving) return;
        PixelKit.Sfx?.Invoke("close", 1f);
        GetViewport().GuiReleaseFocus();
        Visible = false;
        _panel?.QueueFree();
        _panel = null;
        if (Game.I.IsReady) Game.I.SetPaused(_wasPaused);
    }

    Control BuildMain()
    {
        var col = SetupUi.Column(10);
        col.AddChild(PixelKit.Kicker("PAX PIXELIA", PixelKit.Secondary, 11));
        var title = PixelKit.Label("ПАУЗА", 33, PixelKit.AccentLight);
        title.AddThemeFontOverride("font", PixelKit.Spaced(3));
        PixelKit.TextShadow(title, 1.2f);
        col.AddChild(title);
        var div = PixelKit.Divider(56);
        div.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        col.AddChild(div);
        var g = Game.I;
        _date = PixelKit.Label($"{g.DateText} · {g.Nations[0].Name}", 16, PixelKit.TextDim);
        col.AddChild(_date);
        col.AddChild(SetupUi.Gap(4));

        _continue = MenuButton(col, "ПРОДОЛЖИТЬ", "PrimaryButton", CloseMenu, "Esc");
        MenuButton(col, "Настройки", "", OpenSettings);
        MenuButton(col, "В главное меню", "GhostButton", () => _ = ConfirmLeave(false));
        MenuButton(col, "Выйти из игры", "GhostButton", () => _ = ConfirmLeave(true));

        col.AddChild(SetupUi.Gap(2));
        _session = PixelKit.Label(SessionLine(), 13, PixelKit.TextDim);
        _session.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var copy = SetupUi.IconButton(PxIcon.Copy, "Скопировать зерно", 34);
        copy.Pressed += () =>
        {
            DisplayServer.ClipboardSet(g.Setup?.SeedText ?? g.Seed.ToString());
            _session.Text = "Зерно скопировано";
            PixelKit.PopIn(_session, 0, .8f, .3f);
        };
        col.AddChild(SetupUi.Row(8, _session, copy));
        return Panel(col, 380);
    }

    static string SessionLine()
    {
        int m = (int)(Time.GetTicksMsec() / 60000);
        var g = Game.I;
        return $"Сессия {m / 60}:{m % 60:00} · зерно {g.Setup?.SeedText ?? g.Seed.ToString()}";
    }

    static Button MenuButton(Container parent, string text, string variation, Action onPress, string key = null)
    {
        var b = PixelKit.Button(text, variation);
        b.CustomMinimumSize = new Vector2(320, 48);
        if (variation == "PrimaryButton") { b.AddThemeFontOverride("font", PixelKit.Spaced(2)); b.AddThemeFontSizeOverride("font_size", 20); }
        if (key != null) SetupUi.WithKey(b, key);
        else b.Alignment = HorizontalAlignment.Left;
        b.Pressed += onPress;
        parent.AddChild(b);
        return b;
    }

    /// <summary>The front-end screen panel (FrontShell.MakePanel) holding <paramref name="content"/>.</summary>
    public static PanelContainer Panel(Control content, float width)
    {
        var p = FrontShell.MakePanel(30, 22);
        p.CustomMinimumSize = new Vector2(width, 0);
        p.AddChild(content);
        return p;
    }

    void ShowPanel(Control panel)
    {
        _panel?.QueueFree();
        _panel = panel;
        _center.AddChild(panel);
        if (!FrontClock.Reduced) PixelKit.PopIn(panel, 0, .9f, .3f);
        if (panel.GetChild(0) is ScreenHost host) { host.Open(); Nav.Unify(panel); return; }   // the screen focuses itself
        if (panel.GetChild(0) is Container col) SetupUi.Stagger(col, .06f, .04f);
        Nav.Unify(panel);
        Callable.From(() => (_continue is { } c && c.IsInsideTree() && c.IsVisibleInTree() ? c : Nav.FirstFocusable(panel))?.GrabFocus()).CallDeferred();
    }

    void OpenSettings()
    {
        var screen = new SettingsScreen { DialogHost = this };
        screen.Closed += () => { if (Visible) ShowPanel(BuildMain()); };
        _continue = null;
        ShowPanel(ScreenHost.Wrap(screen, GetViewportRect().Size));
        PixelKit.Sfx?.Invoke("open", 1f);
    }

    async Task ConfirmLeave(bool quit)
    {
        int answer = await PxConfirm.Ask(this, quit ? "Выйти из игры?" : "Выйти в главное меню?",
            "Сохранений пока нет — партия будет потеряна.", new[] { "Выйти", "Отмена" }, focus: 1, danger: 0);
        if (answer != 0 || _leaving) return;
        _leaving = true;
        var tree = GetTree();   // this menu leaves the tree with the game scene
        var wipe = new DitherWipe { Cell = Mathf.Max(2, (int)(GetViewportRect().Size.Y / 225)) };
        tree.Root.AddChild(wipe);
        await wipe.Close(2);
        if (quit) { tree.Quit(); return; }
        Game.I.EndGame();
        Session.ReturnedFromGame = true;
        tree.ChangeSceneToFile(FrontScene);
        // the title starts behind its own closed curtain (ReturnedFromGame) and opens it; ours just goes away
        for (int i = 0; i < 2; i++) await wipe.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        wipe.QueueFree();
    }

    // ---- input: Esc = back / continue; everything else stops here (F12 passes) ----
    public override void _Input(InputEvent e)
    {
        if (!Visible || PxConfirm.IsOpen || !e.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        if (_panel?.GetChild(0) is ScreenHost host) host.Screen.GoBack();
        else CloseMenu();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is InputEventKey { Keycode: Key.F12 }) return;
        if (e is InputEventKey or InputEventMouseButton) GetViewport().SetInputAsHandled();
    }
}

/// <summary>Hosts a front-end screen outside the FrontShell (settings from the pause menu): the same header — crumbs,
/// 33 px title, divider — inside the pause panel.</summary>
public partial class ScreenHost : VBoxContainer
{
    public FrontScreen Screen { get; private set; }

    public static PanelContainer Wrap(FrontScreen screen, Vector2 viewport)
    {
        var host = new ScreenHost { Screen = screen };
        host.AddThemeConstantOverride("separation", 14);
        var header = SetupUi.Column(6, PixelKit.Kicker(("PAX PIXELIA  ›  ПАУЗА  ›  " + screen.Crumb).ToUpper(), PixelKit.Secondary, 11));
        var title = PixelKit.Label(screen.Title.ToUpper(), 33, PixelKit.AccentLight);
        title.AddThemeFontOverride("font", PixelKit.Spaced(3));
        PixelKit.TextShadow(title, 1.2f);
        header.AddChild(title);
        var div = PixelKit.Divider(56);
        div.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        header.AddChild(div);
        host.AddChild(header);
        host.AddChild(screen);
        return PauseMenu.Panel(host, Mathf.Min(screen.PanelWidth, viewport.X - 48));
    }

    /// <summary>Builds the screen once the panel is in the tree (layout and fonts need the theme).</summary>
    public void Open()
    {
        Screen.Build();
        Callable.From(Screen.OnShown).CallDeferred();
    }
}
