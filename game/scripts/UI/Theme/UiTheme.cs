using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// Stylebox factories for the design's components (the CSS classes of design_final/css/style.css).
/// Every call returns a fresh Box so callers may tweak it.
/// </summary>
public static class St
{
    public const int R = 4;     // controls, rows, chips
    public const int RCard = 6; // floating cards

    /// <summary>
    /// .pn — floating white card with the layered slate shadow. Opaque: the CSS card is 96.5% white over a 14px
    /// backdrop blur; without the blur the 3.5% see-through only shows as ghost text.
    /// </summary>
    public static Box Card(int radius = RCard) => new Box().Fill(Pal.P1s).Border(Pal.Ln2).Radius(radius)
        .Shadow(Pal.Shade(.07f), 22, 9).Shadow(Pal.Shade(.09f), 8, 4).Shadow(Pal.Shade(.10f), 1, 1);

    /// <summary>.ph / .lh — header gradient with a bottom hairline and an optional 3px owner-colour accent.</summary>
    public static Box Header(int topRadius = RCard - 1) => new Box().Fill(Pal.Hd1, Pal.Hd2).Border(Pal.Ln2, 0, 0, 0, 1)
        .Radius(topRadius, topRadius, 0, 0).Accent(Colors.Transparent);

    /// <summary>.bl — white row with a hairline.</summary>
    public static Box Row() => new Box().Fill(Pal.P2).Border(Pal.Ln).Radius(R).Shadow(Pal.Shade(.06f), 1, 1).Pad(5, 4, 10, 4);
    public static Box Slot() => new Box().Fill(Pal.White(.4f)).Border(Pal.Ln3).Dashed().Radius(R).Pad(5, 4, 10, 4);
    public static Box SlotHover() => new Box().Fill(Pal.P2).Border(Pal.Ac).Dashed().Radius(R).Pad(5, 4, 10, 4);
    /// <summary>Sunken icon tile inside rows and notes.</summary>
    public static Box Tile(bool dashed = false, int radius = 3)
    {
        var b = new Box().Fill(dashed ? Colors.Transparent : Pal.P3).Border(dashed ? Pal.Ln2 : Pal.Ln).Radius(radius);
        return dashed ? b.Dashed() : b;
    }
    public static Box Tag() => new Box().Fill(Pal.P3).Border(Pal.Ln).Radius(R).Pad(8, 0, 8, 0);
    public static Box Chip() => new Box().Fill(Pal.P2).Border(Pal.Ln2).Radius(R).Shadow(Pal.Shade(.06f), 1, 1).Pad(7, 0, 9, 0);
    public static Box Stale() => new Box().Fill(Pal.P3).Border(Pal.Ln3).Dashed().Radius(R).Pad(6, 0, 9, 0);
    public static Box Well() => new Box().Fill(Pal.P3).Border(Pal.Ln).Radius(R).Pad(6);
    /// <summary>.grid2 frame: the hairline colour shows through the 1px gaps between white cells.</summary>
    public static Box GridFrame() => new Box().Fill(Pal.Ln).Border(Pal.Ln2).Radius(R).Shadow(Pal.Shade(.06f), 1, 1).Pad(1);
    public static Box Cell(int tl, int tr, int br, int bl) => new Box().Fill(Pal.P2).Radius(tl, tr, br, bl).Pad(11, 8, 11, 9);
    public static Box Graphite(int radius = R) => new Box().Fill(Pal.G1, Pal.G2).Border(Pal.GEdge).Radius(radius)
        .Shadow(Pal.Shade(.25f), 2, 1).Rule(1, 1, Pal.White(.08f));
    public static Box Empty() => new Box();
}

/// <summary>Builds the Godot Theme: default fonts, label roles, button skins, scrollbars.</summary>
public static class UiTheme
{
    public static Theme Build()
    {
        UiFonts.Init();
        var t = new Theme { DefaultFont = UiFonts.Fu400, DefaultFontSize = 13 };

        // ---- labels ----
        t.SetColor("font_color", "Label", Pal.Tx);
        t.SetConstant("line_spacing", "Label", 2);
        LabelRole(t, "Mu", UiFonts.Fu400, 13, Pal.Mu);
        LabelRole(t, "Strong", UiFonts.Fu500, 13, Pal.Tx);
        LabelRole(t, "Semi", UiFonts.Fu600, 13, Pal.Tx);
        LabelRole(t, "SmallMu", UiFonts.Fu400, 12, Pal.Mu);
        LabelRole(t, "Aside", UiFonts.Fu500, 12, Pal.Mu);
        LabelRole(t, "Cap", UiFonts.Track(UiFonts.Fu500, 1), 11, Pal.Mu);
        LabelRole(t, "Val", UiFonts.Fu600, 17, Pal.Tx);
        LabelRole(t, "Delta", UiFonts.Fu500, 12, Pal.Ok);
        LabelRole(t, "Chip", UiFonts.Fu600, 12, Pal.Tx);
        LabelRole(t, "PanelTitle", UiFonts.Fd700, 23, Pal.Tx);
        LabelRole(t, "Sub", UiFonts.Fu400, 12, Pal.Mu);
        LabelRole(t, "H4", UiFonts.Track(UiFonts.Fd700, 1), 15, Pal.Tx);
        LabelRole(t, "NName", UiFonts.Fd700, 20, Pal.Tx);
        LabelRole(t, "Date", UiFonts.Fd700, 17, Pal.Tx);
        LabelRole(t, "TipTitle", UiFonts.Fd700, 16, Pal.Tx);
        LabelRole(t, "LeadTitle", UiFonts.Fd700, 16, Pal.Tx);
        LabelRole(t, "Kick", UiFonts.Track(UiFonts.Fu500, 4), 12, Pal.Mu);
        LabelRole(t, "Big", UiFonts.Track(UiFonts.Fd800, 8), 58, Pal.Tx);

        // ---- buttons ----
        t.SetFont("font", "Button", UiFonts.Fu500);
        t.SetFontSize("font_size", "Button", 13);
        t.SetConstant("h_separation", "Button", 6);
        ButtonSkin(t, "Button",
            new Box().Fill(Pal.Btn1, Pal.Btn2).Border(Pal.Ln2).Radius(St.R).Shadow(Pal.Shade(.06f), 1, 1),
            new Box().Fill(Pal.P2).Border(Pal.Ln3).Radius(St.R).Shadow(Pal.Shade(.12f), 3, 1),
            new Box().Fill(Pal.BtnPress).Border(Pal.Ln3).Radius(St.R).Rule(0, 1, Pal.Shade(.08f)),
            Pal.Tx, Pal.Tx2, 12);
        ButtonSkin(t, "Sm", Skin(St.R), SkinHover(St.R), SkinPress(St.R), Pal.Tx, Pal.Tx2, 9, 12);
        ButtonSkin(t, "ZL", Skin(St.R, 0, 0, St.R), SkinHover(St.R, 0, 0, St.R), SkinPress(St.R, 0, 0, St.R), Pal.Tx, Pal.Tx2, 0, 13);
        ButtonSkin(t, "ZR", Skin(0, St.R, St.R, 0), SkinHover(0, St.R, St.R, 0), SkinPress(0, St.R, St.R, 0), Pal.Tx, Pal.Tx2, 0, 13);
        ButtonSkin(t, "Pri", St.Graphite(),
            new Box().Fill(Pal.GHover1, Pal.GHover2).Border(Pal.GEdge).Radius(St.R).Shadow(Pal.Shade(.28f), 3, 1).Rule(1, 1, Pal.White(.08f)),
            new Box().Fill(Pal.G2).Border(Pal.GEdge).Radius(St.R), Colors.White, Colors.White, 12);
        ButtonSkin(t, "On", St.Graphite(), St.Graphite(), new Box().Fill(Pal.G2).Border(Pal.GEdge).Radius(St.R), Colors.White, Colors.White, 12);
        ButtonSkin(t, "Ib", St.Empty().Radius(St.R), new Box().Fill(Pal.IbHover).Radius(St.R), new Box().Fill(Pal.IbPress).Radius(St.R), Pal.Tx, Pal.Tx2, 0);
        ButtonSkin(t, "IbOn", IbOn(St.R), IbOn(St.R), IbOn(St.R), Colors.White, Colors.White, 0);
        // the top-bar screens group: square segments, the ends follow the group's rounded outline
        ButtonSkin(t, "Seg", St.Empty(), new Box().Fill(Pal.IbHover), new Box().Fill(Pal.IbPress), Pal.Tx, Pal.Tx2, 0);
        ButtonSkin(t, "SegStart", St.Empty(), new Box().Fill(Pal.IbHover).Radius(3, 0, 0, 3), new Box().Fill(Pal.IbPress).Radius(3, 0, 0, 3), Pal.Tx, Pal.Tx2, 0);
        ButtonSkin(t, "SegEnd", St.Empty(), new Box().Fill(Pal.IbHover).Radius(0, 3, 3, 0), new Box().Fill(Pal.IbPress).Radius(0, 3, 3, 0), Pal.Tx, Pal.Tx2, 0);
        ButtonSkin(t, "SegEndOn", SegOn(), SegOn(), SegOn(), Colors.White, Colors.White, 0);
        ButtonSkin(t, "X", St.Empty(), new Box().Fill(Pal.P2).Border(Pal.Ln2).Radius(St.R), new Box().Fill(Pal.BtnPress).Border(Pal.Ln2).Radius(St.R), Pal.Mu, Pal.Mu, 0);
        t.SetColor("icon_hover_color", "X", Pal.Tx);
        ButtonSkin(t, "Session", St.Empty(), new Box().Fill(Pal.IbHover).Radius(St.R), new Box().Fill(Pal.IbPress).Radius(St.R), Pal.Mu, Pal.Mu, 10);
        ButtonSkin(t, "Pause", Round(Pal.G1, Pal.G2), Round(Pal.Hex(0x4a515a), Pal.GHover2), Round(Pal.G2, Pal.G2), Colors.White, Colors.White, 0);
        ButtonSkin(t, "PauseRed", Round(Pal.Red1, Pal.Red2), Round(Pal.Hex(0xcc5446), Pal.Hex(0xa9392e)), Round(Pal.Red2, Pal.Red2), Colors.White, Colors.White, 0);
        ButtonSkin(t, "Menu", Skin(St.R), SkinHover(St.R), SkinPress(St.R), Pal.Tx, Pal.Tx2, 8, 12);

        // ---- containers / scrollbars ----
        t.SetStylebox("panel", "PanelContainer", St.Empty());
        t.SetStylebox("panel", "ScrollContainer", St.Empty());
        var track = new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4 };
        t.SetStylebox("scroll", "VScrollBar", track);
        t.SetStylebox("scroll_focus", "VScrollBar", track);
        t.SetStylebox("grabber", "VScrollBar", Thumb(Pal.Hex(0xc3c9cf)));
        t.SetStylebox("grabber_highlight", "VScrollBar", Thumb(Pal.Hex(0xa3aab2)));
        t.SetStylebox("grabber_pressed", "VScrollBar", Thumb(Pal.Hex(0xa3aab2)));
        return t;
    }

    static void LabelRole(Theme t, string name, Font f, int size, Color c)
    {
        t.SetTypeVariation(name, "Label");
        t.SetFont("font", name, f);
        t.SetFontSize("font_size", name, size);
        t.SetColor("font_color", name, c);
    }

    static void ButtonSkin(Theme t, string name, Box normal, Box hover, Box pressed, Color font, Color icon, int padX, int fontSize = 0)
    {
        if (name != "Button") t.SetTypeVariation(name, "Button");
        foreach (var b in new[] { normal, hover, pressed }) { b.ContentMarginLeft = padX; b.ContentMarginRight = padX; }
        t.SetStylebox("normal", name, normal);
        t.SetStylebox("hover", name, hover);
        t.SetStylebox("pressed", name, pressed);
        t.SetStylebox("hover_pressed", name, pressed);
        t.SetStylebox("disabled", name, normal);
        t.SetStylebox("focus", name, new StyleBoxEmpty());
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "font_disabled_color" })
            t.SetColor(k, name, font);
        foreach (var k in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color", "icon_disabled_color" })
            t.SetColor(k, name, icon);
        if (fontSize > 0) t.SetFontSize("font_size", name, fontSize);
    }

    static Box Skin(int tl, int tr = -1, int br = -1, int bl = -1) =>
        new Box().Fill(Pal.Btn1, Pal.Btn2).Border(Pal.Ln2).Radius(tl, tr < 0 ? tl : tr, br < 0 ? tl : br, bl < 0 ? tl : bl).Shadow(Pal.Shade(.06f), 1, 1);
    static Box SkinHover(int tl, int tr = -1, int br = -1, int bl = -1) =>
        new Box().Fill(Pal.P2).Border(Pal.Ln3).Radius(tl, tr < 0 ? tl : tr, br < 0 ? tl : br, bl < 0 ? tl : bl).Shadow(Pal.Shade(.12f), 3, 1);
    static Box SkinPress(int tl, int tr = -1, int br = -1, int bl = -1) =>
        new Box().Fill(Pal.BtnPress).Border(Pal.Ln3).Radius(tl, tr < 0 ? tl : tr, br < 0 ? tl : br, bl < 0 ? tl : bl).Rule(0, 1, Pal.Shade(.08f));
    static Box IbOn(int r) => new Box().Fill(Pal.G1, Pal.G2).Border(Pal.GEdge).Radius(r).Shadow(Pal.Shade(.3f), 2, 1).Rule(1, 1, Pal.White(.08f));
    static Box SegOn() => new Box().Fill(Pal.G1, Pal.G2).Radius(0, 3, 3, 0);
    static Box Round(Color top, Color bottom) => new Box().Fill(top, bottom).Radius(16).Shadow(Pal.Shade(.3f), 2, 1);
    static StyleBoxFlat Thumb(Color c) => new()
    {
        BgColor = c, BorderColor = new Color(c, 0), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2, ContentMarginLeft = 4, ContentMarginRight = 4,
    };
}
