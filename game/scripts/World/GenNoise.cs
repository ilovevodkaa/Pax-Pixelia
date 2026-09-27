using System;
using System.Runtime.CompilerServices;

namespace PaxPixelia.World;

/// <summary>
/// Core.Noise.Fbm for the generator's hot loops, bit-identical to it (WorldGenTests checks 200k samples).
/// It exists for start-up speed only. Generation runs once, right after launch, when Core.Noise would still be tier-0
/// JIT code for most of the run; AggressiveOptimization compiles this fully optimised on first call. Vn and H2 are
/// inlined by hand because Debug builds (what Godot runs from the editor) never inline, and the call overhead
/// dominated there. The x wrap skips the integer division in the common case.
/// </summary>
internal static class GenNoise
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Fbm(double x, double y, int k, int oct, int s, int ww)
    {
        double u = x / ww * k, v = y / ww * k, a = .5, sum = 0, nm = 0;
        int p = k;
        for (int o = 0; o < oct; o++)
        {
            // Vn(u, v, p, s + o*101): value noise with period p in x
            int ix = (int)u, iy = (int)v;     // floor without a Math.Floor call (not an intrinsic in Debug builds)
            if (ix > u) ix--;
            if (iy > v) iy--;
            double fx = u - ix, fy = v - iy, ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
            int x0 = ix;
            if ((uint)x0 >= (uint)p) { x0 %= p; if (x0 < 0) x0 += p; }
            int x1 = x0 + 1 == p ? 0 : x0 + 1;
            // H2(ix, iy, s) = mix(ix*374761393 + iy*668265263 + s*1442695041), all mod 2^32
            uint row0 = unchecked((uint)iy * 668265263u + (uint)(s + o * 101) * 1442695041u), row1 = unchecked(row0 + 668265263u);
            uint c0 = unchecked((uint)x0 * 374761393u), c1 = unchecked((uint)x1 * 374761393u), h;
            h = unchecked(c0 + row0); h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16; double ha = h / 4294967296.0;
            h = unchecked(c1 + row0); h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16; double hb = h / 4294967296.0;
            h = unchecked(c0 + row1); h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16; double hc = h / 4294967296.0;
            h = unchecked(c1 + row1); h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16; double hd = h / 4294967296.0;
            sum += a * (ha + (hb - ha) * ux + (hc - ha) * uy + (ha - hb - hc + hd) * ux * uy);
            nm += a; a *= .5; u *= 2; v *= 2; p *= 2;
        }
        return sum / nm;
    }
}
