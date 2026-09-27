using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// HUD palette: the monochrome dark pixel language of <see cref="PixelKit"/> (Mr. President). Near-black and graphite
/// surfaces, importance by brightness; statuses are barely tinted grays. Colour proper appears only where it is data
/// (nations, religions, classes) — the map itself stays colourful.
/// </summary>
public static class Pal
{
    // surfaces, darkest → lightest
    public static readonly Color Ink = PixelKit.Ink;            // #0b0b0c — deepest wells, outlines on the map
    public static readonly Color Well = Hex(0x111113);          // sunken: stat cells, tracks, icon tiles
    public static readonly Color Card = PixelKit.Panel;         // #17171a — floating cards
    public static readonly Color Band = Hex(0x232327);          // top of a card's dithered header band
    public static readonly Color Surface = PixelKit.Surface;    // #202024 — rows, buttons
    public static readonly Color SurfaceHover = PixelKit.SurfaceHover;
    public static readonly Color Pressed = Hex(0x151517);

    // lines
    public static readonly Color Ln = Hex(0x2a2a2e);            // hairlines inside cards
    public static readonly Color Ln2 = PixelKit.PanelBorder;    // #3a3a40 — card and control frames
    public static readonly Color Ln3 = PixelKit.AccentDark;     // #6b6b70 — strong frame / hover

    // ink
    public static readonly Color Hi = PixelKit.AccentLight;     // #f0f0f2 — titles, the active thing
    public static readonly Color Tx = PixelKit.Text;            // #d8d8da
    public static readonly Color Ac = PixelKit.Accent;          // #c8c8cc — inverted fills, rules
    public static readonly Color Sec = PixelKit.Secondary;      // #a3a3a8 — kickers
    public static readonly Color Mu = PixelKit.TextDim;         // #8f8f94
    public static readonly Color Mu2 = PixelKit.TextMuted;      // #5c5c62

    // meaning (faint tints)
    public static readonly Color Ok = PixelKit.Good, Bad = PixelKit.Bad, Warn = PixelKit.Warn, Info = PixelKit.Info;
    public static readonly Color BadFill = Hex(0x2e1f20);
    public static readonly Color Unowned = Hex(0x7d7d84);
    public static readonly Color Shadow = PixelKit.Shadow;

    public static Color Hex(uint rgb) => Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    public static Color A(Color c, float a) => new(c, a);

    /// <summary>Nation colour from the running game's roster (the player's own design at [0]).</summary>
    public static Color Nation(int n) { var d = Game.I.Nations[n]; return Color.Color8(d.R, d.G, d.B); }
    public static Color Religion(int r) { var d = Data.Religions[r]; return Color.Color8(d.R, d.G, d.B); }
    public static Color PopClass(int k) { var d = Data.AncientClasses[k]; return Color.Color8(d.R, d.G, d.B); }
}
