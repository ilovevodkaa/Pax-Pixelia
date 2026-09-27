using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PaxPixelia.Core;

namespace PaxPixelia.World;

/// <summary>
/// Rivers from a drainage network (replaces the mockup's greedy descent, whose parallel tracers left «ladders» of
/// straight lines on flat land):
///  1. every land pixel drains to one of its 8 neighbours — a priority flood from all water over a drainage elevation
///     (height + raw elevation + broad noise valleys), which also fills pits, so everything reaches a sea or a lake;
///  2. rain (by biome) is accumulated downstream;
///  3. channels whose catchment passes a threshold become rivers: traced from their heads, longest first, so
///     tributaries end exactly on the river they join;
///  4. the pixel paths are smoothed, and stretches that D8 flow left ruler-straight swing gently sideways.
/// Deterministic and independent of thread count (the only parallel pass writes per-row outputs).
/// </summary>
internal sealed partial class WorldBuilder
{
    const double RiverDrain = .5;       // raw-elevation weight: flat interiors of big continents still drain to the coast
    const double ValleyAmp = .025;      // broad noise valleys that gather the rain on flat land
    const int Levels = 1 << 15;         // flood priority buckets
    const float RiverCatch = 2600;      // rain-weighted catchment (px at 2560×1440) where a river starts
    const int RiverMinLength = 28, TributaryMinLength = 14;
    const int PitSpread = 160;          // flood levels over which the cells of a filled pit are spread by noise

    /// <summary>Rain per pixel by biome: dry steppes and deserts feed few rivers, jungles and mountains many.</summary>
    static readonly float[] Rain = { 0, .3f, .8f, 1f, .4f, .9f, .4f, 1f, 1f, .8f, .6f, .35f, 1.2f, .45f, .05f };

    [MethodImpl(Hot)]
    void Rivers()
    {
        int w = _w, h = _h, n = _n, s = _s, valleyK = Math.Max(2, _w / 48), pitK = Math.Max(2, _w / 20);
        var land = _d.Land; var hgt = _d.Height; var biome = _d.Biome; var river = _d.River; var bas = _base;
        double sea = _sea;

        // ---- 1. drainage elevation, quantised to flood levels. The valleys are smooth (≥ 24 px), so their noise is
        //         sampled every 4 px and interpolated.
        int gw = w / 4, gh = h / 4 + 2;
        var valley = new float[gw * gh];
        // (single-threaded on purpose: this runs beside the province passes, which hold every pool thread)
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++) valley[gy * gw + gx] = (float)(ValleyAmp * Fbm(gx * 4, gy * 4, valleyK, 2, s + 61));
        var elev = new float[n];
        float eMin = float.MaxValue, eMax = float.MinValue;
        for (int y = 0; y < h; y++)
        {
            int g0 = (y >> 2) * gw;
            float ty = (y & 3) * .25f;
            for (int x = 0, i = y * w; x < w; x++, i++)
            {
                if (land[i] == 0) continue;
                int gx = x >> 2, gx1 = gx + 1 == gw ? 0 : gx + 1;
                float tx = (x & 3) * .25f;
                float top = valley[g0 + gx] + (valley[g0 + gx1] - valley[g0 + gx]) * tx;
                float bot = valley[g0 + gw + gx] + (valley[g0 + gw + gx1] - valley[g0 + gw + gx]) * tx;
                float e = (float)(hgt[i] + RiverDrain * (bas[i] - sea)) + top + (bot - top) * ty;
                elev[i] = e;
                if (e < eMin) eMin = e;
                if (e > eMax) eMax = e;
            }
        }
        if (eMin > eMax) return;                                   // no land at all
        float scale = (Levels - 1) / Math.Max(1e-6f, eMax - eMin);

        // ---- 2. priority flood from the coasts (bucket queue, FIFO inside a bucket): receiver + upstream order
        var rcv = new int[n];
        var next = new int[n];
        var head = new int[Levels]; var tail = new int[Levels];
        Array.Fill(head, -1);
        Span<int> nb = stackalloc int[8];
        int landCount = 0;
        for (int i = 0; i < n; i++)
        {
            if (land[i] == 0) { rcv[i] = -3; continue; }                // water: never queued
            rcv[i] = -2;                                           // land not reached yet
            landCount++;
            int cnt = Neighbours(i, w, h, nb);
            for (int k = 0; k < cnt; k++)
            {
                if (land[nb[k]] != 0) continue;
                rcv[i] = nb[k];                                    // drains straight into the water
                int lv = (int)((elev[i] - eMin) * scale);
                next[i] = -1;
                if (head[lv] < 0) head[lv] = i; else next[tail[lv]] = i;
                tail[lv] = i;
                break;
            }
        }
        var order = new int[landCount];
        int no = 0;
        for (int lv = 0; lv < Levels; lv++)
            for (int i = head[lv]; i >= 0; i = next[i])
            {
                order[no++] = i;
                int cnt = Neighbours(i, w, h, nb);
                for (int k = 0; k < cnt; k++)
                {
                    int j = nb[k];
                    if (rcv[j] != -2) continue;
                    rcv[j] = i;
                    int lj = (int)((elev[j] - eMin) * scale);
                    // A pit is filled up to its spill level. Its cells are queued in the order of a smooth noise just above
                    // that level, so channels across the filled flat meander instead of running as straight BFS rays.
                    if (lj < lv) lj = Math.Min(Levels - 1, lv + (int)(PitSpread * Fbm(j % w, j / w, pitK, 1, s + 62)));
                    next[j] = -1;
                    if (head[lj] < 0) head[lj] = j; else next[tail[lj]] = j;
                    tail[lj] = j;
                }
            }
        if (no < landCount) Array.Resize(ref order, no);           // land cut off from all water (none in practice)

        // ---- 3. rain accumulated downstream (reverse flood order = upstream first)
        var acc = elev;                                            // reuse: elevation is no longer needed
        foreach (int i in order) acc[i] = Rain[biome[i]];
        for (int k = order.Length - 1; k >= 0; k--)
        {
            int i = order[k], r = rcv[i];
            if (r >= 0 && land[r] != 0) acc[r] += acc[i];
        }

        // ---- 4. channels: heads are river pixels no other river pixel drains into; distance to the mouth ranks them
        float thr = RiverCatch * (float)(n / (2560.0 * 1440.0));
        var len = next;                                            // reuse: flood lists are done
        var headOf = new List<int>();
        foreach (int i in order)
        {
            if (acc[i] < thr) { len[i] = -1; continue; }
            int r = rcv[i];
            len[i] = r >= 0 && land[r] != 0 ? len[r] + 1 : 1;
        }
        var fed = new bool[n];
        foreach (int i in order) if (len[i] > 0 && rcv[i] >= 0 && land[rcv[i]] != 0) fed[rcv[i]] = true;
        foreach (int i in order) if (len[i] > 0 && !fed[i]) headOf.Add(i);
        headOf.Sort((a, b) => len[a] != len[b] ? len[b].CompareTo(len[a]) : a.CompareTo(b));

        // ---- 5. trace: main stems first; a tributary stops on the pixel where it meets an earlier river
        var owner = new int[n];                                    // traced river index + 1 per pixel
        var paths = new List<List<int>>();
        var path = new List<int>(512);
        foreach (int hd in headOf)
        {
            path.Clear();
            int i = hd, joins = -1;
            // no streams drawn over ice: a river starts where it leaves the glaciers and peaks
            while (biome[i] is 1 or 2 && rcv[i] >= 0 && land[rcv[i]] != 0 && owner[rcv[i]] == 0) i = rcv[i];
            while (true)
            {
                path.Add(i);
                int r = rcv[i];
                if (r < 0 || land[r] == 0) { if (r >= 0) path.Add(r); break; }   // the mouth: one pixel into the water
                if (owner[r] != 0) { joins = r; path.Add(r); break; }
                i = r;
            }
            int landPx = path.Count - 1;
            if (landPx < (joins >= 0 ? TributaryMinLength : RiverMinLength)) continue;
            int id = paths.Count + 1;
            for (int k = 0; k < landPx; k++) owner[path[k]] = id;
            paths.Add(new List<int>(path));
        }

        // ---- 6. polylines (main stems first, so a tributary can end on its river's final line)
        var fx = new double[paths.Count][]; var fy = new double[paths.Count][];
        for (int r = 0; r < paths.Count; r++)
        {
            var p = paths[r];
            for (int k = 0; k < p.Count; k++) if (land[p[k]] != 0) river[p[k]] = 1;
            Line(p, r, w, s, owner, paths, fx, fy);
            _d.Rivers.Add(Polyline(fx[r], fy[r], p, acc, thr));
        }
    }

    const double MeanderAmp = 2.6, MeanderWave = 26;   // px: sideways swing of straight stretches, and its wavelength

    /// <summary>
    /// Final centre line of river r, one point per path pixel: x unwrapped to stay continuous, ±4 px moving average,
    /// then straight stretches (D8 flow runs along the 8 compass directions) swing gently sideways. The ends stay put:
    /// the mouth, and a tributary's last point, which lies on the final line of the river it joins.
    /// </summary>
    static void Line(List<int> path, int r, int w, int seed, int[] owner, List<List<int>> all, double[][] fx, double[][] fy)
    {
        int n = path.Count;
        var px = new double[n]; var py = new double[n];
        double prev = path[0] % w, half = w / 2.0;
        for (int k = 0; k < n; k++)
        {
            int j = path[k];
            double x = j % w + .5;
            while (x - prev > half) x -= w;
            while (prev - x > half) x += w;
            prev = x;
            px[k] = x; py[k] = j / w + .5;
        }
        var sx = new double[n]; var sy = new double[n];
        for (int k = 0; k < n; k++)
        {
            double ax = 0, ay = 0; int c = 0;
            for (int q = Math.Max(0, k - 4), e = Math.Min(n - 1, k + 4); q <= e; q++) { ax += px[q]; ay += py[q]; c++; }
            sx[k] = ax / c; sy[k] = ay / c;
        }
        sx[0] = px[0]; sy[0] = py[0];
        int parent = owner[path[n - 1]] - 1;
        if (parent >= 0 && parent < r)
        {
            int at = all[parent].IndexOf(path[n - 1]);
            double x = fx[parent][at];
            while (x - px[n - 1] > half) x -= w;
            while (px[n - 1] - x > half) x += w;
            sx[n - 1] = x; sy[n - 1] = fy[parent][at];
        }
        else { sx[n - 1] = px[n - 1]; sy[n - 1] = py[n - 1]; }

        var ox = (double[])sx.Clone(); var oy = (double[])sy.Clone();
        for (int k = 1; k < n - 1; k++)
        {
            int a = Math.Max(0, k - 8), b = Math.Min(n - 1, k + 8);
            double arc = 0;
            for (int q = a; q < b; q++) arc += Math.Sqrt((sx[q + 1] - sx[q]) * (sx[q + 1] - sx[q]) + (sy[q + 1] - sy[q]) * (sy[q + 1] - sy[q]));
            double cx = sx[b] - sx[a], cy = sy[b] - sy[a], chord = Math.Sqrt(cx * cx + cy * cy);
            if (arc < 1e-6 || chord < 1e-6) continue;
            double straight = Math.Clamp((chord / arc - .9) / .08, 0, 1);
            double taper = Math.Min(1, Math.Min(k, n - 1 - k) / 8.0);
            double off = MeanderAmp * straight * taper * Wave(k / MeanderWave, seed, r);
            ox[k] = sx[k] - cy / chord * off; oy[k] = sy[k] + cx / chord * off;
        }
        fx[r] = ox; fy[r] = oy;
    }

    /// <summary>A meander curve in [-1, 1]: a sine (bends of a river come at a fairly steady spacing) whose phase and
    /// size drift with smooth value noise, a different curve per river.</summary>
    static double Wave(double t, int seed, int r)
    {
        int i = (int)Math.Floor(t / 2);
        double f = t / 2 - i, u = f * f * (3 - 2 * f);
        double a = Noise.H2(i, r, seed + 63), b = Noise.H2(i + 1, r, seed + 63), v = a + (b - a) * u;
        return Math.Sin(Math.Tau * t + Math.Tau * Noise.H2(r, 7, seed + 64) + 2 * v) * (.65 + .35 * v);
    }

    /// <summary>The 8 neighbours of pixel i in a fixed order (sides first), x wrapped, none beyond the top/bottom rows.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Neighbours(int i, int w, int h, Span<int> nb)
    {
        int y = i / w, x = i - y * w;
        int l = x == 0 ? i + w - 1 : i - 1, r = x == w - 1 ? i - w + 1 : i + 1, c = 0;
        nb[c++] = r; nb[c++] = l;
        if (y > 0) nb[c++] = i - w;
        if (y < h - 1) nb[c++] = i + w;
        if (y > 0) { nb[c++] = r - w; nb[c++] = l - w; }
        if (y < h - 1) { nb[c++] = r + w; nb[c++] = l + w; }
        return c;
    }

    /// <summary>Every 2nd point of the final line (plus the last), each with its flow.</summary>
    static WorldData.RiverPath Polyline(double[] fx, double[] fy, List<int> path, float[] acc, float thr)
    {
        int n = fx.Length, m = n / 2 + 1, o = 0;
        var xs = new float[m]; var ys = new float[m]; var fl = new float[m];
        for (int k = 0; k < n - 1; k += 2) { xs[o] = (float)fx[k]; ys[o] = (float)fy[k]; fl[o++] = Flow(acc[path[Math.Min(k, n - 2)]], thr); }
        xs[o] = (float)fx[n - 1]; ys[o] = (float)fy[n - 1]; fl[o] = fl[o - 1]; o++;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (float y in ys) { minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        return new WorldData.RiverPath { Xs = xs, Ys = ys, Flow = fl, MinY = minY, MaxY = maxY };
    }

    /// <summary>0 at the head of a river, 1 for a big river (~40× the starting catchment): log scale.</summary>
    static float Flow(float a, float thr) => Math.Clamp(MathF.Log(Math.Max(1, a / thr)) / MathF.Log(40), 0, 1);
}
