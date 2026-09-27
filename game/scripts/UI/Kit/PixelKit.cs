using System;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// Pixel style kit shared by the HUD and the front-end screens — a C# port of the «Mr. President» ui_kit.gd
/// (the user's reference style): near-black/graphite palette where importance is shown by brightness, the
/// PixelifySansMrP font without antialiasing, square 2px frames, hard offset shadows and small tween motions.
/// </summary>
public static class PixelKit
{
    // ---- palette ----
    public static readonly Color Bg = Color.FromHtml("#0e0e10");
    public static readonly Color Panel = Color.FromHtml("#17171a");
    public static readonly Color PanelBorder = Color.FromHtml("#3a3a40");
    public static readonly Color Surface = Color.FromHtml("#202024");
    public static readonly Color SurfaceHover = Color.FromHtml("#2c2c31");
    public static readonly Color Text = Color.FromHtml("#d8d8da");
    public static readonly Color TextDim = Color.FromHtml("#8f8f94");
    public static readonly Color TextMuted = Color.FromHtml("#5c5c62");
    /// <summary>Main accent: light gray — important things stand out by brightness, not colour.</summary>
    public static readonly Color Accent = Color.FromHtml("#c8c8cc");
    public static readonly Color AccentLight = Color.FromHtml("#f0f0f2");
    public static readonly Color AccentDark = Color.FromHtml("#6b6b70");
    public static readonly Color Secondary = Color.FromHtml("#a3a3a8");
    public static readonly Color Ink = Color.FromHtml("#0b0b0c");
    // statuses: barely tinted grays so they don't cut the eye
    public static readonly Color Good = Color.FromHtml("#8aa88a");
    public static readonly Color Bad = Color.FromHtml("#b07070");
    public static readonly Color Info = Color.FromHtml("#8a93a8");
    public static readonly Color Warn = Color.FromHtml("#bdbdb4");
    public static readonly Color Shadow = new(0, 0, 0, 0.7f);
    public static readonly Vector2 ShadowOffset = new(4, 4);

    /// <summary>Optional UI sound hook: (name, pitch) — "hover", "click". Set by the audio module when it exists.</summary>
    public static Action<string, float> Sfx;

    // ---- font ----
    public const string FontPath = "res://assets/fonts/PixelifySansMrP.ttf";
    static Font _font;
    static readonly System.Collections.Generic.Dictionary<(int, int), FontVariation> _variations = new();

    /// <summary>The pixel font with antialiasing, hinting and subpixel positioning off (crisp at integer sizes).</summary>
    public static Font LoadFont()
    {
        if (_font != null) return _font;
        var file = ResourceLoader.Exists(FontPath) ? GD.Load<FontFile>(FontPath) : null;
        if (file == null)
        {
            file = new FontFile();
            if (file.LoadDynamicFont(ProjectSettings.GlobalizePath(FontPath)) != Error.Ok) file = null;
        }
        if (file == null) { GD.PushWarning($"PixelKit: font {FontPath} not found"); return _font = ThemeDB.FallbackFont; }
        file.Antialiasing = TextServer.FontAntialiasing.None;
        file.Hinting = TextServer.Hinting.None;
        file.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        return _font = file;
    }

    /// <summary>Body text of the given weight (400–700). Variations are cached and shared — never mutate them.</summary>
    public static FontVariation Body(int weight = 400) => Variation(weight, 0);

    /// <summary>Heading font: bold pixel font with extra glyph spacing.</summary>
    public static FontVariation Spaced(int spacing) => Variation(700, spacing);

    /// <summary>Any weight (400–700) with extra glyph spacing (px). Cached and shared — never mutate.</summary>
    public static FontVariation Variant(int weight, int spacing) => Variation(weight, spacing);

    static FontVariation Variation(int weight, int spacing)
    {
        if (_variations.TryGetValue((weight, spacing), out var v)) return v;
        v = new FontVariation { BaseFont = LoadFont(), SpacingGlyph = spacing };
        v.VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } };
        _variations[(weight, spacing)] = v;
        return v;
    }

    // ---- style boxes ----
    /// <summary>Flat box: square corners, no antialiasing; border drawn only if its alpha &gt; 0.</summary>
    public static StyleBoxFlat Box(Color fill, Color? border = null, int borderWidth = 2)
    {
        var b = border ?? Colors.Transparent;
        var s = new StyleBoxFlat { BgColor = fill, BorderColor = b, AntiAliasing = false };
        s.SetBorderWidthAll(b.A > 0 ? borderWidth : 0);
        s.SetCornerRadiusAll(0);
        return s;
    }

    /// <summary>Framed panel: fill, border and a hard offset shadow (no blur).</summary>
    public static StyleBoxFlat Framed(Color fill, Color border, int borderWidth = 2, bool shadow = true)
    {
        var s = Box(fill, border, borderWidth);
        if (shadow) { s.ShadowColor = Shadow; s.ShadowSize = 1; s.ShadowOffset = ShadowOffset; }
        return s;
    }

    public static T Padded<T>(T s, float horizontal, float vertical) where T : StyleBox
    {
        s.ContentMarginLeft = s.ContentMarginRight = horizontal;
        s.ContentMarginTop = s.ContentMarginBottom = vertical;
        return s;
    }

    // ---- small factories ----
    public static Label Label(string text, int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Text);
        return l;
    }

    public static Label Paragraph(string text, int size = 15, Color? color = null)
    {
        var l = Label(text, size, color ?? TextDim);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    /// <summary>Small spaced caption above a value or a section.</summary>
    public static Label Kicker(string text, Color? color = null, int size = 12)
    {
        var l = Label(text, size, color ?? Secondary);
        l.AddThemeFontOverride("font", Spaced(3));
        return l;
    }

    /// <summary>Hard pixel drop shadow under a label (offset 2px × strength).</summary>
    public static Label TextShadow(Label l, float strength = 1f)
    {
        int o = Math.Max(1, (int)MathF.Round(2 * strength));
        l.AddThemeColorOverride("font_shadow_color", Shadow);
        l.AddThemeConstantOverride("shadow_offset_x", o);
        l.AddThemeConstantOverride("shadow_offset_y", o);
        l.AddThemeConstantOverride("shadow_outline_size", 0);
        return l;
    }

    public static ColorRect Divider(float width = 120, Color? color = null) => new()
    {
        Color = color ?? Accent, CustomMinimumSize = new Vector2(width, 4),
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static PanelContainer Badge(string text, Color fill, Color fontColor, int size = 12)
    {
        var p = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        p.AddThemeStyleboxOverride("panel", Padded(Box(fill), 10, 3));
        var l = Label(text, size, fontColor);
        l.AddThemeFontOverride("font", Spaced(1));
        p.AddChild(l);
        return p;
    }

    /// <summary>Button with the kit's press motion. Variations: "", "BigButton", "PrimaryButton", "GhostButton".</summary>
    public static Button Button(string text, string variation = "")
    {
        var b = new Button { Text = text, ThemeTypeVariation = variation, CustomMinimumSize = new Vector2(0, 46) };
        AddPressMotion(b);
        return b;
    }

    // ---- motion ----
    public static void KeepPivotCentered(Control c)
    {
        if (c.HasMeta("pivot_centered")) return;
        c.SetMeta("pivot_centered", true);
        c.Resized += () => c.PivotOffset = c.Size * 0.5f;
        c.PivotOffset = c.Size * 0.5f;
    }

    /// <summary>Slight springy grow on hover, squash on press (as in Mr. President).</summary>
    public static void AddPressMotion(BaseButton b)
    {
        KeepPivotCentered(b);
        b.MouseEntered += () => { if (!b.Disabled) { TweenScale(b, 1.03f, .16f); Sfx?.Invoke("hover", (float)GD.RandRange(.97, 1.03)); } };
        b.Pressed += () => Sfx?.Invoke("click", (float)GD.RandRange(.96, 1.04));
        b.MouseExited += () => TweenScale(b, 1f, .2f);
        b.ButtonDown += () => TweenScale(b, .96f, .08f);
        b.ButtonUp += () =>
        {
            if (!b.IsInsideTree()) return;   // the press may have rebuilt the list
            bool hovered = b.GetGlobalRect().HasPoint(b.GetGlobalMousePosition());
            TweenScale(b, hovered ? 1.03f : 1f, .2f);
        };
    }

    public static void TweenScale(Control c, float target, float duration)
    {
        if (!c.IsInsideTree()) return;
        if (c.HasMeta("scale_tween") && c.GetMeta("scale_tween").AsGodotObject() is Tween old && old.IsValid()) old.Kill();
        var t = c.CreateTween();
        t.TweenProperty(c, "scale", Vector2.One * target, duration).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        c.SetMeta("scale_tween", t);
    }

    /// <summary>Appear: fade + scale (scale is not reset by containers, unlike position).</summary>
    public static Tween PopIn(Control c, float delay = 0, float fromScale = .92f, float duration = .35f)
    {
        KeepPivotCentered(c);
        c.Modulate = new Color(c.Modulate, 0);
        c.Scale = Vector2.One * fromScale;
        var t = c.CreateTween().SetParallel(true);
        t.TweenProperty(c, "modulate:a", 1f, duration * .7f).SetDelay(delay).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        t.TweenProperty(c, "scale", Vector2.One, duration).SetDelay(delay).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        return t;
    }

    public static void Bump(Control c, float amount = 1.08f, float duration = .18f)
    {
        KeepPivotCentered(c);
        c.Scale = Vector2.One * amount;
        TweenScale(c, 1f, duration);
    }

    public static Tween Pulse(Control c, float strength = .15f, float period = 1.4f)
    {
        var bright = new Color(1 + strength, 1 + strength, 1 + strength);
        var t = c.CreateTween().SetLoops();
        t.TweenProperty(c, "modulate", bright, period * .5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        t.TweenProperty(c, "modulate", Colors.White, period * .5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        return t;
    }

    public static void Shake(Control c, float strength = 10)
    {
        float x0 = c.Position.X;
        var t = c.CreateTween();
        foreach (float o in new[] { strength, -strength, strength * .6f, -strength * .6f, strength * .25f, 0f })
            t.TweenProperty(c, "position:x", x0 + o, .05f);
    }
}
