using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// The game-wide pixel Theme (port of «Mr. President» ui_theme.gd). Assign it to a root Control and every Button,
/// slider, toggle, input, popup and scrollbar below inherits the style. Type variations: BigButton (menu buttons,
/// 8px left accent bar on hover), PrimaryButton (inverted: light fill, dark text), GhostButton (quiet).
/// </summary>
public static class PixelTheme
{
    static Theme _theme;

    public static Theme Build()
    {
        if (_theme != null) return _theme;
        var t = new Theme { DefaultFont = PixelKit.Body(400), DefaultFontSize = 18 };
        t.SetColor("font_color", "Label", PixelKit.Text);
        Buttons(t);
        Toggles(t);
        Inputs(t);
        Sliders(t);
        Popups(t);
        Scrollbars(t);
        t.SetStylebox("panel", "PanelContainer", PixelKit.Framed(PixelKit.Panel, PixelKit.PanelBorder));
        t.SetStylebox("panel", "TooltipPanel", PixelKit.Padded(PixelKit.Framed(PixelKit.Panel, PixelKit.PanelBorder), 10, 6));
        t.SetColor("font_color", "TooltipLabel", PixelKit.Text);
        return _theme = t;
    }

    /// <summary>Button box: square frame + hard shadow; pressed = shorter shadow (the button is «pushed in»).</summary>
    static StyleBoxFlat ButtonBox(Color fill, Color border, int shadow = 4, bool accentLeft = false)
    {
        var s = PixelKit.Box(fill, border);
        if (shadow > 0) { s.ShadowColor = PixelKit.Shadow; s.ShadowSize = 1; s.ShadowOffset = new Vector2(shadow, shadow); }
        if (accentLeft) s.BorderWidthLeft = 8;
        PixelKit.Padded(s, 20, 8);
        return s;
    }

    record ButtonColors(Color Fill, Color Border, Color HoverFill, Color HoverBorder, Color PressedFill, Color Font, Color HoverFont, bool AccentLeft = false);

    static void ButtonType(Theme t, string type, ButtonColors c)
    {
        t.SetStylebox("normal", type, ButtonBox(c.Fill, c.Border, 4));
        t.SetStylebox("hover", type, ButtonBox(c.HoverFill, c.HoverBorder, 4, c.AccentLeft));
        t.SetStylebox("pressed", type, ButtonBox(c.PressedFill, c.HoverBorder, 1, c.AccentLeft));
        t.SetStylebox("hover_pressed", type, ButtonBox(c.PressedFill, c.HoverBorder, 1, c.AccentLeft));
        t.SetStylebox("disabled", type, ButtonBox(Color.FromHtml("#141416"), Color.FromHtml("#26262a"), 0));
        var focus = PixelKit.Box(Colors.Transparent, PixelKit.AccentLight);
        focus.DrawCenter = false;
        focus.SetExpandMarginAll(4);
        t.SetStylebox("focus", type, focus);
        t.SetColor("font_color", type, c.Font);
        t.SetColor("font_focus_color", type, c.Font);
        t.SetColor("font_hover_color", type, c.HoverFont);
        t.SetColor("font_pressed_color", type, c.HoverFont);
        t.SetColor("font_hover_pressed_color", type, c.HoverFont);
        t.SetColor("font_disabled_color", type, PixelKit.TextMuted);
        t.SetFont("font", type, PixelKit.Body(600));
    }

    static void Buttons(Theme t)
    {
        var pressed = Color.FromHtml("#151517");
        ButtonType(t, "Button", new(PixelKit.Surface, PixelKit.PanelBorder, PixelKit.SurfaceHover, PixelKit.Accent, pressed, PixelKit.Text, PixelKit.AccentLight));

        t.SetTypeVariation("BigButton", "Button");
        ButtonType(t, "BigButton", new(Color.FromHtml("#1a1a1d"), PixelKit.PanelBorder, PixelKit.SurfaceHover, PixelKit.AccentLight, pressed, PixelKit.Text, PixelKit.AccentLight, true));
        t.SetFontSize("font_size", "BigButton", 22);
        t.SetFont("font", "BigButton", PixelKit.Spaced(2));

        // the main button is inverted: light fill, dark text
        t.SetTypeVariation("PrimaryButton", "Button");
        ButtonType(t, "PrimaryButton", new(PixelKit.Accent, PixelKit.AccentLight, PixelKit.AccentLight, Colors.White, Color.FromHtml("#a8a8ad"), PixelKit.Ink, PixelKit.Ink));

        t.SetTypeVariation("GhostButton", "Button");
        ButtonType(t, "GhostButton", new(Color.FromHtml("#131315"), Color.FromHtml("#2a2a2e"), PixelKit.Surface, PixelKit.TextDim, Color.FromHtml("#0f0f11"), PixelKit.TextDim, PixelKit.Text));
        t.SetFontSize("font_size", "GhostButton", 16);
    }

    static void Toggles(Theme t)
    {
        var empty = PixelKit.Padded(new StyleBoxEmpty(), 4, 6);
        var hover = PixelKit.Padded(PixelKit.Box(new Color(PixelKit.Secondary, .05f)), 4, 6);
        foreach (var st in new[] { "normal", "pressed", "disabled", "focus" }) t.SetStylebox(st, "CheckButton", empty);
        t.SetStylebox("hover", "CheckButton", hover);
        t.SetStylebox("hover_pressed", "CheckButton", hover);
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) t.SetColor(c, "CheckButton", PixelKit.Text);
        t.SetColor("font_focus_color", "CheckButton", PixelKit.Secondary);
        t.SetFontSize("font_size", "CheckButton", 16);
        t.SetIcon("checked", "CheckButton", SwitchTexture(true, false));
        t.SetIcon("unchecked", "CheckButton", SwitchTexture(false, false));
        t.SetIcon("checked_disabled", "CheckButton", SwitchTexture(true, true));
        t.SetIcon("unchecked_disabled", "CheckButton", SwitchTexture(false, true));
    }

    static void Inputs(Theme t)
    {
        foreach (var type in new[] { "LineEdit", "TextEdit" })
        {
            t.SetStylebox("normal", type, PixelKit.Padded(PixelKit.Box(Color.FromHtml("#0b0b0d"), PixelKit.PanelBorder), 14, 8));
            t.SetStylebox("focus", type, PixelKit.Padded(PixelKit.Box(Colors.Transparent, PixelKit.AccentLight), 14, 8));
            t.SetColor("font_color", type, PixelKit.Text);
            t.SetColor("font_placeholder_color", type, PixelKit.TextMuted);
            t.SetColor("caret_color", type, PixelKit.AccentLight);
            t.SetColor("selection_color", type, new Color(PixelKit.Accent, .3f));
            t.SetConstant("caret_width", type, 3);
        }
    }

    static void Sliders(Theme t)
    {
        t.SetStylebox("slider", "HSlider", PixelKit.Padded(PixelKit.Box(Color.FromHtml("#0b0b0d"), PixelKit.PanelBorder), 0, 4));
        t.SetStylebox("grabber_area", "HSlider", PixelKit.Padded(PixelKit.Box(PixelKit.AccentDark), 0, 4));
        t.SetStylebox("grabber_area_highlight", "HSlider", PixelKit.Padded(PixelKit.Box(PixelKit.Accent), 0, 4));
        t.SetIcon("grabber", "HSlider", SquareTexture(16, PixelKit.Accent));
        t.SetIcon("grabber_highlight", "HSlider", SquareTexture(16, PixelKit.AccentLight));
    }

    static void Popups(Theme t)
    {
        t.SetStylebox("panel", "PopupMenu", PixelKit.Padded(PixelKit.Framed(PixelKit.Panel, PixelKit.PanelBorder), 6, 6));
        t.SetStylebox("hover", "PopupMenu", PixelKit.Box(PixelKit.SurfaceHover));
        t.SetColor("font_color", "PopupMenu", PixelKit.Text);
        t.SetColor("font_hover_color", "PopupMenu", PixelKit.AccentLight);
        t.SetFontSize("font_size", "PopupMenu", 16);
        t.SetConstant("v_separation", "PopupMenu", 10);
    }

    static void Scrollbars(Theme t)
    {
        t.SetStylebox("scroll", "VScrollBar", PixelKit.Padded(PixelKit.Box(Color.FromHtml("#0b0b0d")), 3, 3));
        t.SetStylebox("grabber", "VScrollBar", PixelKit.Box(PixelKit.PanelBorder));
        t.SetStylebox("grabber_highlight", "VScrollBar", PixelKit.Box(PixelKit.AccentDark));
        t.SetStylebox("grabber_pressed", "VScrollBar", PixelKit.Box(PixelKit.Accent));
    }

    // ---- procedural textures (no image assets needed) ----
    static ImageTexture SquareTexture(int side, Color color)
    {
        var img = Image.CreateEmpty(side, side, false, Image.Format.Rgba8);
        img.Fill(PixelKit.Ink);
        img.FillRect(new Rect2I(2, 2, side - 4, side - 4), color);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Pixel switch: rectangular track and square knob.</summary>
    static ImageTexture SwitchTexture(bool on, bool dimmed)
    {
        const int w = 44, h = 24;
        var track = on ? PixelKit.AccentDark : Color.FromHtml("#26262a");
        var knob = on ? PixelKit.AccentLight : PixelKit.TextMuted;
        if (dimmed) { track.A = .4f; knob.A = .5f; }
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(PixelKit.Ink, track.A));
        img.FillRect(new Rect2I(2, 2, w - 4, h - 4), track);
        img.FillRect(new Rect2I(on ? w - 22 : 4, 4, 18, h - 8), knob);
        return ImageTexture.CreateFromImage(img);
    }
}
