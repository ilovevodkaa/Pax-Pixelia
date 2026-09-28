using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core.Save;

namespace PaxPixelia.UI.Front;

/// <summary>
/// The title screen layer (MAIN_MENU.md §3.1): the «PAX PIXELIA» pixel logo (stepped gradient, diagonal shimmer,
/// hard shadow — no slogan), the BigButton column and the corner captions. Sizes switch to the compact set below
/// 800 px of height or with seven items (logo 66, buttons 340×48 with step 8). With saves (F-4) «Продолжить» leads the
/// column with a second line «Ардания · 880 до н. э. · 2 ч 14 мин · вчера 23:40» and takes the default focus;
/// «Загрузить» is inactive until a save exists.
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
    /// <summary>«Продолжить»: visible while a loadable save exists.</summary>
    public Button ContinueButton { get; private set; }
    public Button LoadButton { get; private set; }
    public IReadOnlyList<Button> Buttons => _buttons;
    /// <summary>The button that takes the focus on the title: «Продолжить» when there is a save, else «Новая игра».</summary>
    public Button DefaultButton => ContinueButton is { Visible: true } c ? c : NewGameButton;
    /// <summary>The ↑/↓ ring: visible, active buttons in column order.</summary>
    public List<Button> FocusRing => _buttons.FindAll(b => b.Visible && !b.Disabled);
    /// <summary>The newest loadable save («Продолжить»), or null.</summary>
    public SaveEntry Latest { get; private set; }
    /// <summary>A title button by its caption, any case (tests).</summary>
    public Button Find(string text) => string.Equals(text, "Продолжить", System.StringComparison.OrdinalIgnoreCase)
        ? ContinueButton : _buttons.Find(b => string.Equals(b.Text, text, System.StringComparison.OrdinalIgnoreCase));
    Label _continueTitle, _continueMeta;
    bool _interactive = true;
    int _layout = -1;

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

        ContinueButton = AddContinue();
        NewGameButton = AddItem("НОВАЯ ИГРА", () => _shell.OpenScreen("newgame"));
        LoadButton = AddItem("ЗАГРУЗИТЬ", () => _shell.OpenScreen("load"));
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

        RefreshSaves();
        Resized += Relayout;
        Relayout();
    }

    public override void _ExitTree() => FrontClock.Unregister(_titleMat);

    /// <summary>Menu buttons stop taking focus while a screen is open over the title.</summary>
    public void SetInteractive(bool on)
    {
        _interactive = on;
        ApplyFocusModes();
        _author.MouseFilter = on ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
    }

    /// <summary>Only visible, active buttons take the focus, and only while no screen is open over the title.</summary>
    void ApplyFocusModes()
    {
        foreach (var b in _buttons) b.FocusMode = _interactive && b.Visible && !b.Disabled ? FocusModeEnum.All : FocusModeEnum.None;
    }

    /// <summary>Re-read the saves folder: «Продолжить» shows the newest loadable save (or hides), «Загрузить» is active
    /// while any save file exists; the ↑/↓ ring skips what is hidden or inactive.</summary>
    public void RefreshSaves()
    {
        Latest = SaveStore.Latest();
        ContinueButton.Visible = Latest != null;
        if (Latest != null)
        {
            var meta = SaveText.Meta(Latest.Header);
            var font = _continueMeta.GetThemeFont("font");
            int size = _continueMeta.GetThemeFontSize("font_size");
            if (font != null && font.GetStringSize(meta, HorizontalAlignment.Left, -1, size).X > ContinueButton.CustomMinimumSize.X - 28)
                meta = SaveText.Meta(Latest.Header, withWhen: false);
            _continueMeta.Text = meta;
            ContinueButton.TooltipText = $"{SaveText.Title(Latest.Header)} · сохранено {SaveText.When(Latest.Header.SavedUnixMs)}";
        }
        bool any = SaveStore.Any();
        LoadButton.Disabled = !any;
        LoadButton.TooltipText = any ? "" : "Сохранений пока нет";
        LoadButton.MouseDefaultCursorShape = any ? CursorShape.PointingHand : CursorShape.Arrow;
        var ring = FocusRing;
        for (int i = 0; i < ring.Count; i++)
        {
            // wrap around the column with ↑/↓
            var b = ring[i];
            b.FocusNeighborTop = b.GetPathTo(ring[(i + ring.Count - 1) % ring.Count]);
            b.FocusNeighborBottom = b.GetPathTo(ring[(i + 1) % ring.Count]);
        }
        ApplyFocusModes();
        if (IsInsideTree()) Relayout();
    }

    /// <summary>«Продолжить» with its second line (nation · date · playtime · when): two labels over an empty BigButton.</summary>
    Button AddContinue()
    {
        var b = AddItem("", () => { if (Latest != null) _shell.LoadSave(Latest); });
        b.Name = "Continue";
        _continueTitle = PixelKit.Label("ПРОДОЛЖИТЬ", 22, PixelKit.Text, HorizontalAlignment.Center);
        _continueTitle.AddThemeFontOverride("font", b.GetThemeFont("font") ?? PixelKit.Spaced(2));
        _continueMeta = PixelKit.Label("", 13, PixelKit.TextDim, HorizontalAlignment.Center);
        _continueMeta.ClipText = true;
        _continueMeta.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 0);
        col.AddChild(_continueTitle);
        col.AddChild(_continueMeta);
        foreach (var n in col.GetChildren()) ((Control)n).MouseFilter = MouseFilterEnum.Ignore;
        b.AddChild(col);
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 14; col.OffsetRight = -14;
        b.FocusEntered += () => { _continueTitle.AddThemeColorOverride("font_color", PixelKit.AccentLight); _continueMeta.AddThemeColorOverride("font_color", PixelKit.Secondary); };
        b.FocusExited += () => { _continueTitle.AddThemeColorOverride("font_color", PixelKit.Text); _continueMeta.AddThemeColorOverride("font_color", PixelKit.TextDim); };
        return b;
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
        int visible = _buttons.FindAll(b => b.Visible).Count;
        bool compact = Size.Y < 800 || visible >= 7;
        int btn = compact ? 48 : 52, gap = compact ? 8 : 10, cont = compact ? 58 : 64;
        bool withContinue = ContinueButton.Visible;
        float need = (visible - (withContinue ? 1 : 0)) * btn + (withContinue ? cont : 0) + (visible - 1) * gap;
        // seven items on a short window (1024×600, 1280×720): tighter buttons and the column moves up under the logo
        bool tight = Size.Y > 0 && need > Size.Y * (.84f - .38f) + 64;
        if (tight) { btn = 40; gap = 6; cont = 50; }
        int layout = (compact ? 1 : 0) | (tight ? 2 : 0) | visible << 2;
        if (layout == _layout && _title.HasThemeFontSizeOverride("font_size")) return;
        _layout = layout;
        _compact = compact;
        int size = compact ? 66 : 88;
        _title.AddThemeFontSizeOverride("font_size", size);
        _title.AddThemeFontOverride("font", PixelKit.Spaced(compact ? 4 : 6));
        _titleMat.SetShaderParameter("snap", size / 11f);
        _menu.AddThemeConstantOverride("separation", gap);
        foreach (var b in _buttons) b.CustomMinimumSize = new Vector2(340, btn);
        ContinueButton.CustomMinimumSize = new Vector2(340, cont);
        if (_logo.GetParent() is Control logoBand) { logoBand.AnchorTop = tight ? .03f : .06f; logoBand.AnchorBottom = tight ? .28f : .36f; }
        if (_menu.GetParent() is Control menuBand) { menuBand.AnchorTop = tight ? .29f : .38f; menuBand.AnchorBottom = tight ? .92f : .84f; }
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
