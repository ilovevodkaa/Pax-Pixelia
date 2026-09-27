using System;

namespace PaxPixelia.Core.Flags;

/// <summary>
/// Deterministic flag rasteriser (pure C#, RGBA8 byte arrays): the field, the charge with the heraldic rule of
/// tincture, wind frames and the flag on a pole. Port of sheet_flags.py; the Godot side wraps it in FlagTextures.
/// </summary>
public static class FlagRender
{
    /// <summary>Sum of |ΔRGB| below which a charge melts into the field under it.</summary>
    const int MinContrast = 120;
    static readonly (byte, byte, byte) Outline = (29, 25, 27);          // pp.K
    static readonly (byte, byte, byte) Wood = (122, 86, 52), Finial = (232, 194, 88);

    /// <summary>The flag as w×h RGBA8 (default 18×12).</summary>
    public static byte[] Render(FlagSpec spec, (byte R, byte G, byte B) nation, int w = FlagPatterns.W, int h = FlagPatterns.H)
    {
        var px = new byte[w * h * 4];
        Span<(byte, byte, byte)> slot = stackalloc (byte, byte, byte)[3];
        slot[0] = FlagPatterns.Rgb(spec.T1, nation);
        slot[1] = FlagPatterns.Rgb(spec.T2, nation);
        slot[2] = FlagPatterns.Rgb(spec.T3, nation);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Put(px, w, x, y, slot[FlagPatterns.Slot(spec.Division, x, y, w, h)]);
        if (spec.Charge > 0 && w >= FlagPatterns.W)
        {
            var (cx, cy) = FlagPatterns.Anchor(spec.Pos, w, h);
            var col = FlagPatterns.Rgb(EffectiveChargeTincture(spec, nation), nation);
            var mask = FlagPatterns.Mask(spec.Charge);
            int x0 = (int)Math.Round(cx + .5 - mask[0].Length / 2.0), y0 = (int)Math.Round(cy + .5 - mask.Length / 2.0);
            for (int my = 0; my < mask.Length; my++)
                for (int mx = 0; mx < mask[my].Length; mx++)
                    if (mask[my][mx] == '#') Put(px, w, x0 + mx, y0 + my, col);
        }
        return px;
    }

    /// <summary>The charge tincture actually drawn: the chosen one, or silver / sable when it would melt into the field.</summary>
    public static byte EffectiveChargeTincture(FlagSpec spec, (byte R, byte G, byte B) nation)
    {
        var under = FieldUnderCharge(spec, nation);
        var c = FlagPatterns.Rgb(spec.ChargeTinct, nation);
        if (Math.Abs(c.R - under.R) + Math.Abs(c.G - under.G) + Math.Abs(c.B - under.B) >= MinContrast) return spec.ChargeTinct;
        return under.R + under.G + under.B < 420 ? FlagPatterns.Argent : FlagPatterns.Sable;
    }

    /// <summary>True when the rule of tincture overrides the chosen charge colour (the editor shows a chip).</summary>
    public static bool ChargeRecoloured(FlagSpec spec, (byte R, byte G, byte B) nation) =>
        spec.Charge > 0 && EffectiveChargeTincture(spec, nation) != spec.ChargeTinct;

    static (byte R, byte G, byte B) FieldUnderCharge(FlagSpec spec, (byte R, byte G, byte B) nation)
    {
        var (x, y) = FlagPatterns.Anchor(spec.Pos);
        byte t = FlagPatterns.Slot(spec.Division, x, y) switch { 0 => spec.T1, 1 => spec.T2, _ => spec.T3 };
        return FlagPatterns.Rgb(t, nation);
    }

    /// <summary>One of 4 wind frames: a travelling sine brightens/darkens columns in 2 posterised steps and the fly
    /// end sags up to 2px. Output is w×(h+2) RGBA8.</summary>
    public static byte[] WindFrame(byte[] flat, int w, int h, int frame)
    {
        int oh = h + 2;
        var px = new byte[w * oh * 4];
        for (int x = 0; x < w; x++)
        {
            double s = Math.Sin(x * .55 - frame * 1.6);
            int dy = (int)Math.Round((s + 1) * .5 * ((double)x / w) * 2);
            float k = s > .55 ? 1.10f : s < -.55 ? .86f : 1f;
            for (int y = 0; y < h; y++)
            {
                int si = (y * w + x) * 4, di = ((y + dy) * w + x) * 4;
                for (int c = 0; c < 3; c++) px[di + c] = (byte)Math.Min(255, (int)(flat[si + c] * k + .5f));
                px[di + 3] = flat[si + 3];
            }
        }
        return px;
    }

    public const int PoleW = FlagPatterns.W + 4, PoleH = FlagPatterns.H + 12;

    /// <summary>The flag on a pole (PoleW×PoleH RGBA8) for wind frame 0..3: wooden pole with a gold finial, 1px dark
    /// outline around everything and a hard 1px shadow down-right.</summary>
    public static byte[] OnPole(FlagSpec spec, (byte R, byte G, byte B) nation, int frame)
    {
        const int w = FlagPatterns.W, h = FlagPatterns.H;
        var wave = WindFrame(Render(spec, nation), w, h, frame);
        var img = new byte[PoleW * PoleH * 4];
        for (int y = 1; y < PoleH - 2; y++) Put(img, PoleW, 1, y, y == 1 ? Finial : Wood);
        for (int y = 0; y < h + 2; y++)
            for (int x = 0; x < w; x++)
            {
                int si = (y * w + x) * 4;
                if (wave[si + 3] == 0) continue;
                Put(img, PoleW, x + 2, y + 2, (wave[si], wave[si + 1], wave[si + 2]));
            }
        return WithShadow(Outlined(img, PoleW, PoleH), PoleW, PoleH);
    }

    /// <summary>1px outline around opaque pixels (in place on a copy).</summary>
    public static byte[] Outlined(byte[] src, int w, int h)
    {
        var dst = (byte[])src.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (src[(y * w + x) * 4 + 3] != 0) continue;
                if (Opaque(src, w, h, x + 1, y) || Opaque(src, w, h, x - 1, y) || Opaque(src, w, h, x, y + 1) || Opaque(src, w, h, x, y - 1))
                    Put(dst, w, x, y, Outline);
            }
        return dst;
    }

    static byte[] WithShadow(byte[] src, int w, int h)
    {
        var dst = (byte[])src.Clone();
        for (int y = 1; y < h; y++)
            for (int x = 1; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (src[i + 3] != 0 || !Opaque(src, w, h, x - 1, y - 1)) continue;
                dst[i] = dst[i + 1] = dst[i + 2] = 0; dst[i + 3] = 90;
            }
        return dst;
    }

    static bool Opaque(byte[] px, int w, int h, int x, int y) => x >= 0 && y >= 0 && x < w && y < h && px[(y * w + x) * 4 + 3] > 200;

    static void Put(byte[] px, int w, int x, int y, (byte R, byte G, byte B) c)
    {
        int h = px.Length / 4 / w;
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int i = (y * w + x) * 4;
        px[i] = c.R; px[i + 1] = c.G; px[i + 2] = c.B; px[i + 3] = 255;
    }
}
