using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PaxPixelia.Core;
using static PaxPixelia.World.TerrainPalette;

namespace PaxPixelia.World;

/// <summary>
/// Pixel-art terrain colour (ART_BIBLE §2–3, concept sheet terrain_tiles.png). Every pixel is one step of a 5-tone
/// material ramp: step = hillshade from the top left + clustered tone patches (+1 on crests), posterised with a narrow
/// Bayer band. On top come clustered details instead of per-pixel salt: tree crowns (forest, taiga, jungle, acacias),
/// dunes, steppe streaks, meadow tufts, tundra moss and frost, swamp pools, rock strokes and ragged snow caps; sand
/// beaches on warm low coasts, cliff shadows, green floodplains along desert rivers; water in dithered depth bands with
/// a light coast line. Material and step are kept per pixel (TerrMaterial / TerrStep) for a future palette LUT.
/// </summary>
internal sealed partial class WorldBuilder
{
    const double ShadeGain = 10 / .15 * .8;   // height gradient → ramp steps (ART_BIBLE: step = (shade − 1) / 0.15)

    [MethodImpl(Hot)]
    void Colour()
    {
        int w = _w, h = _h;
        var mat = _d.TerrMaterial = new byte[_n];
        var stp = _d.TerrStep = new byte[_n];
        var col = _d.BaseColor; var land = _d.Land;
        var anchors = TreeAnchors();
        var ramps = Ramps;
        Parallel.For(0, h, _po, [MethodImpl(Hot)] (y) =>
        {
            for (int x = 0, i = y * w; x < w; x++, i++)
            {
                int t = land[i] != 0 ? LandTexel(x, y, i, anchors) : WaterTexel(x, y, i);
                int m = t >> 3, st = t & 7, c = ramps[m][st], o = i * 4;
                mat[i] = (byte)m; stp[i] = (byte)st;
                col[o] = (byte)(c >> 16); col[o + 1] = (byte)(c >> 8); col[o + 2] = (byte)c; col[o + 3] = 255;
            }
        });
    }

    static int Tex(int material, int step) => material << 3 | (step < 0 ? 0 : step > 4 ? 4 : step);

    [MethodImpl(Hot)]
    int LandTexel(int x, int y, int i, byte[] anchors)
    {
        int w = _w, h = _h, s = _s;
        var land = _d.Land; var hgt = _d.Height;
        int row = y * w, xl = row + (x == 0 ? w - 1 : x - 1), xr = row + (x == w - 1 ? 0 : x + 1);
        int up = y > 0 ? i - w : i, dn = y < h - 1 ? i + w : i;
        double hv = hgt[i];
        double hL = land[xl] != 0 ? hgt[xl] : 0, hR = land[xr] != 0 ? hgt[xr] : 0, hU = land[up] != 0 ? hgt[up] : 0, hD = land[dn] != 0 ? hgt[dn] : 0;
        double gx = hR - hL, gy = hD - hU, shade = (gx + gy) * ShadeGain;
        bool coast = land[xl] == 0 || land[xr] == 0 || land[up] == 0 || land[dn] == 0;
        bool crest = hv > .34 && (hv >= hL && hv >= hR || hv >= hU && hv >= hD);
        int b = _d.Biome[i];
        double tone = GenNoise.Fbm(x, y, _k.Tone, 2, s + 60, w);

        if (b is 2 or 3) return MountainTexel(x, y, hv, shade, tone, crest);
        if (b == 1)
        {
            int q = TerrainGlyphs.Quant(2.7 + shade * .8 + (tone - .5) * 1.1, x, y);
            return Speck(x, y, 9, .5, s + 110) is 1 or 2 ? Tex(Snow, q - 1) : Tex(Snow, q);   // sastrugi
        }

        double lvl = 2 + shade + (tone - .5) * 1.3 + (crest ? .8 : 0);
        // coasts: sand beaches on warm, low, gentle shores; a shadow line under cliffs
        if (b >= 8 && b != 12 && hv < .1 && Math.Abs(gx) + Math.Abs(gy) < .05)
        {
            int d = coast ? 1 : NearWater(x, y, i) ? 2 : 0;
            if (d == 1 || d == 2 && Noise.H2(x >> 1, y >> 1, s + 111) < .75)
                return Tex(Sand, TerrainGlyphs.Quant((d == 1 ? 2.6 : 2.1) + shade * .5 + (tone - .5) * .8, x, y));
        }
        if (coast && hv > .3) lvl -= 1.2;
        if (b is 6 or 11 or 13 or 14)
        {
            int d = RiverDistance(x, y);
            if (d <= 1 || d == 2 && Noise.H2(x >> 1, y >> 1, s + 120) < .6) return Tex(Floodplain, TerrainGlyphs.Quant(lvl, x, y));
        }

        int ground = hv > .34 + (TerrainGlyphs.Bayer(x, y) - .5) * .03 && b > 3 ? Hill(b) : b;   // dithered foot of the hills
        int code = Tree(x, y, anchors, out var kind, out int anchorBiome);
        int qn = TerrainGlyphs.Quant(lvl, x, y);
        if (code != 0)
        {
            if (kind == TerrainGlyphs.Kind.Acacia)
                return code == 1 ? Tex(ground, qn - 2) : code == 5 ? Tex(Acacia, 0) : Tex(Acacia, code - 1 + (qn - 2));
            return code == 1 ? Tex(anchorBiome, 0) : Tex(anchorBiome, code + (qn - 2));
        }
        switch (b)
        {
            case 8: return Tex(ground, Math.Clamp(qn - 1, 0, 1));                     // forest floor between crowns
            case 5: return Tex(ground, Math.Clamp(qn - 1, 0, 2));
            case 12: return Tex(ground, 0);
            case 14:                                                                  // sand seas with dunes, else flat desert
                if (GenNoise.Fbm(x, y, Math.Max(2, w / 220), 2, s + 9, w) > .5)
                {
                    double u = x * .055 + y * .105 + GenNoise.Fbm(x, y, _k.Dune, 2, s + 8, w) * 1.6, f = u - Math.Floor(u);
                    int dune = f < .1 ? 4 : f < .3 ? 3 : f > .9 ? 1 : 2;
                    return Tex(ground, dune + (int)Math.Round(shade * .6));
                }
                return Speck(x, y, 13, .35, s + 112) switch { 1 => Tex(ground, 0), 2 => Tex(ground, 1), _ => Tex(ground, qn) };
            case 11 or 6:                                                             // wind streaks
            {
                double u = x * .0557 + y * .1432 + GenNoise.Fbm(x, y, _k.Dune, 1, s + 5, w) * .95, f = u - Math.Floor(u);
                if (f < .045 && qn >= 2 && Noise.H2(x / 5, y, s + 113) < .6) return Tex(ground, qn + 1);
                if (f > .5 && f < .525) return Tex(ground, qn - 1);
                return Tex(ground, qn);
            }
            case 10:                                                                  // wind-combed rows in patches
                return qn == 2 && (y + x / 6) % 4 == 0 && Noise.H2(x / 8, y / 6, s + 114) < .35 ? Tex(ground, 3) : Tex(ground, qn);
            case 9:                                                                   // grass tufts
                return Speck(x, y, 5, .7, s + 115) switch { 1 or 2 => Tex(ground, qn + 1), 3 => Tex(ground, qn - 1), _ => Tex(ground, qn) };
            case 4:                                                                   // moss patches and frost
            {
                if (Speck(x, y, 11, .3, s + 116) is 1 or 2) return Tex(Snow, 2);
                double n = GenNoise.Fbm(x, y, Math.Max(2, w / 5), 2, s + 30, w);
                return Tex(ground, n > .6 ? qn - 1 : n < .34 ? qn + 1 : qn);
            }
            case 7:                                                                   // pools with a lit rim, reeds
            {
                int pk = Math.Max(2, w / 6);
                double n = GenNoise.Fbm(x, y, pk, 2, s + 31, w);
                if (n > .6)
                    return n > .66 ? Tex(Pools, GenNoise.Fbm(x - 1, y - 1, pk, 2, s + 31, w) <= .66 ? 3 : 1) : Tex(Pools, 2);
                return Speck(x, y, 4, .6, s + 117) switch { 1 => Tex(ground, 4), 3 => Tex(ground, 3), _ => Tex(ground, qn) };
            }
            default: return Tex(ground, qn);
        }
    }

    /// <summary>Rock with lit crests and dark strokes; above a ragged snow line (dithered edge) snow, lit on the
    /// slopes facing the light and blue-grey on the others.</summary>
    [MethodImpl(Hot)]
    int MountainTexel(int x, int y, double hv, double shade, double tone, bool crest)
    {
        double line = .9 + .1 * (GenNoise.Fbm(x, y, _k.Micro, 2, _s + 81, _w) * 2 - 1);
        if (hv > line || hv > line - .02 && (hv - line + .02) / .02 > TerrainGlyphs.Bayer(x, y))
            return Tex(Snow, TerrainGlyphs.Quant(3 + shade * .8 + (crest ? .8 : 0), x, y));
        // rock facets: small noise planes break the smooth hillshade into rocky patches
        double facet = GenNoise.Fbm(x, y, Math.Max(2, _w / 6), 2, _s + 82, _w) - .5;
        int q = TerrainGlyphs.Quant(2 + shade * .6 + facet * 2.2 + (tone - .5) * .6 + (crest ? .9 : 0), x, y);
        if (q <= 2) switch (Speck(x, y, 6, .5, _s + 118)) { case 1: return Tex(Rock, 0); case 2: return Tex(Rock, 1); }
        return Tex(Rock, q);
    }

    [MethodImpl(Hot)]
    int WaterTexel(int x, int y, int i)
    {
        int w = _w, h = _h;
        var land = _d.Land;
        int row = y * w;
        if (land[row + (x == 0 ? w - 1 : x - 1)] != 0 || land[row + (x == w - 1 ? 0 : x + 1)] != 0 || y > 0 && land[i - w] != 0 || y < h - 1 && land[i + w] != 0)
            return Tex(Water, 4);                                                     // coast line
        double dep = -_d.Height[i];
        double lvl = dep < .035 ? 3.5 - dep / .035 : dep < .1 ? 2.5 - (dep - .035) / .065 : dep < .25 ? 1.5 - (dep - .1) / .15 : .5 - (dep - .25) / .5;
        lvl += (GenNoise.Fbm(x, y, _k.Tone, 2, _s + 77, w) - .5) * .25;
        int q = Math.Min(3, TerrainGlyphs.Quant(lvl, x, y, .14));                    // depth changes slowly: a narrow dither band
        if (q <= 1 && Speck(x, y, 9, .3, _s + 119) is 1 or 2) q++;                   // swell
        return Tex(Water, q);
    }

    /// <summary>Water within 2 px (4-neighbours at distance 2, diagonals at distance 1).</summary>
    [MethodImpl(Hot)]
    bool NearWater(int x, int y, int i)
    {
        int w = _w, h = _h, row = y * w;
        var land = _d.Land;
        int x2l = row + (x - 2 + w) % w, x2r = row + (x + 2) % w, xl = (x - 1 + w) % w, xr = (x + 1) % w;
        if (land[x2l] == 0 || land[x2r] == 0) return true;
        if (y > 1 && land[i - 2 * w] == 0 || y < h - 2 && land[i + 2 * w] == 0) return true;
        if (y > 0 && (land[row - w + xl] == 0 || land[row - w + xr] == 0)) return true;
        return y < h - 1 && (land[row + w + xl] == 0 || land[row + w + xr] == 0);
    }

    /// <summary>Chebyshev distance to the nearest river pixel within 2 px; 3 if none.</summary>
    [MethodImpl(Hot)]
    int RiverDistance(int x, int y)
    {
        int w = _w, best = 3;
        var river = _d.River;
        for (int dy = -2; dy <= 2; dy++)
        {
            int yy = y + dy;
            if ((uint)yy >= (uint)_h) continue;
            for (int dx = -2; dx <= 2; dx++)
            {
                int xx = x + dx, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (d >= best) continue;
                if (xx < 0) xx += w; else if (xx >= w) xx -= w;
                if (river[yy * w + xx] != 0) best = d;
            }
        }
        return best;
    }

    /// <summary>One tree per jittered 4×4 cell whose anchor pixel is forest (55–92%), taiga, jungle or savanna (rare
    /// acacias); packed kind + offset per cell.</summary>
    byte[] TreeAnchors()
    {
        const int g = TerrainGlyphs.Cell;
        int w = _w, h = _h, s = _s, aw = w / g, ah = (h + g - 1) / g;
        var a = new byte[aw * ah];
        var land = _d.Land; var biome = _d.Biome;
        Parallel.For(0, ah, _po, [MethodImpl(Hot)] (cy) =>
        {
            for (int cx = 0; cx < aw; cx++)
            {
                int ox = (int)(Noise.H2(cx, cy, s + 101) * g), oy = (int)(Noise.H2(cx, cy, s + 102) * g), ay = cy * g + oy;
                if (ay >= h) continue;
                int i = ay * w + cx * g + ox;
                if (land[i] == 0) continue;
                double r = Noise.H2(cx, cy, s + 103), pick = Noise.H2(cx, cy, s + 104);
                var kind = biome[i] switch
                {
                    8 => r < .92 ? pick < .35 ? TerrainGlyphs.Kind.ForestBig : TerrainGlyphs.Kind.ForestSmall : TerrainGlyphs.Kind.None,
                    5 => r < .75 ? TerrainGlyphs.Kind.Conifer : TerrainGlyphs.Kind.None,
                    12 => pick < .5 ? TerrainGlyphs.Kind.JungleBig : TerrainGlyphs.Kind.JungleSmall,
                    13 => r < .1 ? TerrainGlyphs.Kind.Acacia : TerrainGlyphs.Kind.None,
                    _ => TerrainGlyphs.Kind.None,
                };
                if (kind != TerrainGlyphs.Kind.None) a[cy * aw + cx] = TerrainGlyphs.Pack(kind, ox, oy);
            }
        });
        return a;
    }

    /// <summary>The glyph code painted at (x, y) by the last-drawn tree covering it (trees are drawn in row-major
    /// order of their anchors, each shadow before its crown), 0 if none.</summary>
    [MethodImpl(Hot)]
    int Tree(int x, int y, byte[] anchors, out TerrainGlyphs.Kind kind, out int anchorBiome)
    {
        const int g = TerrainGlyphs.Cell, reach = TerrainGlyphs.Reach - 1;
        int w = _w, aw = w / g, ah = anchors.Length / aw, code = 0, bestY = -1, bestX = int.MinValue, bestI = 0;
        kind = TerrainGlyphs.Kind.None;
        int cy0 = Math.Max(0, (y - reach) / g), cy1 = Math.Min(ah - 1, y / g), cx0 = (x - reach + w) / g - aw, cx1 = x / g;
        if (x - reach >= 0) cx0 = (x - reach) / g;
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                byte a = anchors[cy * aw + (cx < 0 ? cx + aw : cx)];
                if (a == 0) continue;
                int ax = cx * g + TerrainGlyphs.Ox(a), ay = cy * g + TerrainGlyphs.Oy(a);
                int v = TerrainGlyphs.At(TerrainGlyphs.KindOf(a), x - ax, y - ay);
                if (v == 0 || ay < bestY || ay == bestY && ax < bestX) continue;
                bestY = ay; bestX = ax; code = v; kind = TerrainGlyphs.KindOf(a); bestI = ay * w + (ax < 0 ? ax + w : ax);
            }
        anchorBiome = code != 0 ? _d.Biome[bestI] : 0;
        return code;
    }

    /// <summary>Small 2×2 details on a jittered grid of g-px cells (a share `keep` of the cells has one): 1 = the
    /// anchor pixel, 2 = right of it, 3 = below it, 4 = diagonal; 0 = none.</summary>
    [MethodImpl(Hot)]
    int Speck(int x, int y, int g, double keep, int salt)
    {
        for (int k = 0; k < 4; k++)
        {
            int ax = x - (k & 1), ay = y - (k >> 1);
            if (ay < 0) continue;
            if (ax < 0) ax += _w;
            int cx = ax / g, cy = ay / g;
            if (Noise.H2(cx, cy, salt + 2) >= keep) continue;
            if (cx * g + (int)(Noise.H2(cx, cy, salt) * g) == ax && cy * g + (int)(Noise.H2(cx, cy, salt + 1) * g) == ay) return k + 1;
        }
        return 0;
    }
}
