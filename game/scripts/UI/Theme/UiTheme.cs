using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// Stylebox factories of the HUD's pixel components. Every call returns a fresh <see cref="Box"/> so callers may tweak it.
/// Cards: near-opaque graphite, 2px frame, hard 4px shadow, faint grain. Inner parts: 2px frames, no shadows.
/// </summary>
public static class St
{
    public const int Frame = 2;
    public const int CardShadow = 4;

    /// <summary>Floating card (panel, leaderboard, notes, strips): readable over the busy map.</summary>
    public static Box Card() => new Box().Fill(Pal.A(Pal.Card, .97f)).Border(Pal.Ln2).Shadow(CardShadow).Grain();

    /// <summary>Header band of a card: a Bayer-dithered light falling from the top, hairline underneath.</summary>
    public static Box Header(int band = 40) => new Box().Dither(Pal.Band, Pal.A(Pal.Card, 0), band).Border(Pal.Ln, 0, 0, 0, Frame);

    public static Box Row() => new Box().Fill(Pal.Surface).Border(Pal.Ln).Pad(5, 4, 10, 4);
    public static Box Slot() => new Box().Border(Pal.Ln3).Dashed().Pad(5, 4, 10, 4);
    public static Box SlotHover() => new Box().Fill(Pal.Surface).Border(Pal.Ac).Dashed().Pad(5, 4, 10, 4);
    /// <summary>Sunken square holding an icon.</summary>
    public static Box Tile(bool dashed = false) => dashed ? new Box().Border(Pal.Ln3).Dashed(2) : new Box().Fill(Pal.Well).Border(Pal.Ln);
    public static Box Tag() => new Box().Fill(Pal.Well).Border(Pal.Ln).Pad(7, 0, 7, 0);
    public static Box Stale() => new Box().Border(Pal.Ln3).Dashed(2).Pad(6, 0, 8, 0);
    public static Box Well() => new Box().Fill(Pal.Well).Border(Pal.Ln).Pad(6);
    /// <summary>One stat of the ledger grid: a sunken cell.</summary>
    public static Box Cell() => new Box().Fill(Pal.Well).Border(Pal.Ln).Pad(10, 7, 10, 8);
    /// <summary>Inverted fill — the active thing (PrimaryButton of the kit).</summary>
    public static Box Inverted() => new Box().Fill(Pal.Ac).Border(Pal.Hi);
    public static Box Empty() => new();
}

/// <summary>
/// The HUD Theme: the shared <see cref="PixelTheme"/> (Button / BigButton / PrimaryButton / GhostButton, toggles,
/// inputs, sliders, popups, scrollbars) plus the HUD's label roles and compact button variations.
/// </summary>
public static class UiTheme
{
    static Theme _theme;

    /// <summary>Forget the built theme (an era skin changed the colours).</summary>
    public static void Invalidate() => _theme = null;

    public static Theme Build()
    {
        if (_theme != null) return _theme;
        var t = new Theme();
        t.MergeWith(PixelTheme.Build());
        t.DefaultFont = UiFonts.Regular;
        t.DefaultFontSize = UiFonts.Body;

        // ---- labels ----
        t.SetColor("font_color", "Label", Pal.Tx);
        t.SetFont("font", "Label", UiFonts.Regular);
        t.SetFontSize("font_size", "Label", UiFonts.Body);
        t.SetConstant("line_spacing", "Label", 3);
        LabelRole(t, "Mu", UiFonts.Regular, UiFonts.Body, Pal.Mu);
        LabelRole(t, "Strong", UiFonts.Medium, UiFonts.Body, Pal.Tx);
        LabelRole(t, "Semi", UiFonts.Semi, UiFonts.Body, Pal.Hi);
        LabelRole(t, "SmallMu", UiFonts.Regular, UiFonts.Small, Pal.Mu);
        LabelRole(t, "Aside", UiFonts.Regular, UiFonts.Small, Pal.Mu);
        LabelRole(t, "Cap", UiFonts.Spaced(1, 600), UiFonts.Tiny, Pal.Sec);
        LabelRole(t, "Val", UiFonts.Semi, UiFonts.Value, Pal.Hi);
        LabelRole(t, "Delta", UiFonts.Medium, UiFonts.Small, Pal.Ok);
        LabelRole(t, "PanelTitle", UiFonts.Spaced(1), UiFonts.Title, Pal.Hi, shadow: 2);
        LabelRole(t, "Sub", UiFonts.Regular, UiFonts.Small, Pal.Mu);
        LabelRole(t, "H4", UiFonts.Spaced(2), UiFonts.Tiny, Pal.Ac);
        LabelRole(t, "NName", UiFonts.Spaced(1), UiFonts.Title, Pal.Hi, shadow: 2);
        LabelRole(t, "Date", UiFonts.Semi, UiFonts.Value, Pal.Hi, shadow: 1);
        LabelRole(t, "TipTitle", UiFonts.Bold, UiFonts.Value, Pal.Hi, shadow: 1);
        LabelRole(t, "LeadTitle", UiFonts.Spaced(2), UiFonts.Tiny, Pal.Ac);
        LabelRole(t, "Kick", UiFonts.Spaced(3), UiFonts.Tiny, Pal.Sec);
        LabelRole(t, "Big", UiFonts.Spaced(6), UiFonts.Huge, Pal.Hi, shadow: 6);

        // ---- buttons ----
        var disabled = Btn(Pal.DisabledFill, Pal.DisabledLine, 0);
        Skin(t, "Button", Btn(Pal.Surface, Pal.Ln2, 3), Btn(Pal.SurfaceHover, Pal.Ac, 3), Btn(Pal.Pressed, Pal.Ac, 0), disabled,
            Pal.Tx, Pal.Hi, UiFonts.Semi, UiFonts.Body, 12);
        Skin(t, "Pri", Btn(Pal.Ac, Pal.Hi, 3), Btn(Pal.Hi, Pal.Max, 3), Btn(Pal.AcPressed, Pal.Max, 0), disabled,
            Pal.OnAc, Pal.OnAc, UiFonts.Semi, UiFonts.Body, 12);
        Skin(t, "On", Btn(Pal.Ac, Pal.Hi, 3), Btn(Pal.Hi, Pal.Max, 3), Btn(Pal.AcPressed, Pal.Max, 0), disabled,
            Pal.OnAc, Pal.OnAc, UiFonts.Semi, UiFonts.Body, 12);
        Skin(t, "Sm", Btn(Pal.Surface, Pal.Ln2, 2), Btn(Pal.SurfaceHover, Pal.Ac, 2), Btn(Pal.Pressed, Pal.Ac, 0), disabled,
            Pal.Tx, Pal.Hi, UiFonts.Medium, UiFonts.Small, 9);
        Skin(t, "Menu", Btn(Pal.Surface, Pal.Ln, 0), Btn(Pal.SurfaceHover, Pal.Ln3, 0, accent: true), Btn(Pal.Pressed, Pal.Ac, 0), disabled,
            Pal.Tx, Pal.Hi, UiFonts.Medium, UiFonts.Small, 8);
        Skin(t, "Ghost", Btn(Pal.GhostFill, Pal.Ln, 2), Btn(Pal.Surface, Pal.Mu, 2), Btn(Pal.GhostPressed, Pal.Mu, 0), disabled,
            Pal.Mu, Pal.Tx, UiFonts.Medium, UiFonts.Small, 9);
        // icon buttons of the strips: quiet until hovered; the active one is inverted
        Skin(t, "Ib", Btn(Colors.Transparent, Colors.Transparent, 0), Btn(Pal.Surface, Pal.Ln2, 0), Btn(Pal.Pressed, Pal.Ac, 0), disabled,
            Pal.Sec, Pal.Hi, UiFonts.Medium, UiFonts.Small, 0);
        Skin(t, "IbOn", Btn(Pal.Ac, Pal.Hi, 2), Btn(Pal.Hi, Pal.Max, 2), Btn(Pal.AcPressed, Pal.Max, 0), disabled,
            Pal.OnAc, Pal.OnAc, UiFonts.Medium, UiFonts.Small, 0);
        // the top bar's square screen buttons (Seg*) and the zoom group (ZL/ZR) share one framed skin
        foreach (var name in new[] { "Seg", "SegStart", "SegEnd", "ZL", "ZR", "Pause" })
            Skin(t, name, Btn(Pal.Surface, Pal.Ln2, 2), Btn(Pal.SurfaceHover, Pal.Ac, 2), Btn(Pal.Pressed, Pal.Ac, 0), disabled,
                Pal.Tx, Pal.Hi, UiFonts.Semi, UiFonts.Small, 0);
        Skin(t, "SegEndOn", Btn(Pal.Ac, Pal.Hi, 2), Btn(Pal.Hi, Pal.Max, 2), Btn(Pal.AcPressed, Pal.Max, 0), disabled,
            Pal.OnAc, Pal.OnAc, UiFonts.Semi, UiFonts.Small, 0);
        Skin(t, "PauseRed", Btn(Pal.BadFill, Pal.Bad, 2), Btn(Pal.BadHover, Pal.BadTextHover, 2), Btn(Pal.BadPressed, Pal.Bad, 0), disabled,
            Pal.BadText, Pal.BadTextHover, UiFonts.Semi, UiFonts.Small, 0);
        Skin(t, "X", Btn(Colors.Transparent, Colors.Transparent, 0), Btn(Pal.Surface, Pal.Ln3, 0), Btn(Pal.Pressed, Pal.Ac, 0), disabled,
            Pal.Mu, Pal.Hi, UiFonts.Medium, UiFonts.Small, 0);
        Skin(t, "Session", Btn(Colors.Transparent, Colors.Transparent, 0), Btn(Pal.Surface, Pal.Ln2, 0), Btn(Pal.Pressed, Pal.Ln3, 0), disabled,
            Pal.Mu, Pal.Tx, UiFonts.Medium, UiFonts.Small, 8);

        // ---- containers ----
        t.SetStylebox("panel", "PanelContainer", St.Empty());
        t.SetStylebox("panel", "ScrollContainer", St.Empty());
        return _theme = t;
    }

    static void LabelRole(Theme t, string name, Font f, int size, Color c, int shadow = 0)
    {
        t.SetTypeVariation(name, "Label");
        t.SetFont("font", name, f);
        t.SetFontSize("font_size", name, size);
        t.SetColor("font_color", name, c);
        if (shadow <= 0) return;
        t.SetColor("font_shadow_color", name, Pal.Shadow);
        t.SetConstant("shadow_offset_x", name, shadow);
        t.SetConstant("shadow_offset_y", name, shadow);
        t.SetConstant("shadow_outline_size", name, 0);
    }

    /// <summary>A button state: 2px frame and a hard shadow of <paramref name="shadow"/> px (0 = pressed in).</summary>
    static Box Btn(Color fill, Color border, int shadow, bool accent = false)
    {
        var b = new Box().Fill(fill).Border(border);
        if (shadow > 0) b.Shadow(shadow);
        if (accent) b.AccentLeft(Pal.Ac, 3);
        return b;
    }

    static void Skin(Theme t, string name, Box normal, Box hover, Box pressed, Box disabled, Color font, Color hoverFont, Font f, int size, int padX)
    {
        if (name != "Button") t.SetTypeVariation(name, "Button");
        foreach (var b in new[] { normal, hover, pressed, disabled }) b.Pad(padX, 0);
        t.SetStylebox("normal", name, normal);
        t.SetStylebox("hover", name, hover);
        t.SetStylebox("pressed", name, pressed);
        t.SetStylebox("hover_pressed", name, pressed);
        t.SetStylebox("disabled", name, disabled);
        t.SetStylebox("focus", name, new StyleBoxEmpty());
        t.SetFont("font", name, f);
        t.SetFontSize("font_size", name, size);
        t.SetConstant("h_separation", name, 6);
        foreach (var k in new[] { "font_color", "font_focus_color" }) t.SetColor(k, name, font);
        foreach (var k in new[] { "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) t.SetColor(k, name, hoverFont);
        t.SetColor("font_disabled_color", name, Pal.Mu2);
        foreach (var k in new[] { "icon_normal_color", "icon_focus_color" }) t.SetColor(k, name, font);
        foreach (var k in new[] { "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) t.SetColor(k, name, hoverFont);
        t.SetColor("icon_disabled_color", name, Pal.Mu2);
    }
}
