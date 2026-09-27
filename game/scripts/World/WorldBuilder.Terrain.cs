using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PaxPixelia.Core;

namespace PaxPixelia.World;

/// <summary>Terrain: relief, sea level, heights and climate biomes (the approved mockup's wgA/seaLevel/wgH/wgBio).</summary>
internal sealed partial class WorldBuilder
{
    const double LandShare = .37;

    /// <summary>Domain-warped fbm elevation (poles pushed down) + ridged mountains under a large-scale mask.</summary>
    void Relief()
    {
        int w = _w, h = _h, s = _s, warpK = _k.Warp, ridgeK = _k.Ridge, eOct = _k.EOct;
        double warpA = _k.WarpA;
        var bas = _base = new float[_n];
        var ridge = _ridge = new float[_n];
        var rowMin = new double[h];
        var rowMax = new double[h];
        Parallel.For(0, h, _po, [MethodImpl(Hot)] (y) =>
        {
            double pl = Math.Abs((double)y / h - .5) * 2, pole = .2 * pl * pl * pl, mn = 1e9, mx = -1e9;
            for (int x = 0, i = y * w; x < w; x++, i++)
            {
                double qx = x + warpA * (Fbm(x, y, warpK, 3, s + 7) * 2 - 1);
                double qy = y + warpA * (Fbm(x, y, warpK, 3, s + 13) * 2 - 1);
                double e = Fbm(qx, qy, 4, eOct, s) - pole;
                bas[i] = (float)e;
                if (e < mn) mn = e;
                if (e > mx) mx = e;
                double mask = Fbm(x, y, 3, 3, s + 21);
                if (mask <= .47) { ridge[i] = 0; continue; }
                double r = 1 - Math.Abs(Fbm(qx, qy, ridgeK, 4, s + 3) - .5) * 7;
                if (r < 0) r = 0;
                ridge[i] = (float)(r * r * Noise.Smooth(.47, .6, mask));
            }
            rowMin[y] = mn; rowMax[y] = mx;
        });
        _min = rowMin.Min(); _max = rowMax.Max();
    }

    /// <summary>Sea level from a 4096-bin histogram so that ~37% is land; then specks are removed:
    /// islands under 14 px sink, lakes under 70 px are filled.</summary>
    void SeaLevel()
    {
        const int bins = 4096;
        int w = _w;
        double mn = _min, rng = _max - _min;
        var bas = _base;
        var hist = new int[bins];
        var gate = new object();
        Parallel.For(0, _h, _po, () => new int[bins], [MethodImpl(Hot)] (y, _, local) =>
        {
            for (int i = y * w, e = i + w; i < e; i++) local[Math.Min(bins - 1, (int)((bas[i] - mn) / rng * bins))]++;
            return local;
        }, local => { lock (gate) for (int b = 0; b < bins; b++) hist[b] += local[b]; });

        long acc = 0;
        double target = _n * (1 - LandShare), sea = mn;
        for (int b = 0; b < bins; b++)
        {
            acc += hist[b];
            if (acc >= target) { sea = mn + (b + 1) / (double)bins * rng; break; }
        }
        _sea = sea;

        var land = _d.Land;
        Parallel.For(0, _h, _po, [MethodImpl(Hot)] (y) => { for (int i = y * w, e = i + w; i < e; i++) land[i] = (byte)(bas[i] > sea ? 1 : 0); });
        var lab = Components.Label(land, w, _h, _po);
        Parallel.For(0, _h, _po, [MethodImpl(Hot)] (y) =>
        {
            for (int i = y * w, e = i + w; i < e; i++)
            {
                int size = lab.Size[lab.Id[i]];
                if (land[i] != 0 ? size < 14 : size < 70) land[i] ^= 1;
            }
        });
    }

    /// <summary>Heights (land 0..~1.2 from coast distance proxy + ridges + hills; water = negative depth) and biomes
    /// from latitude/altitude temperature and noise moisture.</summary>
    void HeightsAndBiomes()
    {
        int w = _w, h = _h, s = _s, hillK = _k.Hill, microK = _k.Micro, moistK = _k.Moist, tempK = _k.Temp, clusterK = Math.Max(2, _w / 3);
        double sea = _sea, depth = _sea - _min;
        var bas = _base; var ridge = _ridge; var land = _d.Land; var hgt = _d.Height; var biome = _d.Biome;
        Parallel.For(0, h, _po, [MethodImpl(Hot)] (y) =>
        {
            double lat = Math.Abs((double)y / h - .5) * 2;
            for (int x = 0, i = y * w; x < w; x++, i++)
            {
                double d = bas[i] - sea;
                if (land[i] == 0) { hgt[i] = (float)(Math.Min(-.001, d) / depth); continue; }
                double dd = Math.Max(d, .001);
                float hf = (float)(Math.Min(1, dd / .2) * .28 + ridge[i] * .82 * Noise.Smooth(0, .05, dd)
                    + .1 * Math.Max(0, Fbm(x, y, hillK, 3, s + 33) - .5) * 2 + .05 * Fbm(x, y, microK, 2, s + 34));
                hgt[i] = hf;
                double hv = hf;
                // biome edges: ragged in 3-px clusters, not per-pixel salt
                double edge = (Fbm(x, y, clusterK, 1, s + 52) - .5) * 1.6 + (Noise.H2(x >> 1, y >> 1, s + 52) - .5) * .5;
                double m = (Fbm(x, y, 5, 4, s + 50) - .5) * 1.9 + .5 + (Fbm(x, y, moistK, 3, s + 51) - .5) * .4 + edge * .07;
                double t = 1 - lat * 1.1 + (Fbm(x, y, 4, 3, s + 70) - .5) * .3 + (Fbm(x, y, tempK, 2, s + 71) - .5) * .12 + edge * .04 - hv * .5;
                biome[i] = BiomeOf(t, m, hv);
            }
        });
    }

    static byte BiomeOf(double t, double m, double h)
    {
        if (t < .08) return 1;                 // glacier
        if (h > .82) return 2;                 // peaks
        if (h > .6) return 3;                  // mountains
        if (t < .2) return 4;                  // tundra
        if (t < .35) return (byte)(m > .5 ? 5 : 6);
        if (t < .7) return (byte)(m > .7 && h < .12 ? 7 : m > .55 ? 8 : m > .44 ? 9 : m > .32 ? 10 : 11);
        return (byte)(m > .6 ? 12 : m > .42 ? 13 : 14);
    }
}
