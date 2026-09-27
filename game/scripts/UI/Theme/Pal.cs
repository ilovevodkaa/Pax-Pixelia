using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// «Мрамор и графит» palette tokens (design_final/DESIGN_NOTES.md §1). White and pale-gray surfaces, slate hairlines,
/// graphite ink; graphite is the only accent. Colour appears only where it carries meaning (nations, classes, religion, deltas).
/// </summary>
public static class Pal
{
    // surfaces
    public static readonly Color P1 = new(249 / 255f, 250 / 255f, 251 / 255f, .965f); // floating card
    public static readonly Color P1s = Hex(0xf9fafb);   // opaque twin (fades, notch)
    public static readonly Color P2 = Hex(0xffffff);    // raised cell / row
    public static readonly Color P3 = Hex(0xeff1f3);    // sunken well, icon tiles
    public static readonly Color Hd1 = Hex(0xfdfdfe), Hd2 = Hex(0xf0f2f4); // header gradient
    public static readonly Color Btn1 = Hex(0xffffff), Btn2 = Hex(0xf1f3f5); // button gradient
    public static readonly Color Plate2 = Hex(0xf7f8f9);                     // nation / clock plate bottom

    // lines
    public static readonly Color Ln = Hex(0xe1e4e8);    // hairline inside cards
    public static readonly Color Ln2 = Hex(0xc4cad0);   // card & control outline
    public static readonly Color Ln3 = Hex(0x959da6);   // strong outline / hover

    // ink
    public static readonly Color Tx = Hex(0x1d2126);
    public static readonly Color Tx2 = Hex(0x434a52);   // icon graphite
    public static readonly Color Mu = Hex(0x646c76);    // secondary text
    public static readonly Color Mu2 = Hex(0x9ba2aa);   // faint
    public static readonly Color Ac = Hex(0x2d3238);
    public static readonly Color G1 = Hex(0x3a4048), G2 = Hex(0x2a2f35), GEdge = Hex(0x1f2328); // graphite fill
    public static readonly Color GHover1 = Hex(0x454c55), GHover2 = Hex(0x30353c);
    public static readonly Color Red1 = Hex(0xc24a3b), Red2 = Hex(0xa2342a);

    // interaction wells
    public static readonly Color IbHover = Hex(0xe6e9ec), IbPress = Hex(0xdde1e5), BtnPress = Hex(0xe9ecef);
    public static readonly Color Track = Hex(0xdde1e5), PipOff = Hex(0xd0d5da), PipHover = Hex(0xb4bbc2);

    // meaning
    public static readonly Color Ok = Hex(0x2d7a3c), Bad = Hex(0xb3392b), Warn = Hex(0x9a620c), Sci = Hex(0x46627f);
    public static readonly Color Unowned = Hex(0x9aa1a9);

    /// <summary>The slate shadow ink rgba(16,20,26,a).</summary>
    public static Color Shade(float a) => new(16 / 255f, 20 / 255f, 26 / 255f, a);
    public static Color White(float a) => new(1, 1, 1, a);
    public static Color Rgb(byte r, byte g, byte b) => Color.Color8(r, g, b);
    public static Color Hex(uint rgb) => Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color Nation(int n) { var d = Core.Data.Nations[n]; return Color.Color8(d.R, d.G, d.B); }
    public static Color Religion(int r) { var d = Core.Data.Religions[r]; return Color.Color8(d.R, d.G, d.B); }
    public static Color PopClass(int k) { var d = Core.Data.AncientClasses[k]; return Color.Color8(d.R, d.G, d.B); }
}
