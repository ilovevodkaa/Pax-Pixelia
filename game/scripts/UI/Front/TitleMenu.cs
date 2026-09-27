using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// The title screen layer (MAIN_MENU.md §3.1): the «PAX PIXELIA» pixel logo (stepped gradient, diagonal shimmer,
/// hard shadow — no slogan), the BigButton column and the corner captions. Sizes switch to the compact set below
/// 800 px of height (logo 66, buttons 340×48 with step 8).
/// </summary>
public partial class TitleMenu : Control
{
    public const string TitleText = "PAX PIXELIA";
    public const string Signature = "by _ilovevodka";

    readonly FrontShell _shell;
    readonly List<Button> _buttons = new();
    readonly List<Tween> _intro = new();
    VBoxContainer _logo, _menu;
    Label _title, _author;
    ShaderMaterial _titleMat;
    ColorRect _divider;
    bool _compact;

    public Button NewGameButton { get; private set; }
    public Button QuitButton { get; private set; }
    public IReadOnlyList<Button> Buttons => _buttons;

    public TitleMenu(FrontShell shell) => _shell = shell;

    public bool Shimmer { set => _titleMat.SetShaderParameter("period", value ? 6f : 0f); }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _logo = Column(.06f, .36f, 10);
        _menu = Column(.38f, .84f, 10);

        _title = PixelKit.Label(TitleText, 88, Colors.White, HorizontalAlignment.Center);
        _title.Name = "Title";
        _title.AddThemeFontOverride("font", PixelKit.Spaced(6));
        PixelKit.TextShadow(_title, 2.2f);
        _titleMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/front/title_shimmer.gdshader") };
        _titleMat.SetShaderParameter("period", FrontClock.Reduced ? 0f : 6f);
        _title.Material = _titleMat;
        _title.Resized += UpdateTitleGradient;
        FrontClock.Register(_titleMat);
        _logo.AddChild(_title);
        _divider = PixelKit.Divider(120);
        _divider.Name = "Divider";
        _logo.AddChild(_divider);

        NewGameButton = AddItem("НОВАЯ ИГРА", () => _shell.OpenScreen("newgame"));
        var mp = AddItem("СЕТЕВАЯ ИГРА", () => _shell.OpenScreen("mpstub"));
        var badge = PixelKit.Badge("M2", PixelKit.Surface, PixelKit.TextDim, 11);
        badge.MouseFilter = MouseFilterEnum.Ignore;
        foreach (var child in badge.GetChildren()) ((Control)child).MouseFilter = MouseFilterEnum.Ignore;
        mp.AddChild(badge);
        badge.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterRight, LayoutPresetMode.KeepSize, 14);
        AddItem("НАСТРОЙКИ", () => _shell.OpenScreen("settings"));
        AddItem("АВТОРЫ", () => _shell.OpenScreen("credits"));
        QuitButton = AddItem("ВЫЙТИ", _shell.Quit);
        Nav.Unify(_menu);
        for (int i = 0; i < _buttons.Count; i++)
        {
            // wrap around the column with ↑/↓
            var b = _buttons[i];
            b.FocusNeighborTop = b.GetPathTo(_buttons[(i + _buttons.Count - 1) % _buttons.Count]);
            b.FocusNeighborBottom = b.GetPathTo(_buttons[(i + 1) % _buttons.Count]);
        }

        var version = PixelKit.Label(VersionText(), 13, PixelKit.TextDim);
        AddChild(version);
        version.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft, LayoutPresetMode.Minsize, 18);
        _author = PixelKit.Label(Signature, 13, PixelKit.TextDim, HorizontalAlignment.Right);
        _author.MouseFilter = MouseFilterEnum.Stop;
        _author.MouseDefaultCursorShape = CursorShape.PointingHand;
        _author.MouseEntered += () => _author.AddThemeColorOverride("font_color", PixelKit.AccentLight);
        _author.MouseExited += () => _author.AddThemeColorOverride("font_color", PixelKit.TextDim);
        _author.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _shell.OpenScreen("credits");
        };
        AddChild(_author);
        _author.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.Minsize, 18);

        Resized += Relayout;
        Relayout();
    }

    public override void _ExitTree() => FrontClock.Unregister(_titleMat);

    /// <summary>Menu buttons stop taking focus while a screen is open over the title.</summary>
    public void SetInteractive(bool on)
    {
        foreach (var b in _buttons) b.FocusMode = on ? FocusModeEnum.All : FocusModeEnum.None;
        _author.MouseFilter = on ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
    }

    /// <summary>Cold-start intro (§3.1): the title «stamps» down, the divider unrolls, the buttons pop in a ladder.</summary>
    public void PlayIntro(bool returning)
    {
        if (returning)
        {
            float d = 0;
            foreach (var b in _buttons) { _intro.Add(PixelKit.PopIn(b, d, .9f, .3f)); d += .05f; }
            return;
        }
        _intro.Add(PixelKit.PopIn(_title, .5f, 1.35f, .7f));
        PixelKit.KeepPivotCentered(_divider);
        _divider.Scale = new Vector2(0, 1);
        var t = _divider.CreateTween();
        t.TweenInterval(.95);
        t.TweenCallback(Callable.From(() => PixelKit.Sfx?.Invoke("stamp", 1f)));
        t.TweenInterval(.05);
        t.TweenProperty(_divider, "scale", Vector2.One, .6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        _intro.Add(t);
        float delay = 1.1f;
        foreach (var b in _buttons) { _intro.Add(PixelKit.PopIn(b, delay, .85f, .5f)); delay += .08f; }
    }

    public bool IntroPlaying
    {
        get
        {
            foreach (var t in _intro) if (t.IsValid() && t.IsRunning()) return true;
            return false;
        }
    }

    /// <summary>Any key or click during the intro: jump to the final state.</summary>
    public void FinishIntro()
    {
        foreach (var t in _intro) if (t.IsValid()) t.Kill();
        _intro.Clear();
        foreach (var c in new Control[] { _title, _divider }) { c.Modulate = new Color(c.Modulate, 1); c.Scale = Vector2.One; }
        foreach (var b in _buttons) { b.Modulate = new Color(b.Modulate, 1); b.Scale = Vector2.One; }
    }

    Button AddItem(string text, Action pressed)
    {
        var b = PixelKit.Button(text, "BigButton");
        b.CustomMinimumSize = new Vector2(340, 52);
        b.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        b.Pressed += pressed;
        Nav.FocusLooksLikeHover(b);
        _menu.AddChild(b);
        _buttons.Add(b);
        return b;
    }

    VBoxContainer Column(float top, float bottom, int separation)
    {
        var center = new CenterContainer { AnchorLeft = 0, AnchorRight = 1, AnchorTop = top, AnchorBottom = bottom, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", separation);
        center.AddChild(column);
        return column;
    }

    void Relayout()
    {
        bool compact = Size.Y < 800 || _buttons.Count >= 7;
        if (compact == _compact && _title.HasThemeFontSizeOverride("font_size") && _title.GetThemeFontSize("font_size") == (compact ? 66 : 88)) return;
        _compact = compact;
        int size = compact ? 66 : 88;
        _title.AddThemeFontSizeOverride("font_size", size);
        _title.AddThemeFontOverride("font", PixelKit.Spaced(compact ? 4 : 6));
        _titleMat.SetShaderParameter("snap", size / 11f);
        _menu.AddThemeConstantOverride("separation", compact ? 8 : 10);
        foreach (var b in _buttons) b.CustomMinimumSize = new Vector2(340, compact ? 48 : 52);
    }

    void UpdateTitleGradient()
    {
        var font = _title.GetThemeFont("font");
        int size = _title.GetThemeFontSize("font_size");
        float ascent = font.GetAscent(size), height = font.GetHeight(size);
        float baseline = (_title.Size.Y - height) * .5f + ascent;
        float cap = ascent * .72f;
        _titleMat.SetShaderParameter("text_top", baseline - cap);
        _titleMat.SetShaderParameter("text_height", cap);
        _titleMat.SetShaderParameter("travel", _title.Size.X + _title.Size.Y);
    }

    static string VersionText()
    {
        var v = ProjectSettings.GetSetting("application/config/version", "0.1").AsString();
        if (string.IsNullOrEmpty(v)) v = "0.1";
        if (v.EndsWith(".0") && v.Split('.').Length == 3) v = v[..^2];
        return $"v{v} · прототип";
    }
}
