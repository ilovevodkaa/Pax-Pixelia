using System;

namespace PaxPixelia.World;

/// <summary>
/// Terrain materials and their 5-tone ramps (docs/design/ART_BIBLE.md §2.2): step 0 = deepest shadow … 2 = base …
/// 4 = highlight; shadows drift to blue and gain saturation, highlights drift to yellow and lose it. The generator
/// bakes ramp colours into WorldData.BaseColor and also stores (material, step) per pixel, so the map can later swap
/// palettes through a LUT (seasons, night, engraving) without regenerating the world.
/// </summary>
public static class TerrainPalette
{
    // materials 1..14 are the biomes (Core.Data.BiomeName); the rest are surfaces the colour pass adds
    public const byte Water = 0, Sand = 15, Rock = 16, Snow = 17, Pools = 18, Acacia = 19, Floodplain = 20, HillBase = 21;

    /// <summary>Hill variant of biome b (4..14): the biome ramp mixed 28% towards dry brown.</summary>
    public static byte Hill(int biome) => (byte)(HillBase + biome - 4);

    public const int Count = HillBase + 11;

    /// <summary>Ramps[material][step] = 0xRRGGBB.</summary>
    public static readonly int[][] Ramps = Build();

    static int[][] Build()
    {
        var r = new int[Count][];
        r[Water] = new[] { 0x1C2C40, 0x253B52, 0x2F4D64, 0x406378, 0x507486 };  // abyss, deep, mid, shallow, coast
        r[1] = new[] { 0x96A4BC, 0xB2C0D4, 0xCED8E4, 0xE4EAF0, 0xF6F8FA };      // glacier
        r[2] = r[1];                                                            // peaks (rock/snow chosen per pixel)
        r[3] = new[] { 0x4A4442, 0x625C56, 0x7A746C, 0x989286, 0xB2AC9E };      // mountains: rock, no snow tone
        r[4] = new[] { 0x525E4D, 0x68725E, 0x808670, 0x949781, 0xA9A993 };      // tundra
        r[5] = new[] { 0x273B35, 0x30473C, 0x3A5442, 0x445F49, 0x506A51 };      // taiga
        r[6] = Ramp(142, 142, 102);                                             // cold steppe
        r[7] = new[] { 0x2B3E2B, 0x3A4B35, 0x4A5840, 0x58634B, 0x676F57 };      // swamp
        r[8] = new[] { 0x274732, 0x315736, 0x40663C, 0x527349, 0x668157 };      // forest
        r[9] = new[] { 0x34652F, 0x4F7A3C, 0x6E904A, 0x8AA35C, 0xA6B571 };      // meadow
        r[10] = new[] { 0x4E6D39, 0x6D8548, 0x909C58, 0xADB06B, 0xC5C582 };     // plains
        r[11] = new[] { 0x775941, 0x917A52, 0xAAA064, 0xC0BF79, 0xD6D691 };     // steppe
        r[12] = new[] { 0x1E4534, 0x275337, 0x306238, 0x3C6F3D, 0x4F7B4A };     // jungle
        r[13] = new[] { 0x784F36, 0x927245, 0xAC9A56, 0xC2BC6B, 0xD9D984 };     // savanna
        r[14] = new[] { 0x8C6555, 0xAA8B6A, 0xC8B480, 0xE2D799, 0xFCFBB5 };     // desert
        r[Sand] = new[] { 0x9A8660, 0xB8A377, 0xD2BE8C, 0xE4D5A2, 0xF1E6BD };
        r[Rock] = r[3];
        r[Snow] = r[1];
        r[Pools] = new[] { 0x2A3C40, 0x34494E, 0x46605F, 0x5A7672, 0x6E8A80 };  // swamp water with a reedy rim
        r[Acacia] = new[] { 0x3E4524, 0x566834, 0x70823E, 0x8C9C4C, 0xA8B45E };
        r[Floodplain] = Ramp(118, 138, 70);                                     // green strips along desert rivers
        for (int b = 4; b <= 14; b++)
        {
            int c = r[b][2];
            r[Hill(b)] = Ramp(((c >> 16) & 255) * .72 + 114 * .28, ((c >> 8) & 255) * .72 + 104 * .28, (c & 255) * .72 + 82 * .28);
        }
        return r;
    }

    /// <summary>The concept sheets' terrain_ramp (docs/design/concepts/src/terrain.py), pure arithmetic so it is
    /// identical on every machine.</summary>
    public static int[] Ramp(double r, double g, double b)
    {
        Hsv(r / 255, g / 255, b / 255, out double h, out double s, out double v);
        ReadOnlySpan<double> vm = stackalloc double[] { .70, .85, 1, 1.13, 1.26 }, sm = stackalloc double[] { 1.10, 1.05, 1, .90, .78 };
        var o = new int[5];
        for (int k = 0; k < 5; k++)
        {
            int step = k - 2;
            double hh = h, ss = s;
            if (s < .08) { if (step < 0) { hh = .6; ss = s + .06 * -step; } }
            else if (step < 0) { hh = HueToward(h, .68, .035 * -step); ss = s * sm[k]; }
            else if (step > 0) { hh = HueToward(h, 1 / 6.0, .022 * step); ss = s * sm[k]; }
            Rgb(hh, Math.Clamp(ss, 0, 1), Math.Clamp(v * vm[k], 0, 1), out double rr, out double gg, out double bb);
            o[k] = (int)(rr * 255 + .5) << 16 | (int)(gg * 255 + .5) << 8 | (int)(bb * 255 + .5);
        }
        return o;
    }

    static double HueToward(double h, double target, double amt)
    {
        double d = Mod(target - h + .5, 1) - .5;
        return Mod(h + Math.Clamp(d, -amt, amt), 1);
    }

    static double Mod(double a, double m) => a - m * Math.Floor(a / m);

    static void Hsv(double r, double g, double b, out double h, out double s, out double v)
    {
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
        v = mx; s = mx == 0 ? 0 : d / mx;
        if (d == 0) { h = 0; return; }
        double rc = (mx - r) / d, gc = (mx - g) / d, bc = (mx - b) / d;
        h = r == mx ? bc - gc : g == mx ? 2 + rc - bc : 4 + gc - rc;
        h = Mod(h / 6, 1);
    }

    static void Rgb(double h, double s, double v, out double r, out double g, out double b)
    {
        if (s == 0) { r = g = b = v; return; }
        int i = (int)(h * 6);
        double f = h * 6 - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }
}
