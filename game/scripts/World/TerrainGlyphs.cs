using System.Runtime.CompilerServices;

namespace PaxPixelia.World;

/// <summary>
/// Pixel-art helpers of the colour pass (ART_BIBLE §3.3): posterising with a narrow ordered-dither band, and the
/// tree glyphs of the concept sheet (docs/design/concepts/src/terrain.py) — crowns lit from the top left with a
/// contact shadow to the lower right, placed one per jittered 4×4 cell and resolved per pixel, so the pass stays
/// parallel and needs no drawing order between rows.
/// </summary>
internal static class TerrainGlyphs
{
    public const int Cell = 4, Reach = 6;   // anchor cell size; a glyph with its shadow spans at most Reach px from its anchor

    public enum Kind : byte { None, ForestBig, ForestSmall, Conifer, JungleBig, JungleSmall, Acacia }

    static readonly int[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    /// <summary>Posterise a continuous ramp level to a step 0..4: only a band (±0.17 by default) around each step
    /// boundary is Bayer-dithered, so flat areas stay one clean tone.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Quant(double v, int x, int y, double band = .34)
    {
        double lo = .5 - band / 2, hi = .5 + band / 2;
        int fl = v >= 0 ? (int)v : (int)v - 1;
        double f = v - fl;
        int q = f < lo ? fl : f > hi ? fl + 1 : (f - lo) / band > (Bayer4[((y & 3) << 2) | (x & 3)] + .5) / 16 ? fl + 1 : fl;
        return q < 0 ? 0 : q > 4 ? 4 : q;
    }

    /// <summary>Bayer threshold in [0, 1) for a pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Bayer(int x, int y) => (Bayer4[((y & 3) << 2) | (x & 3)] + .5) / 16;

    // Crown pixels: L = step 4, M = 3, D = 2; the shadow is the crown mask moved one pixel right and down
    // (the acacia casts a flat shadow line on the ground under its canopy instead). T = trunk.
    static readonly string[][] Shapes =
    {
        null,
        new[] { ".LM.", "LMMD", "MMDD", ".DD." },
        new[] { ".L.", "LMD", ".D." },
        new[] { ".L.", ".LD", "LMD", "MDD" },
        new[] { ".LLM.", "LLMMD", "LMMDD", "MMDDD", ".DDD." },
        new[] { ".LM.", "LMMD", "MDDD", ".DD." },
        new[] { "LLMMM", ".DDD.", "..T.." },
    };

    // [kind][dy * Reach + dx] = 0 nothing, 1 shadow, 2 D, 3 M, 4 L, 5 trunk
    static readonly byte[][] Masks = BuildMasks();

    static byte[][] BuildMasks()
    {
        var m = new byte[Shapes.Length][];
        for (int k = 1; k < Shapes.Length; k++)
        {
            var g = new byte[Reach * Reach];
            var s = Shapes[k];
            if ((Kind)k == Kind.Acacia)
                for (int dx = 1; dx <= 5 && dx < Reach; dx++) g[3 * Reach + dx] = 1;
            else
                for (int dy = 0; dy < s.Length; dy++)
                    for (int dx = 0; dx < s[dy].Length; dx++)
                        if (s[dy][dx] != '.') g[(dy + 1) * Reach + dx + 1] = 1;
            for (int dy = 0; dy < s.Length; dy++)
                for (int dx = 0; dx < s[dy].Length; dx++)
                    g[dy * Reach + dx] = s[dy][dx] switch { 'L' => 4, 'M' => 3, 'D' => 2, 'T' => 5, _ => g[dy * Reach + dx] };
            m[k] = g;
        }
        return m;
    }

    /// <summary>What glyph `kind` paints at offset (dx, dy) from its anchor (0 = nothing).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int At(Kind kind, int dx, int dy) => (uint)dx < Reach && (uint)dy < Reach ? Masks[(int)kind][dy * Reach + dx] : 0;

    /// <summary>Pack an anchor for the per-cell table: kind, and the anchor's offset inside its cell.</summary>
    public static byte Pack(Kind kind, int ox, int oy) => (byte)((int)kind << 4 | oy << 2 | ox);
    public static Kind KindOf(byte a) => (Kind)(a >> 4);
    public static int Ox(byte a) => a & 3;
    public static int Oy(byte a) => (a >> 2) & 3;
}
