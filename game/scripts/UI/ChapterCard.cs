using System;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Flags;
using PaxPixelia.UI.Front;

namespace PaxPixelia.UI;

/// <summary>
/// The chapter card (MAIN_MENU.md §3.8) — the loading screen of a new game and a drop-in for the old LoadingScreen
/// (ShowNow / SetStatus / FadeOut): the «Горизонт эпох» sky with only the current era on the horizon,
/// «ГЛАВА I · ПЕРВОБЫТНАЯ · 4000 ДО Н. Э.», the player's flag, the name with the title shimmer, the era's saying,
/// 24 progress cells, the generator status and a tip. Started from the menu (Session.LaunchedFromMenu) it opens with
/// its own dither curtain, holds ≥ 1.6 s after WorldReady (any key skips) and closes through the curtain onto the
/// paused map; from the CLI it fades out at once so tests are not slowed down.
/// </summary>
public partial class ChapterCard : Control
{
    static readonly string[] Sayings =
    {
        "Где костёр — там и дом", "Столица не сразу строилась", "Все дороги ведут в столицу", "Тише едешь — дальше будешь",
        "Семь раз отмерь — один раз отрежь", "Без труда не вытащишь и угля из шахты", "Куй железо, пока горячо",
        "Мирный атом — в каждый дом", "Не всё то правда, что в ленте", "На орбите — наоборот: быстрее едешь — дальше будешь",
        "Ещё один год — и точно спать",
    };
    static readonly (string Text, float Share)[] Stages =
        { ("Рельеф…", .15f), ("Климат…", .35f), ("Реки и провинции…", .55f), ("Провинции…", .75f), ("Державы и границы…", .90f) };
    const int Cells = 24, CellStep = 16;
    const float MinHold = 1.6f;
    const string TipsPath = "res://assets/front/tips.json";

    readonly SkyBackdrop _sky = new() { Name = "Sky" };
    readonly TextureRect _flag;
    readonly Label _kicker, _name, _saying, _status, _tip, _continue;
    readonly ShaderMaterial _shimmer;
    readonly DrawLayer _bar;
    DitherWipe _wipe;
    float _target, _shown;   // progress 0..1: target from the generator, shown = lit cells / Cells
    double _cellClock, _visibleFor;
    bool _leaving, _skip;
    Tween _fade;
    string[] _tips;

    bool HoldMode => Session.LaunchedFromMenu && !FrontClock.Reduced;

    public ChapterCard()
    {
        Name = "ChapterCard";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = PixelTheme.Build();
        AddChild(_sky);

        _kicker = PixelKit.Label("", 22, PixelKit.TextDim, HorizontalAlignment.Center);
        _kicker.AddThemeFontOverride("font", PixelKit.Spaced(3));
        _flag = SetupUi.Pixel(new Vector2(120, 84));
        _flag.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _name = PixelKit.Label("", 66, PixelKit.AccentLight, HorizontalAlignment.Center);
        _name.AddThemeFontOverride("font", PixelKit.Spaced(6));
        PixelKit.TextShadow(_name, 2.2f);
        _shimmer = Shimmer.Attach(_name, 66);
        _saying = PixelKit.Label("", 22, PixelKit.TextDim, HorizontalAlignment.Center);
        _bar = new DrawLayer(DrawCells) { CustomMinimumSize = new Vector2(Cells * CellStep - 4, 12), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        _status = PixelKit.Label("", 16, PixelKit.Text, HorizontalAlignment.Center);
        _tip = PixelKit.Label("", 16, PixelKit.TextDim, HorizontalAlignment.Center);
        _tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _tip.CustomMinimumSize = new Vector2(760, 0);
        _tip.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _continue = PixelKit.Label("любая клавиша — продолжить", 13, PixelKit.TextDim, HorizontalAlignment.Right);
        _continue.AddThemeFontOverride("font", PixelKit.Spaced(1));
        _continue.Visible = false;

        var col = SetupUi.Column(0, _kicker, SetupUi.Gap(26), _flag, SetupUi.Gap(22), _name, SetupUi.Gap(8), _saying,
            SetupUi.Gap(56), _bar, SetupUi.Gap(14), _status, SetupUi.Gap(30), _tip);
        var band = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore, AnchorLeft = 0, AnchorRight = 1, AnchorTop = .08f, AnchorBottom = .72f };
        band.AddChild(col);
        AddChild(band);
        AddChild(_continue);
        // only the card itself takes the mouse (while it is up); its contents never do, so a fading card lets the map through
        foreach (var n in FindChildren("*", "Control", true, false)) ((Control)n).MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Ready()
    {
        _sky.ShowPlanet = false;
        Game.I.GenerationProgress += OnProgress;
        Game.I.WorldReady += OnWorldReady;
        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public override void _ExitTree()
    {
        if (Game.I != null) { Game.I.GenerationProgress -= OnProgress; Game.I.WorldReady -= OnWorldReady; }
        GetViewport().SizeChanged -= Layout;
    }

    // ---- the LoadingScreen API used by Hud ----
    public void ShowNow()
    {
        _fade?.Kill();
        Modulate = Colors.White;
        MouseFilter = MouseFilterEnum.Stop;
        if (Visible && !_leaving) return;
        Visible = true;
        _leaving = _skip = false;
        _target = _shown = 0;
        _visibleFor = 0;
        _continue.Visible = false;
        _tip.Text = "";
        Fill();
        if (HoldMode) _ = OpenCurtain();
    }

    public void SetStatus(string text) => _status.Text = text;

    /// <summary>The world is ready: from the menu hold the card (≥ 1.6 s, any key skips), from the CLI fade at once.</summary>
    public void FadeOut()
    {
        if (!Visible || _leaving) return;
        _target = 1;
        if (HoldMode) { _ = Leave(); return; }
        MouseFilter = MouseFilterEnum.Ignore;
        _fade?.Kill();
        _fade = CreateTween();
        _fade.TweenProperty(this, "modulate:a", 0f, .35).SetEase(Tween.EaseType.In);
        _fade.TweenCallback(Callable.From(() => Visible = false));
    }

    // ---- content ----
    void Fill()
    {
        var g = Game.I;
        var (name, rgb, flag) = PlayerLook();
        int era = Math.Clamp(g.EraIndex, 0, Sayings.Length - 1);
        _kicker.Text = $"Глава {Roman(era + 1)} · {g.EraName}{(g.DateText == "" ? "" : " · " + g.DateText)}".ToUpper();
        _name.Text = name.ToUpper();
        _saying.Text = $"«{Sayings[era]}»";
        _flag.Texture = FlagTextures.Get(flag, rgb);
        if (_sky.Mat != null) _sky.SoloEra = era;
        _tips ??= LoadTips();
        if (_tip.Text == "" && _tips.Length > 0) _tip.Text = "Совет: " + _tips[(int)(GD.Randi() % (uint)_tips.Length)];
    }

    /// <summary>Name, colour and flag of the player: the setup's design (known before the world exists) or the roster.</summary>
    static (string, (byte, byte, byte), FlagSpec) PlayerLook()
    {
        var g = Game.I;
        if (g.Setup?.Player is { } d) return (d.Name, d.Rgb, d.Flag);
        var n = g.Nations[0];
        return (n.Name, (n.R, n.G, n.B), n.Flag == default ? FlagSpec.ForBot(g.Seed, 0) : n.Flag);
    }

    static string[] LoadTips()
    {
        if (!FileAccess.FileExists(TipsPath)) return Array.Empty<string>();
        var json = Json.ParseString(FileAccess.GetFileAsString(TipsPath));
        return json.VariantType == Variant.Type.Dictionary && json.AsGodotDictionary().TryGetValue("tips", out var tips)
            ? tips.AsStringArray() : Array.Empty<string>();
    }

    static string Roman(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X", 11 => "XI", _ => n.ToString(),
    };

    void Layout()
    {
        var size = GetViewportRect().Size;
        bool compact = size.Y < 800;
        Shimmer.SetSize(_name, _shimmer, compact ? 44 : 66);
        _flag.CustomMinimumSize = new Vector2(20, 14) * (compact ? 4 : 6);
        var min = _continue.GetCombinedMinimumSize();
        _continue.Size = min;
        _continue.Position = size - min - new Vector2(18, 18);
    }

    void OnProgress(string text)
    {
        foreach (var (t, share) in Stages)
            if (t == text) { _target = Mathf.Max(_target, share); return; }
        _target = Mathf.Min(.95f, _target + .1f);
    }

    void OnWorldReady()
    {
        _target = 1;
        if (Visible) Fill();   // the date and the roster exist now
    }

    // ---- menu launch: curtain in, hold, curtain out ----
    async Task OpenCurtain()
    {
        _wipe ??= AddWipe();
        await _wipe.Open();
    }

    DitherWipe AddWipe()
    {
        var w = new DitherWipe(startClosed: true);
        GetTree().Root.AddChild(w);
        w.Cell = _sky.PixelSize;
        return w;
    }

    async Task Leave()
    {
        _leaving = true;
        // two frames hide the hitch of building the map; the card stays at least MinHold seconds
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        while (_visibleFor < MinHold && !_skip) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _wipe ??= AddWipe();
        await _wipe.Close();
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        await _wipe.Open();
        _wipe.QueueFree();
        _wipe = null;
        if (Game.I.IsReady && Game.I.State.Paused) Game.I.ShowToast("Пауза. Пробел — пустить время", 5);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } && _leaving) _skip = true;
    }

    public override void _Input(InputEvent e)
    {
        // only a menu launch holds the card and owns the keyboard; CLI runs (tests, --keys) keep the old behaviour
        if (!Visible || !HoldMode || e is not InputEventKey { Pressed: true } k || k.Keycode == Key.F12) return;
        if (_leaving) _skip = true;
        GetViewport().SetInputAsHandled();   // nothing reaches the map under the card
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _visibleFor += delta;
        FrontClock.Tick(delta);   // no FrontShell in Main.tscn: the card drives the sky, smoke and shimmer
        if (_leaving && !_continue.Visible && _visibleFor > .4) { _continue.Visible = true; PixelKit.PopIn(_continue, 0, .9f, .3f); }
        // cells run toward the target one at a time every 30 ms, no smoothing
        _cellClock += delta;
        while (_cellClock >= .03)
        {
            _cellClock -= .03;
            if (_shown < _target - .001f) { _shown = Mathf.Min(_target, _shown + 1f / Cells); _bar.QueueRedraw(); }
        }
    }

    void DrawCells(Control c)
    {
        int lit = (int)Mathf.Round(_shown * Cells);
        for (int i = 0; i < Cells; i++)
            c.DrawRect(new Rect2(i * CellStep, 0, 12, 12), i < lit ? (i == lit - 1 ? PixelKit.AccentLight : PixelKit.Accent) : PixelKit.Surface);
    }
}
