using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PaxPixelia.Core;

namespace PaxPixelia.World;

/// <summary>Terrain: relief, sea level, heights, climate biomes, shaded pixel-art colour (mockup wgA/seaLevel/wgH/wgBio/wgCol).</summary>
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
        int w = _w, h = _h, s = _s, hillK = _k.Hill, microK = _k.Micro, moistK = _k.Moist, tempK = _k.Temp;
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
                double m = (Fbm(x, y, 5, 4, s + 50) - .5) * 1.9 + .5 + (Fbm(x, y, moistK, 3, s + 51) - .5) * .4 + (Noise.H2(x, y, s + 52) - .5) * .07;
                double t = 1 - lat * 1.1 + (Fbm(x, y, 4, 3, s + 70) - .5) * .3 + (Fbm(x, y, tempK, 2, s + 71) - .5) * .12 + (Noise.H2(x, y, s + 53) - .5) * .04 - hv * .5;
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

    /// <summary>Pixel-art base colour: biome palette, hill tint, per-pixel tone noise, dune ripples, hillshade
    /// posterised to 0.06 steps; water in 4 depth bands with a light coast line. Rivers are drawn by the map, not baked.
    /// High mountains (the mockup painted every pixel above .82 white, so ridges read as white ribbons) get a ragged
    /// snow line with a dithered edge, cool snow, and speckled rock flanks with lit crests.</summary>
    void Colour()
    {
        int w = _w, h = _h, s = _s, toneK = _k.Tone, duneK = _k.Dune, snowK = _k.Micro;
        var land = _d.Land; var hgt = _d.Height; var biome = _d.Biome; var col = _d.BaseColor;
        var palette = Data.BiomeColor;
        Parallel.For(0, h, _po, [MethodImpl(Hot)] (y) =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x, xl = row + (x + w - 1) % w, xr = row + (x + 1) % w;
                double f;
                if (land[i] != 0)
                {
                    int b = biome[i];
                    double hv = hgt[i];
                    double r = palette[b][0], g = palette[b][1], bl = palette[b][2];
                    if (hv > .34 && hv <= .6 && b > 3) { r = r * .72 + 114 * .28; g = g * .72 + 104 * .28; bl = bl * .72 + 82 * .28; }
                    double hn = Noise.H2(x, y, s + 999), tone = Fbm(x, y, toneK, 2, s + 60);
                    f = b == 5 || b == 8 || b == 12 ? (hn < .3 ? .78 : hn > .84 ? 1.13 : 1) : .965 + hn * .07; // forests: dark/light canopy speckle
                    if (b is 2 or 3)
                    {
                        double line = .9 + .1 * (Fbm(x, y, snowK, 2, s + 81) * 2 - 1);
                        bool snow = hv > line || (hv > line - .014 && ((x + y) & 1) == 0);   // two tones dithered along the edge
                        bool crest = hv >= hgt[xl] && hv >= hgt[xr] || y > 0 && y < h - 1 && hv >= hgt[i - w] && hv >= hgt[i + w];
                        if (snow) { r = 228; g = 233; bl = 238; f = crest ? 1.06 : 1; }
                        else { r = palette[3][0]; g = palette[3][1]; bl = palette[3][2]; f = (hn < .16 ? .84 : hn > .9 ? 1.1 : 1) * (crest ? 1.14 : 1); }
                    }
                    f *= .92 + tone * .16;
                    if (b == 14 || b == 11) f *= 1 + .04 * Math.Sin(x * .5 + y * .2 + Fbm(x, y, duneK, 1, s + 5) * 6);
                    double gx = (double)hgt[xr] - hgt[xl], gy = (y < h - 1 ? hgt[i + w] : hv) - (y > 0 ? hgt[i - w] : hv);
                    double shade = 1 + (gx + gy) * 10;
                    shade = JsMath.Round(shade / .06) * .06;
                    shade = shade < .55 ? .55 : shade > 1.4 ? 1.4 : shade;
                    f *= shade;
                    Put(col, i, r * f, g * f, bl * f);
                    continue;
                }
                double dep = -hgt[i];
                int cr, cg, cb;
                if (land[xl] != 0 || land[xr] != 0 || (y > 0 && land[i - w] != 0) || (y < h - 1 && land[i + w] != 0)) (cr, cg, cb) = (80, 116, 134);
                else if (dep < .035) (cr, cg, cb) = (64, 99, 120);
                else if (dep < .1) (cr, cg, cb) = (47, 77, 100);
                else if (dep < .25) (cr, cg, cb) = (37, 59, 82);
                else (cr, cg, cb) = (28, 44, 64);
                f = Noise.H2(x, y, s + 77) < .012 ? 1.16 : 1;   // rare glints
                Put(col, i, cr * f, cg * f, cb * f);
            }
        });
    }

    static void Put(byte[] rgba, int i, double r, double g, double b)
    {
        int o = i * 4;
        rgba[o] = Clamp(r); rgba[o + 1] = Clamp(g); rgba[o + 2] = Clamp(b); rgba[o + 3] = 255;
    }

    static byte Clamp(double v) => v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)(int)v;
}
