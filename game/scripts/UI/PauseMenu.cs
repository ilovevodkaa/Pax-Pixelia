using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.UI.Front;

namespace PaxPixelia.UI;

/// <summary>
/// In-game pause menu (MAIN_MENU.md §3.7): the end of the HUD's Esc chain. Pauses the game (and restores the previous
/// state on close) without pausing the scene tree, so the map keeps drawing under the 0.78 shade. Продолжить ·
/// Сохранить (asks for a name; the same name overwrites after a question) · Загрузить (the front-end «Загрузить»
/// screen in the same overlay; the running game goes to an autosave first) · Настройки · В главное меню · Выйти из
/// игры (both confirmed; the game is autosaved on the way out, «ПРОДОЛЖИТЬ» brings it back). While open it takes
/// every key except F12. Debug: --pausemenu opens it after WorldReady.
/// </summary>
public partial class PauseMenu : Control
{
    public const string FrontScene = "res://scenes/Front.tscn";
    public const string MainScene = "res://scenes/Main.tscn";
    public const string LeaveText = "Партия сохранится в автосохранение — «Продолжить» в главном меню вернёт вас сюда.";
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
        _shade = new ColorRect { Color = Pal.Light ? new Color(Pal.Ink, .55f) : new Color(Pal.Ink, .8f), MouseFilter = MouseFilterEnum.Ignore };
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
        MenuButton(col, "Сохранить", "", () => _ = SaveManual());
        var load = MenuButton(col, "Загрузить", "", OpenLoad);
        if (!SaveStore.Any()) { load.Disabled = true; load.TooltipText = "Сохранений пока нет"; }
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
        b.CustomMinimumSize = new Vector2(320, _live != null && _live.GetViewportRect().Size.Y < 720 ? 40 : 48);   // six items on 1024×600
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

    // ---- saves (F-4) ----

    /// <summary>«Сохранить»: a name (the default «Ардания · март 3200 до н. э.»), a question when a manual save of that
    /// name exists, then the save with a thumbnail of the map (the menu is not in it).</summary>
    async Task SaveManual()
    {
        var g = Game.I;
        if (!g.IsReady || _leaving || PxConfirm.IsOpen) return;
        var name = await PxConfirm.AskText(this, "Сохранить партию", "Название сохранения:", g.DefaultSaveName(), "Сохранить");
        if (name == null || !Visible || !g.IsReady) return;
        if (name.Length == 0) name = g.DefaultSaveName();
        string path = null;
        if (SaveStore.FindManual(name) is { } old)
        {
            int a = await PxConfirm.Ask(this, "Перезаписать?", $"Сохранение «{old.Header.Name}» уже есть, от {SaveText.When(old.Header.SavedUnixMs)}.",
                new[] { "Перезаписать", "Отмена" }, focus: 1, danger: 0);
            if (a != 0 || !Visible) return;
            path = old.Path;
        }
        var r = await g.SaveAsync(SaveKind.Manual, name, path);
        if (_date == null || !IsInstanceValid(_date) || !_date.IsInsideTree()) return;
        _date.ClipText = true;   // a long name must not widen the panel
        _date.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _date.Text = r.Ok ? $"Сохранено «{name}»" : r.Error;
        _date.AddThemeColorOverride("font_color", r.Ok ? PixelKit.Text : PixelKit.Bad);
        PixelKit.PopIn(_date, 0, .8f, .3f);
        PixelKit.Sfx?.Invoke(r.Ok ? "confirm" : "error", 1f);
        if (r.Ok && FindLoadButton() is { } load) { load.Disabled = false; load.TooltipText = ""; }
    }

    Button FindLoadButton() => _panel?.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "Загрузить");

    /// <summary>«Загрузить»: the front-end screen inside the pause panel.</summary>
    void OpenLoad()
    {
        var screen = new LoadScreen { DialogHost = this };
        screen.Closed += () => { if (Visible) ShowPanel(BuildMain()); };
        screen.LoadRequested = e => _ = LoadSave(e);
        _continue = null;
        ShowPanel(ScreenHost.Wrap(screen, GetViewportRect().Size));
        PixelKit.Sfx?.Invoke("open", 1f);
    }

    /// <summary>Load a save over the running game: asked first, the running game goes to an autosave slot (never over
    /// the chosen file), then the same path as «ПРОДОЛЖИТЬ» — curtain, Main.tscn, the chapter card.</summary>
    async Task LoadSave(SaveEntry e)
    {
        if (_leaving || PxConfirm.IsOpen || e is not { Ok: true }) return;
        int answer = await PxConfirm.Ask(this, "Загрузить сохранение?", $"«{SaveText.Title(e.Header)}» · {e.Header.DateText}. Текущая партия сохранится в автосохранение.",
            new[] { "Загрузить", "Отмена" }, focus: 0, danger: -1);
        if (answer != 0 || _leaving) return;
        _leaving = true;
        var g = Game.I;
        var r = await g.AutoSave.SaveOnLeave(keep: e.Path);
        if (!r.Ok)
        {
            // as on leaving: the running game is only dropped with the player's yes (a load can still fail after it)
            GD.PushWarning($"save: the running game was not saved before loading: {r.Error}");
            int again = await PxConfirm.Ask(this, "Не удалось сохранить", $"{r.Error}. Загрузить без сохранения текущей партии?",
                new[] { "Загрузить", "Отмена" }, focus: 1, danger: 0);
            if (again != 0) { _leaving = false; return; }
        }
        var world = g.World is { } w && w.Seed == e.Header.Seed && w.W == e.Header.WorldW && w.H == e.Header.WorldH ? w : null;
        await Leave(() =>
        {
            Session.Pending = new PendingGame(e.Header.ToSetup(), world) { LoadPath = e.Path };
            return MainScene;
        });
    }

    async Task ConfirmLeave(bool quit)
    {
        int answer = await PxConfirm.Ask(this, quit ? "Выйти из игры?" : "Выйти в главное меню?",
            LeaveText, new[] { "Выйти", "Отмена" }, focus: 1, danger: 0);
        if (answer != 0 || _leaving) return;
        _leaving = true;
        var r = await Game.I.AutoSave.SaveOnLeave();
        if (!r.Ok)
        {
            int again = await PxConfirm.Ask(this, "Не удалось сохранить", $"{r.Error}. Выйти без сохранения?",
                new[] { "Выйти", "Отмена" }, focus: 1, danger: 0);
            if (again != 0) { _leaving = false; return; }
        }
        if (quit)
        {
            var wipeOut = new DitherWipe { Cell = Mathf.Max(2, (int)(GetViewportRect().Size.Y / 225)) };
            GetTree().Root.AddChild(wipeOut);
            await wipeOut.Close(2);
            GetTree().Quit();
            return;
        }
        await Leave(() => { Session.ReturnedFromGame = true; return FrontScene; });
    }

    /// <summary>Curtain, end the game, switch scenes (the scene path comes from <paramref name="prepare"/>, run after EndGame).</summary>
    async Task Leave(Func<string> prepare)
    {
        var tree = GetTree();   // this menu leaves the tree with the game scene
        var wipe = new DitherWipe { Cell = Mathf.Max(2, (int)(GetViewportRect().Size.Y / 225)) };
        tree.Root.AddChild(wipe);
        await wipe.Close(2);
        Game.I.EndGame();
        tree.ChangeSceneToFile(prepare());
        // the next scene starts behind its own closed curtain (the title's, the chapter card's) and opens it; ours goes away
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
