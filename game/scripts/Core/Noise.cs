using System;
using System.Runtime.CompilerServices;

namespace PaxPixelia.Core;

/// <summary>
/// Deterministic noise (pure C#, no Godot types) — must stay bit-for-bit reproducible for a seed,
/// because multiplayer (lockstep) clients regenerate the same world from the seed.
/// Port of the mockup's h2/vn/fbm (docs/mockups/js/core.js). fbm is periodic in x: the world wraps horizontally.
/// </summary>
public static class Noise
{
    /// <summary>Integer hash of (ix, iy, s) → [0,1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double H2(int ix, int iy, int s) => Hash(ix, iy, s) / 4294967296.0;

    /// <summary>The 32-bit integer hash behind H2 (the simulation rolls with it without leaving integers).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Hash(int ix, int iy, int s)
    {
        int h = unchecked(ix * 374761393 + iy * 668265263 + s * 1442695041);
        h = unchecked((h ^ (int)((uint)h >> 13)) * 1274126177);
        h ^= (int)((uint)h >> 16);
        return (uint)h;
    }

    /// <summary>Value noise with period p in x.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Vn(double x, double y, int p, int s)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy, ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
        int x0 = ix % p; if (x0 < 0) x0 += p;
        int x1 = x0 + 1 == p ? 0 : x0 + 1;
        double a = H2(x0, iy, s), b = H2(x1, iy, s), c = H2(x0, iy + 1, s), d = H2(x1, iy + 1, s);
        return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy;
    }

    /// <summary>Fractal noise. x,y in world pixels; k = cells across the whole world width ww (integer ⇒ periodic).</summary>
    public static double Fbm(double x, double y, int k, int oct, int s, int ww)
    {
        double u = x / ww * k, v = y / ww * k, a = .5, sum = 0, nm = 0;
        int p = k;
        for (int o = 0; o < oct; o++) { sum += a * Vn(u, v, p, s + o * 101); nm += a; a *= .5; u *= 2; v *= 2; p *= 2; }
        return sum / nm;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Smooth(double a, double b, double x)
    {
        double t = (x - a) / (b - a); t = t < 0 ? 0 : t > 1 ? 1 : t; return t * t * (3 - 2 * t);
    }
}
