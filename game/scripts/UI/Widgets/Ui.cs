using System;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// Small factories for the recurring controls. Decoration is mouse-transparent (Ignore) so cards decide input;
/// interactive children use Pass so a wheel over them still scrolls the province panel.
/// </summary>
public static class Ui
{
    public static Label Text(string text, string role = null, bool wrap = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        if (role != null) l.ThemeTypeVariation = role;
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    /// <summary>UPPERCASE tracked caption (.cap, .stat .l, .tag).</summary>
    public static Label Cap(string text) { var l = Text(text, "Cap"); l.Uppercase = true; return l; }

    public static Label Colored(this Label l, Color c) { l.AddThemeColorOverride("font_color", c); return l; }
    public static Label Sized(this Label l, int size) { l.AddThemeFontSizeOverride("font_size", size); return l; }
    public static Label Spacing(this Label l, int lineSpacing) { l.AddThemeConstantOverride("line_spacing", lineSpacing); return l; }

    /// <summary>Pixel icon at scale 1 (13 px) or 2 (26 px), tinted.</summary>
    public static TextureRect Icon(string name, int scale = 1, Color? tint = null, bool shadow = true) => new()
    {
        Texture = Icons.Get(name, scale, shadow),
        StretchMode = TextureRect.StretchModeEnum.KeepCentered,
        CustomMinimumSize = new Vector2(Icons.Size(scale, shadow), Icons.Size(scale, shadow)),
        SelfModulate = tint ?? Pal.Sec,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    /// <summary>
    /// A themed button. With both icon and text it is a <see cref="TextButton"/> (icon and text centred together,
    /// like the CSS inline-flex buttons); icon-only buttons use Godot's own centred icon.
    /// </summary>
    public static Button Button(string text, string icon = null, string skin = null, Action onPress = null, int iconScale = 1, int height = 30)
    {
        bool composite = icon != null && !string.IsNullOrEmpty(text);
        var b = composite ? new TextButton(text, Icons.Get(icon, iconScale)) : new Button { Text = text ?? "" };
        b.FocusMode = Control.FocusModeEnum.None;
        b.MouseFilter = Control.MouseFilterEnum.Pass;
        b.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        SetHeight(b, height);
        if (skin != null) b.ThemeTypeVariation = skin;
        if (icon != null && !composite) b.Icon = Icons.Get(icon, iconScale);
        if (onPress != null) b.Pressed += Deferred(onPress);
        if (skin == "Pri") PixelKit.AddPressMotion(b);   // the kit's springy hover/press on the main actions only
        else Core.Audio.UiSounds.Attach(b);               // the others still sound: hover + click
        return b;
    }

    /// <summary>
    /// Runs a UI action after the current input event has finished propagating. Actions often rebuild the very
    /// container that holds the button; a control that leaves the tree mid-event stops Godot's GUI propagation and
    /// the click would fall through to the map's _UnhandledInput.
    /// </summary>
    public static Action Deferred(Action a) => () => Callable.From(a).CallDeferred();

    public static Button IconButton(string icon, string skin, int w, int h, int iconScale, Action onPress = null)
    {
        var b = Button(null, icon, skin, onPress, iconScale, h);
        b.CustomMinimumSize = new Vector2(w, h);
        b.IconAlignment = HorizontalAlignment.Center;
        return b;
    }

    public static void SetHeight(Button b, int h)
    {
        if (b is TextButton tb) tb.MinHeight = h; else b.CustomMinimumSize = new Vector2(b.CustomMinimumSize.X, h);
    }

    /// <summary>Disabled buttons take the theme's sunken «disabled» skin; the pointer cursor goes away too.</summary>
    public static void Enable(Button b, bool on)
    {
        if (b.Disabled == !on) return;
        b.Disabled = !on;
        b.MouseDefaultCursorShape = on ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
    }

    public static Control Gap(int w, int h) => new() { CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
    public static Control Expand() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static HBoxContainer HBox(int sep, params Control[] kids)
    {
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", sep);
        foreach (var k in kids) if (k != null) h.AddChild(k);
        return h;
    }

    public static VBoxContainer VBox(int sep, params Control[] kids)
    {
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", sep);
        foreach (var k in kids) if (k != null) v.AddChild(k);
        return v;
    }

    public static PanelContainer Panel(StyleBox sb, Control child = null, Control.MouseFilterEnum filter = Control.MouseFilterEnum.Ignore)
    {
        var p = new PanelContainer { MouseFilter = filter };
        p.AddThemeStyleboxOverride("panel", sb);
        if (child != null) p.AddChild(child);
        return p;
    }

    public static MarginContainer Margin(Control child, int l, int t, int r, int b)
    {
        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        m.AddThemeConstantOverride("margin_left", l); m.AddThemeConstantOverride("margin_top", t);
        m.AddThemeConstantOverride("margin_right", r); m.AddThemeConstantOverride("margin_bottom", b);
        if (child != null) m.AddChild(child);
        return m;
    }

    public static ColorRect Rule(Color c, int w, int h) => new()
    {
        Color = c, CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    public static T Center<T>(this T c) where T : Control { c.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; return c; }
    public static T Grow<T>(this T c) where T : Control { c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; return c; }
    public static T MinSize<T>(this T c, int w, int h) where T : Control { c.CustomMinimumSize = new Vector2(w, h); return c; }

    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }
}

/// <summary>Square pixel colour swatch with a hard dark frame (legend squares, nation chips, leaderboard).</summary>
public partial class Swatch : Control
{
    Color _color;
    public Color Color { get => _color; set { if (_color == value) return; _color = value; QueueRedraw(); } }

    public Swatch() : this(Colors.Gray) { }
    public Swatch(Color c, int size = 10)
    {
        _color = c;
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Pal.Ink);
        DrawRect(new Rect2(2, 2, Size.X - 4, Size.Y - 4), _color);
        DrawRect(new Rect2(2, 2, Size.X - 4, 2), _color.Lightened(.25f));   // lit top edge, as on the map sprites
    }
}

/// <summary>
/// Button whose icon and caption sit together as one centred (or left-aligned) group with a 6px gap — Godot's Button
/// pins its icon to the edge. Caption font/colour and icon tint follow the button's skin (theme variation).
/// Native Button sizing ignores script children, so the width is published through CustomMinimumSize
/// (use <see cref="MinHeight"/> instead of setting CustomMinimumSize directly).
/// </summary>
public partial class TextButton : Button
{
    readonly TextureRect _icon;
    readonly Label _label;
    readonly HBoxContainer _row;
    float _minHeight = 30;

    public TextButton() : this("", null) { }
    public TextButton(string caption, Texture2D icon)
    {
        _icon = new TextureRect { Texture = icon, StretchMode = TextureRect.StretchModeEnum.KeepCentered, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _label = new Label { Text = caption, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        _row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", 6);
        _row.AddChild(_icon);
        _row.AddChild(_label);
        AddChild(_row);
        ClipContents = true;
    }

    public string Caption { get => _label.Text; set { if (_label.Text == value) return; _label.Text = value; Fit(); } }
    public Texture2D IconTexture { get => _icon.Texture; set { if (_icon.Texture == value) return; _icon.Texture = value; Fit(); } }
    public float MinHeight { get => _minHeight; set { _minHeight = value; Fit(); } }
    /// <summary>Let the container decide the width (the caption is clipped) — for grid cells like the build menu.</summary>
    public bool Elastic { get => _elastic; set { _elastic = value; Fit(); } }
    bool _elastic;

    public HorizontalAlignment Align
    {
        set => _row.Alignment = value == HorizontalAlignment.Left ? BoxContainer.AlignmentMode.Begin : BoxContainer.AlignmentMode.Center;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationThemeChanged || what == NotificationEnterTree) Restyle();
        else if (what == NotificationResized) Layout();
    }

    void Restyle()
    {
        _label.AddThemeFontOverride("font", GetThemeFont("font"));
        _label.AddThemeFontSizeOverride("font_size", GetThemeFontSize("font_size"));
        SyncColors();
        Fit();
    }

    /// <summary>Caption and icon follow the skin's hover / pressed / disabled colours like a native Button's text.</summary>
    void SyncColors()
    {
        var (font, icon) = GetDrawMode() switch
        {
            DrawMode.Disabled => ("font_disabled_color", "icon_disabled_color"),
            DrawMode.Hover => ("font_hover_color", "icon_hover_color"),
            DrawMode.Pressed or DrawMode.HoverPressed => ("font_pressed_color", "icon_pressed_color"),
            _ => ("font_color", "icon_normal_color"),
        };
        _label.AddThemeColorOverride("font_color", GetThemeColor(font));
        _icon.SelfModulate = GetThemeColor(icon);
    }

    public override void _Draw() => SyncColors();

    /// <summary>Width from font metrics (child min-sizes are not reliable before the children enter the tree).</summary>
    float ContentWidth()
    {
        var font = GetThemeFont("font");
        float text = font?.GetStringSize(_label.Text, HorizontalAlignment.Left, -1, GetThemeFontSize("font_size")).X ?? 0;
        return Mathf.Ceil((_icon.Texture?.GetWidth() ?? 0) + 6 + text);
    }

    void Fit()
    {
        var sb = GetThemeStylebox("normal");
        float pad = sb == null ? 0 : sb.GetMargin(Side.Left) + sb.GetMargin(Side.Right);
        CustomMinimumSize = new Vector2(_elastic ? 0 : ContentWidth() + pad, _minHeight);
        Layout();
    }

    void Layout()
    {
        var sb = GetThemeStylebox("normal");
        float l = sb?.GetMargin(Side.Left) ?? 0, r = sb?.GetMargin(Side.Right) ?? 0;
        _row.Position = new Vector2(l, 0);
        _row.Size = new Vector2(Mathf.Max(0, Size.X - l - r), Size.Y);
    }
}
