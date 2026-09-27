using System;

namespace PaxPixelia.World;

/// <summary>
/// Noise frequencies for a world width (port of core.js KS/KF/NK). Fbm k = noise cells across the whole width.
/// Pixel-scale layers keep their feature size in pixels at any width (k scales with the width), continental layers
/// (elevation 4, mountain mask 3, moisture 5, temperature 4) keep a fixed k, so continents keep their count and grow
/// with the map. EOct adds elevation octaves so the finest coastline detail stays ~6 px.
/// </summary>
public sealed class GenScale
{
    public readonly double Ks;      // width / 1024
    public readonly int Warp, Ridge, Hill, Micro, Moist, Temp, Tone, Dune, Jit, EOct;
    public readonly double WarpA;   // domain-warp amplitude, px
    public const double JitA = 4.5; // province border jitter, px

    public GenScale(int width)
    {
        Ks = width / 1024.0;
        Warp = Kf(6); WarpA = .387 * width / Warp;
        Ridge = Kf(8); Hill = Kf(24); Micro = Kf(56); Moist = Kf(22); Temp = Kf(18); Tone = Kf(40); Dune = Kf(40); Jit = Kf(64);
        EOct = 7 + (int)JsMath.Round(Math.Log2(Ks / 1.5));
    }

    int Kf(int k) => Math.Max(1, (int)JsMath.Round(k * Ks));
}
