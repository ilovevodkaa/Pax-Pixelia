using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// HUD palette: named roles (surfaces, lines, inks, statuses) whose colours come from the era skin in force
/// (<see cref="EraSkin"/>: Костёр, Глина, Перо, Латунь, Сигнал). Importance by contrast against the card; colour proper
/// appears where it is data (nations, religions, classes) — the map itself stays colourful.
/// </summary>
public static class Pal
{
    // surfaces, darkest → lightest
    public static Color Ink = PixelKit.Ink;            // #0b0b0c — deepest wells, outlines on the map
    public static Color Well = Hex(0x111113);          // sunken: stat cells, tracks, icon tiles
    public static Color Card = PixelKit.Panel;         // #17171a — floating cards
    public static Color Band = Hex(0x232327);          // top of a card's dithered header band
    public static Color Surface = PixelKit.Surface;    // #202024 — rows, buttons
    public static Color SurfaceHover = PixelKit.SurfaceHover;
    public static Color Pressed = Hex(0x151517);

    // lines
    public static Color Ln = Hex(0x2a2a2e);            // hairlines inside cards
    public static Color Ln2 = PixelKit.PanelBorder;    // #3a3a40 — card and control frames
    public static Color Ln3 = PixelKit.AccentDark;     // #6b6b70 — strong frame / hover

    // ink
    public static Color Hi = PixelKit.AccentLight;     // #f0f0f2 — titles, the active thing
    public static Color Tx = PixelKit.Text;            // #d8d8da
    public static Color Ac = PixelKit.Accent;          // #c8c8cc — inverted fills, rules
    public static Color Sec = PixelKit.Secondary;      // #a3a3a8 — kickers
    public static Color Mu = PixelKit.TextDim;         // #8f8f94
    public static Color Mu2 = PixelKit.TextMuted;      // #5c5c62

    // meaning (faint tints)
    public static Color Ok = PixelKit.Good, Bad = PixelKit.Bad, Warn = PixelKit.Warn, Info = PixelKit.Info;
    public static Color BadFill = Hex(0x2e1f20);
    public static Color Unowned = Hex(0x7d7d84);
    public static Color Shadow = PixelKit.Shadow;

    // derived roles (set by ApplySkin; the graphite values below are the menus' look)
    public static Color OnAc = PixelKit.OnAccent;                // text and icons on an Ac fill
    public static Color Popup = Hex(0x141416);                   // tooltips, toasts, the event window
    public static Color Bar = Hex(0x141417);                     // the top bar
    public static Color GhostFill = Hex(0x131315), GhostPressed = Hex(0x0f0f11);
    public static Color AcPressed = Hex(0xa8a8ad);               // an inverted fill pressed in
    public static Color Max = Colors.White;                      // the strongest frame of a hovered inverted control
    public static Color DisabledFill = Hex(0x141416), DisabledLine = Hex(0x26262a);
    public static Color BadHover = Hex(0x3a2728), BadPressed = Hex(0x241819), BadText = Hex(0xe0b0b0), BadTextHover = Hex(0xf0c8c8);
    /// <summary>Faint washes and grids over cards: light on dark skins, dark on light ones (callers give the alpha).</summary>
    public static Color Haze = Colors.White;
    public static bool Light;

    /// <summary>Take an era skin's colours (<see cref="EraSkin.Apply"/>).</summary>
    public static void ApplySkin(EraSkin k)
    {
        Ink = k.Ink; Well = k.Well; Card = k.Card; Band = k.Band; Surface = k.Surface; SurfaceHover = k.SurfaceHover; Pressed = k.Pressed;
        Ln = k.Ln; Ln2 = k.Ln2; Ln3 = k.Ln3; Hi = k.Hi; Tx = k.Tx; Ac = k.Ac; Sec = k.Sec; Mu = k.Mu; Mu2 = k.Mu2;
        Ok = k.Ok; Bad = k.Bad; Warn = k.Warn; Info = k.Info; Shadow = k.Shadow;
        Light = k.Light;
        OnAc = k.Light ? k.Card : k.Ink;
        Popup = k.Light ? k.Card : k.Well.Lerp(k.Card, .4f);
        Bar = k.Light ? k.Band : k.Well.Lerp(k.Card, .35f);
        GhostFill = k.Light ? k.Surface : k.Well.Lerp(k.Card, .3f);
        GhostPressed = k.Well;
        AcPressed = k.Ac.Lerp(k.Mu, .35f);
        Max = k.Light ? k.Ink : Colors.White;
        DisabledFill = k.Light ? k.Pressed : k.Well.Lerp(k.Card, .4f);
        DisabledLine = k.Ln;
        BadFill = k.Card.Lerp(k.Bad, .18f); BadHover = k.Card.Lerp(k.Bad, .28f); BadPressed = k.Well.Lerp(k.Bad, .15f);
        BadText = k.Light ? k.Bad : k.Bad.Lerp(Colors.White, .45f);
        BadTextHover = k.Light ? k.Bad.Darkened(.25f) : k.Bad.Lerp(Colors.White, .6f);
        Haze = k.Light ? k.Ink : Colors.White;
    }

    public static Color Hex(uint rgb) => Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    public static Color A(Color c, float a) => new(c, a);

    /// <summary>Nation colour from the running game's roster (the player's own design at [0]).</summary>
    public static Color Nation(int n) { var d = Game.I.Nations[n]; return Color.Color8(d.R, d.G, d.B); }
    public static Color Religion(int r) { var d = Data.Religions[r]; return Color.Color8(d.R, d.G, d.B); }
    public static Color PopClass(int k) { var d = Data.AncientClasses[k]; return Color.Color8(d.R, d.G, d.B); }
}
