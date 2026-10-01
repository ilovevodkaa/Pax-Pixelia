using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// All screen-space UI in the monochrome dark pixel style of the shared kit (<see cref="PixelKit"/>, the user's
/// «Mr. President» look), built in code: top bar, notifications, map-mode strip + minimap, province panel, leaderboard,
/// toast, tooltip, chapter card (the loading screen) and the pause menu.
/// Talks to the rest of the game only through <see cref="Game.I"/> events and actions.
/// Keyboard: Space pause, 1–5 speed, Esc cancels scout targeting → closes the leaderboard → closes the panel → pause menu.
/// </summary>
public partial class Hud : CanvasLayer
{
    Control _root;
    TopBar _top;
    Notifications _notes;
    ModeStrip _modes;
    ModeLegend _legend;
    Minimap _mini;
    ProvincePanel _panel;
    Leaderboard _lead;
    TechScreen _tech;
    PolicyCard _policy;
    Toast _toast;
    TipCard _tip;
    ChapterCard _loading;
    EventWindow _events;

    Control _tipOwner;
    int _tipProvince = -1;
    bool _tipDirty;
    // heavy refreshes are coalesced: fog/ownership events may arrive every tick at speed 5
    bool _miniDirty, _leadDirty, _liveDirty, _techDirty, _policyDirty;
    double _miniCooldown, _leadCooldown, _liveCooldown, _miniHeld, _policyCooldown;

    /// <summary>Debug hooks (UiDebug): a fixed mouse position for screenshots and the control whose tip is forced.</summary>
    internal Vector2? FakeMouse;
    internal Control ForcedTip;
    internal ProvincePanel Panel => _panel;
    internal Leaderboard Lead => _lead;
    /// <summary>Set before a rebuilt HUD enters the tree (a new era skin): the chronicle cards and the selection it keeps.</summary>
    internal (List<(string icon, string text, string year)> Notes, int Selected)? Carry;
    internal (List<(string icon, string text, string year)> Notes, int Selected) TakeCarry() => (_notes.Snapshot(), Game.I.Selected);
    internal TechScreen Tech => _tech;
    internal void DebugToggleTech() => ToggleTech();
    internal ChapterCard Loading => _loading;
    internal TopBar Top => _top;
    internal Notifications Notes => _notes;
    internal Toast ToastView => _toast;
    internal TipCard Tip => _tip;
    internal Minimap Mini => _mini;
    internal EventWindow Events => _events;
    internal bool KeepLoading;
    System.Action _debugReady;
    internal Control DebugTarget(string name) => _top.DebugTarget(name) ?? _modes.DebugTarget(name) ?? _mini.DebugTarget(name);
    internal void DebugToggleLead() => ToggleLeaderboard();
    internal void DebugTogglePolicy() => TogglePolicy();
    internal PolicyCard Policy => _policy;

    public override void _Ready()
    {
        Layer = 10;
        _root = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Build(),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,   // pixel art: icons and dither bands scale without blur
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,   // grain and dither tiles
        };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _top = new TopBar();
        _top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _top.LeaderboardToggled += ToggleLeaderboard;
        _top.TechToggled += ToggleTech;
        _top.PolicyToggled += TogglePolicy;
        _top.PauseClicked += TogglePause;
        _root.AddChild(_top);

        _notes = new Notifications();
        _root.AddChild(_notes);

        _modes = new ModeStrip();
        _modes.FogToggled += ToggleFog;
        _legend = new ModeLegend();
        _mini = new Minimap(RegenerateWorld);
        var bottomLeft = Ui.VBox(6, _legend, _modes, _mini);
        bottomLeft.AnchorTop = bottomLeft.AnchorBottom = 1;
        bottomLeft.OffsetLeft = 12; bottomLeft.OffsetBottom = -12; bottomLeft.OffsetTop = -12;
        bottomLeft.GrowVertical = Control.GrowDirection.Begin;
        _root.AddChild(bottomLeft);

        _panel = new ProvincePanel();
        _root.AddChild(_panel);
        _lead = new Leaderboard();
        _root.AddChild(_lead);
        _policy = new PolicyCard();
        _root.AddChild(_policy);
        _tech = new TechScreen { Hud = this };
        _root.AddChild(_tech);
        _events = new EventWindow();
        _root.AddChild(_events);
        _root.AddChild(new BlitzCard());   // «Блиц недели»: the score when the time is up
        _toast = new Toast();
        _root.AddChild(_toast);
        _tip = new TipCard();
        _root.AddChild(_tip);
        _root.AddChild(new SaveIndicator());   // «Сохранено» in the corner after every save
        _loading = new ChapterCard();
        _root.AddChild(_loading);
        AddChild(new PauseMenu());   // last child of the layer: above everything, first to see input

        Subscribe(true);
        // the top bar folds down to fit 1024 px (captions, deltas and the session counter go below 1280)
        GetWindow().MinSize = new Vector2I(1024, 600);
        GetViewport().SizeChanged += OnResize;
        OnResize();
        _modes.Refresh();
        if (Game.I.IsReady) OnWorldReady(); else _loading.ShowNow();
        _debugReady = UiDebug.Setup(this);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= OnResize;
        if (Game.I == null) return;
        Subscribe(false);
        Game.I.WorldReady -= _debugReady;
    }

    void Subscribe(bool on)
    {
        var g = Game.I;
        if (on)
        {
            g.GenerationProgress += OnProgress; g.WorldReady += OnWorldReady; g.ProvinceSelected += OnSelected;
            g.MapModeChanged += OnModeChanged; g.ProvincesChanged += OnProvincesChanged; g.FogChanged += OnFogChanged;
            g.CycleTick += OnCycleTick; g.DateChanged += OnDateChanged; g.TimeControlChanged += OnTimeControl; g.Notified += OnNotified; g.Toast += OnToast;
            g.CameraMoved += OnCameraMoved; g.TargetingChanged += OnTargeting;
            g.ScoutsChanged += OnScoutsChanged;
            g.ResearchChanged += OnResearchChanged;
            g.TribeChanged += OnTribeChanged;
        }
        else
        {
            g.GenerationProgress -= OnProgress; g.WorldReady -= OnWorldReady; g.ProvinceSelected -= OnSelected;
            g.MapModeChanged -= OnModeChanged; g.ProvincesChanged -= OnProvincesChanged; g.FogChanged -= OnFogChanged;
            g.CycleTick -= OnCycleTick; g.DateChanged -= OnDateChanged; g.TimeControlChanged -= OnTimeControl; g.Notified -= OnNotified; g.Toast -= OnToast;
            g.CameraMoved -= OnCameraMoved; g.TargetingChanged -= OnTargeting;
            g.ScoutsChanged -= OnScoutsChanged;
            g.ResearchChanged -= OnResearchChanged;
            g.TribeChanged -= OnTribeChanged;
        }
    }

    // ---------------- game events ----------------
    void OnProgress(string text)
    {
        if (!_loading.Visible || _loading.Modulate.A < 1) _loading.ShowNow();
        _loading.SetStatus($"{text} · зерно {Game.I.Seed}");
    }

    void OnWorldReady()
    {
        _top.OnWorldReady();
        _mini.View.Resample();
        _mini.OnCameraMoved();
        _modes.Refresh();
        _legend.Refresh();
        _panel.Close();
        _notes.Clear();
        if (Carry is { } carry)
        {
            // the same game in a new skin: keep the chronicle and what the player was looking at
            Carry = null;
            _notes.Restore(carry.Notes);
            _lead.Refresh();
            _tech.Refresh();
            _top.SetResearchIdle(Game.I.ResearchIdle);
            _tipDirty = true;
            if (carry.Selected >= 0) Callable.From(() => Game.I.Select(carry.Selected)).CallDeferred();
            return;
        }
        _lead.Refresh();
        _tech.Refresh();
        _top.SetResearchIdle(Game.I.ResearchIdle);
        _tipDirty = true;
        if (!KeepLoading) _loading.FadeOut();
        if (!Cli.Has("noselect") && !Cli.Has("select")) Callable.From(SelectCapital).CallDeferred();   // --select wins
    }

    static void SelectCapital()
    {
        if (!Game.I.IsReady) return;
        Game.I.Select(Sim.Scouts.Capital(Game.I.State, GameState.LocalPlayer));   // the camp while nomadic
    }

    void OnSelected(int p) { if (p >= 0) _panel.Open(p); else _panel.Close(); }

    void OnModeChanged(MapMode m)
    {
        _modes.Refresh();
        _legend.Refresh();
        _mini.View.Recolor();
    }

    void OnProvincesChanged(IReadOnlyList<int> ps)
    {
        _miniDirty = _leadDirty = _tipDirty = true;
        _top.RefreshResources();
        _panel.OnProvincesChanged(ps);
        _legend.Refresh();
    }

    void OnFogChanged(IReadOnlyList<int> ps)
    {
        _miniDirty = _leadDirty = _tipDirty = true;
        _modes.Refresh();
        _legend.Refresh();
        _panel.OnFogChanged();
    }

    /// <summary>A rules cycle ran (every 0.1–2 s by speed): budget, science and the panel's live values.</summary>
    void OnCycleTick()
    {
        _top.OnCycleTick();
        _liveDirty = _leadDirty = _techDirty = _policyDirty = true;   // top bar + panel live values, coalesced in _Process
    }

    /// <summary>The date moves every tick (months/days): the clock follows at once, the heavier live values
    /// (gold, button states in the panel) at most 5× a second — gold may arrive on any tick, not only at a new year.</summary>
    void OnDateChanged()
    {
        _top.OnDateChanged();
        _liveDirty = true;
    }

    void OnTimeControl(bool paused, int speed)
    {
        _top.RefreshClock();
        _tipDirty = true;
    }

    void OnNotified(string icon, string text) => _notes.Add(icon, text);

    void OnToast(string text, float seconds, ToastKind kind) => _toast.Display(text, seconds, kind);
    void OnCameraMoved() => _mini.OnCameraMoved();

    void OnTargeting(bool on)
    {
        _panel.OnTargetingChanged();
        _tipDirty = true;
        if (!on && _toast.Visible && _toast.Kind != ToastKind.Info) _toast.HideNow();   // the sim toasts the outcome
    }

    void OnScoutsChanged()
    {
        _panel.OnScoutsChanged();
        _mini.View.QueueRedraw();
        _leadDirty = true;
    }

    // ---------------- actions ----------------
    void TogglePause()
    {
        if (!Game.I.IsReady) return;
        bool paused = !Game.I.State.Paused;
        Game.I.SetPaused(paused);
        if (paused) Game.I.ShowToast("Пауза. В сетевой игре все видят, кто её поставил.");
    }

    void ToggleFog()
    {
        if (!Game.I.IsReady) return;
        Game.I.SetFogEnabled(!Game.I.State.FogEnabled);   // the sim toasts and drops a selection that went dark
        _modes.Refresh();
        _tipDirty = true;
    }

    void ToggleLeaderboard()
    {
        if (_tech.Visible && !_lead.Visible) ToggleTech();
        if (_policy.Visible && !_lead.Visible) TogglePolicy();
        bool open = _lead.Toggle();
        _top.SetLeaderboardOpen(open);
        if (open) { _leadCooldown = 1; PlaceLeaderboard(); }
    }

    void PlaceLeaderboard() => _lead.Place(_top.Trophy.GetGlobalRect(), _root.Size);

    void TogglePolicy()
    {
        if (!Game.I.IsReady && !_policy.Visible) return;
        if (_tech.Visible && !_policy.Visible) ToggleTech();
        if (_lead.Visible && !_policy.Visible) ToggleLeaderboard();
        bool open = _policy.Toggle();
        _top.SetPolicyOpen(open);
        if (open) { _policyCooldown = .5; PlacePolicy(); }
    }

    void PlacePolicy() => _policy.Place(_top.PolicyButton.GetGlobalRect(), _root.Size);

    void ToggleTech()
    {
        if (_lead.Visible && !_tech.Visible) ToggleLeaderboard();
        if (_policy.Visible && !_tech.Visible) TogglePolicy();
        if (!Game.I.IsReady && !_tech.Visible) return;
        bool open = _tech.Toggle();
        _top.SetTechOpen(open);
        _top.SetResearchIdle(Game.I.ResearchIdle);
        if (open) HideTip();
    }

    /// <summary>The screen's × button.</summary>
    internal void CloseTech() { if (_tech.Visible) ToggleTech(); }

    /// <summary>The tribe moved, set out, stopped or settled: the panel follows the camp (or the new capital).</summary>
    void OnTribeChanged()
    {
        var g = Game.I;
        if (!g.IsReady) return;
        int home = Sim.Scouts.Capital(g.State, g.Viewer);
        if (_panel.Visible && home >= 0 && _panel.Province != home && (g.State.Nat[g.Viewer].Camp >= 0 || g.Selected == home))
        {
            // the panel showed the camp: keep it on the tribe as it walks
            if (_followCamp) g.Select(home); else _panel.Rebuild();
        }
        else if (_panel.Visible) _panel.Rebuild();
        _followCamp = _panel.Visible && _panel.Province == home;
        _liveDirty = true;
    }

    bool _followCamp = true;

    void OnResearchChanged()
    {
        _tech.Refresh();
        _top.SetResearchIdle(Game.I.ResearchIdle);
        _liveDirty = _tipDirty = true;
        _panel.Rebuild();   // new buildings may have opened in the build menu
    }

    void RegenerateWorld()
    {
        int seed = (int)(GD.Randi() % 1_000_000);
        if (Game.I.IsTargeting) Game.I.CancelScoutTargeting();
        _panel.Close();
        if (_lead.Visible) ToggleLeaderboard();
        if (_policy.Visible) TogglePolicy();
        if (_tech.Visible) ToggleTech();
        _toast.HideNow();
        _loading.ShowNow();
        _loading.SetStatus($"Генерация мира · зерно {seed}");
        Game.I.RegenerateWorld(seed);
    }

    // ---------------- keyboard ----------------
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.Space:
                TogglePause();
                break;
            case >= Key.Key1 and <= Key.Key5:
                if (Game.I.IsReady) Game.I.SetSpeed((int)(k.Keycode - Key.Key0));
                break;
            case >= Key.Kp1 and <= Key.Kp5:
                if (Game.I.IsReady) Game.I.SetSpeed((int)(k.Keycode - Key.Kp0));
                break;
            case Key.T:
                if (Game.I.IsReady && !_loading.Visible) ToggleTech();
                break;
            case Key.Escape:
                if (_tech.Visible) ToggleTech();
                else if (Game.I.IsTargeting) Game.I.CancelScoutTargeting();
                else if (_lead.Visible) ToggleLeaderboard();
                else if (_policy.Visible) TogglePolicy();
                else if (_panel.Visible) Game.I.Select(-1);
                else if (Game.I.IsReady && !_loading.Visible) PauseMenu.Open();
                else return;
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    // ---------------- layout ----------------
    void OnResize()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _top.SetWidth(size.X);
        bool shortScreen = size.Y <= 800;
        int mw = shortScreen ? 240 : 288, mh = shortScreen ? 135 : 162;
        _mini.View.SetMapSize(mw, mh);
        _modes.SetWidth(mw + 16);
        _panel.SetViewport(size);
        if (_lead.Visible) Callable.From(PlaceLeaderboard).CallDeferred();
        if (_policy.Visible) Callable.From(PlacePolicy).CallDeferred();
    }

    // ---------------- tooltip ----------------
    public override void _Process(double delta)
    {
        UpdateTip();
        _liveCooldown -= delta;
        if (_liveDirty && _liveCooldown <= 0) { _liveDirty = false; _liveCooldown = .2; _top.RefreshResources(); _panel.RefreshLive(); }
        _miniCooldown -= delta;
        // while a capture fill runs on the map the minimap keeps its old colours and snaps when it ends (at most 1.2 s late)
        if (_miniDirty && Game.I.CaptureFillsRunning && _miniHeld < 1.2) _miniHeld += delta;
        else if (_miniDirty && _miniCooldown <= 0) { _miniDirty = false; _miniHeld = 0; _miniCooldown = .25; _mini.View.Recolor(); }
        if (_policy.Visible)
        {
            _policyCooldown -= delta;
            if (_policyDirty && _policyCooldown <= 0) { _policyDirty = false; _policyCooldown = .5; _policy.Refresh(); }
            PlacePolicy();
        }
        if (_lead.Visible)
        {
            _leadCooldown -= delta;
            if (_leadDirty && _leadCooldown <= 0) { _leadDirty = false; _leadCooldown = 1; _lead.Refresh(); }
            PlaceLeaderboard();
        }
        if (_techDirty)
        {
            _techDirty = false;
            _top.SetResearchIdle(Game.I.ResearchIdle);
            if (_tech.Visible) _tech.Refresh();
        }
    }

    void UpdateTip()
    {
        var vp = GetViewport();
        var mouse = FakeMouse ?? vp.GetMousePosition();
        var screen = _root.Size;
        var hovered = ForcedTip ?? vp.GuiGetHoveredControl();
        if (hovered == _toast) hovered = null;   // the toast lets the map show through (pick tips stay visible)
        if (hovered != null && !_loading.Visible)
        {
            if (Tips.TryFind(hovered, out var owner, out var build))
            {
                if (owner != _tipOwner || _tipDirty) { _tip.Build(build); _tipOwner = owner; _tipProvince = -1; _tipDirty = false; }
                if (ForcedTip != null && FakeMouse == null) mouse = owner.GetGlobalRect().Position + owner.Size * new Vector2(.5f, 1);
                _tip.ShowAt(mouse, screen);
            }
            else HideTip();
            return;
        }
        int p = Game.I.IsReady && !_loading.Visible ? Game.I.Hovered : -1;
        bool inside = mouse.X >= 0 && mouse.Y >= 0 && mouse.X < screen.X && mouse.Y < screen.Y;
        if (p < 0 || !inside || (FakeMouse == null && Input.IsMouseButtonPressed(MouseButton.Left))) { HideTip(); return; }
        if (p != _tipProvince || _tipOwner != null || _tipDirty)
        {
            _tipOwner = null; _tipDirty = false;
            _tipProvince = _tip.BuildProvince(p) ? p : -1;
            if (_tipProvince < 0) { HideTip(); return; }
        }
        _tip.ShowAt(mouse, screen);
    }

    void HideTip()
    {
        _tip.Visible = false;
        _tipOwner = null;
        _tipProvince = -1;
    }
}
