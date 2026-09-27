using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PaxPixelia.World;

namespace PaxPixelia.Map;

/// <summary>
/// Map-side polish of the generated terrain colour, done once per world on the CPU (ART_BIBLE §3.3 «рельеф без
/// соли»): forest salt (a dark/light speckle on every pixel) becomes clustered canopy glyphs — broadleaf crowns 2×2,
/// spruces 1×2, jungle crowns 3×3, each lit on the north-west and shaded on the south-east with dark gaps between
/// them; the edge of a wood thins out on a hashed dither instead of stopping on a line. Only forest biomes are
/// touched; the relief shading underneath is kept (the salt is taken out with a 3×3 median first).
/// </summary>
internal static class TerrainPolish
{
    const int Taiga = 5, Forest = 8, Jungle = 12;

    public static byte[] Apply(WorldData w)
    {
        var src = w.BaseColor;
        var dst = (byte[])src.Clone();
        int W = w.W, H = w.H;
        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                int b = w.Biome[i];
                if (w.Land[i] == 0 || (b != Taiga && b != Forest && b != Jungle)) continue;
                int m = Median(w, src, x, y, b);
                float f = Glyph(b, x, y, w.Seed);
                // near the edge of the wood the canopy thins out on a dither, so the edge reads soft
                int edge = EdgeDistance(w, x, y, b);
                if (edge < 3 && Hash(x, y, w.Seed + 7) % 3 >= edge) f = f < 1 ? .92f : 1;
                int o = i * 4, s = m * 4;
                dst[o] = Clamp(src[s] * f); dst[o + 1] = Clamp(src[s + 1] * f); dst[o + 2] = Clamp(src[s + 2] * f);
            }
        });
        return dst;
    }

    /// <summary>Brightness factor of the canopy pattern at (x, y) for a forest biome.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Glyph(int biome, int x, int y, int seed)
    {
        int cell = biome == Jungle ? 4 : 3, ch = biome == Taiga ? 3 : cell;
        int cy = Floor(y, ch);
        int shift = (int)(Hash(0, cy, seed + 31) % (uint)cell);      // rows slide against each other: no lattice
        int cx = Floor(x + shift, cell);
        uint h = Hash(cx, cy, seed + biome);
        int ox = (int)(h % (uint)(cell - 1)), oy = (int)((h >> 3) % (uint)(ch - 1));
        bool tree = (h >> 8) % 100 < (biome == Taiga ? 72u : biome == Jungle ? 90u : 78u);
        int lx = x + shift - cx * cell - ox, ly = y - cy * ch - oy;
        const float gap = .9f;
        if (!tree) return .95f;
        switch (biome)
        {
            case Taiga:                               // spruce: 1×2, lit tip, shaded foot
                if (lx == 0 && ly == 0) return 1.1f;
                if (lx == 0 && ly == 1) return .97f;
                return gap;
            case Jungle:                              // 3×3 crown
                if (lx < 0 || ly < 0 || lx > 2 || ly > 2) return gap;
                if (lx + ly == 0) return 1.12f;
                if (lx + ly == 4) return .9f;
                return lx + ly == 1 ? 1.05f : 1f;
            default:                                  // broadleaf: 2×2 crown
                if (lx < 0 || ly < 0 || lx > 1 || ly > 1) return gap;
                return lx + ly == 0 ? 1.11f : lx + ly == 2 ? .92f : 1f;
        }
    }

    static int Floor(int v, int d) => v >= 0 ? v / d : (v - d + 1) / d;

    /// <summary>Chebyshev distance (0..3) to the nearest pixel of another biome.</summary>
    static int EdgeDistance(WorldData w, int x, int y, int b)
    {
        for (int r = 1; r <= 2; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int yy = y + dy;
                    if (yy < 0 || yy >= w.H) continue;
                    int xx = x + dx; if (xx < 0) xx += w.W; else if (xx >= w.W) xx -= w.W;
                    if (w.Biome[yy * w.W + xx] != b) return r - 1;
                }
        return 3;
    }

    /// <summary>Index of the median-brightness pixel among the same-biome pixels of the 3×3 block round (x, y).</summary>
    static int Median(WorldData w, byte[] c, int x, int y, int b)
    {
        Span<int> idx = stackalloc int[9];
        Span<int> lum = stackalloc int[9];
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int yy = y + dy;
            if (yy < 0 || yy >= w.H) continue;
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx; if (xx < 0) xx += w.W; else if (xx >= w.W) xx -= w.W;
                int j = yy * w.W + xx;
                if (w.Biome[j] != b || w.Land[j] == 0) continue;
                int l = c[j * 4] * 3 + c[j * 4 + 1] * 6 + c[j * 4 + 2];
                int k = n++;
                while (k > 0 && lum[k - 1] > l) { lum[k] = lum[k - 1]; idx[k] = idx[k - 1]; k--; }
                lum[k] = l; idx[k] = j;
            }
        }
        return idx[n / 2];
    }

    static uint Hash(int x, int y, int seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    static byte Clamp(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)v;
}
