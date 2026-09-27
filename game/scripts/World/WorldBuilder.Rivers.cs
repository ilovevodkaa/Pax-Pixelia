using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PaxPixelia.Core;

namespace PaxPixelia.World;

/// <summary>
/// Rivers on the coarse drainage network (RiverGrid):
///  1. channels whose rain-weighted catchment passes a threshold become rivers, traced from their heads, longest first;
///     a later river ends on the first earlier river it touches, so tributaries merge into trunks instead of running
///     beside them, and one that would still run close alongside another for long is dropped (no «combs»);
///  2. each river is drawn as a spline through its cells' jittered points, then swings in gentle meanders that grow
///     downstream and on flat land; a tributary ends exactly on the final line of the river it joins;
///  3. big rivers reaching the sea over lowland split into a small delta;
///  4. the river pixels are the rasterised final lines, so WorldData.River and WorldData.Rivers always agree.
/// Sequential and deterministic.
/// </summary>
internal sealed partial class WorldBuilder
{
    const float RiverCatch = 2400;          // rain-weighted catchment (px at 2560×1440) where a river starts
    const int MainMinCells = 9, TributaryMinCells = 5;
    const int Samples = 3;                  // spline points per cell step
    const double MeanderAmp = 2.3, MeanderWave = 26;   // px at 2560 wide: sideways swing and wavelength of a head stream (both grow downstream)

    sealed class Traced
    {
        public List<double> X = new(), Y = new();
        public List<float> F = new();
    }

    [MethodImpl(Hot)]
    void Rivers()
    {
        int cell = _w >= 4096 ? 8 : _w >= 2048 ? 4 : 2;
        var g = new RiverGrid(_d, _base, _sea, _s, cell);
        float thr = RiverCatch * (float)(_n / (2560.0 * 1440.0));
        double ks = _w / 2560.0;

        // ---- channels: heads are channel cells no other channel cell drains into; distance to the mouth ranks them
        var len = new int[g.Count];
        var fed = new bool[g.Count];
        foreach (int c in g.Order)
        {
            if (g.Acc[c] < thr) continue;
            int r = g.Rcv[c];
            len[c] = g.IsLand[r] ? len[r] + 1 : 1;
            if (g.IsLand[r]) fed[r] = true;
        }
        var heads = new List<int>();
        foreach (int c in g.Order) if (len[c] > 0 && !fed[c]) heads.Add(c);
        heads.Sort((a, b) => len[a] != len[b] ? len[b].CompareTo(len[a]) : a.CompareTo(b));

        var owner = new int[g.Count];         // river index + 1 per cell
        var at = new int[g.Count];            // index of the cell's point on its river's final line
        var lines = new List<Traced>();
        var path = new List<int>(512);
        Span<int> nb = stackalloc int[8];
        foreach (int hd in heads)
        {
            // ---- trace
            path.Clear();
            int c = hd, joins = -1;
            while (true)
            {
                path.Add(c);
                int r = g.Rcv[c];
                if (!g.IsLand[r]) { path.Add(r); break; }                        // the mouth cell (water)
                if (owner[r] != 0) { joins = r; path.Add(r); break; }
                int side = -1, cnt = g.Neighbours(c, nb);                        // touching a river downstream: join it now
                for (int k = 0; k < cnt; k++)
                    if (owner[nb[k]] != 0 && g.Rank[nb[k]] < g.Rank[c] && (side < 0 || g.Rank[nb[k]] < g.Rank[side])) side = nb[k];
                if (side >= 0) { joins = side; path.Add(side); break; }
                c = r;
            }
            // no streams drawn over ice: a river starts below the last glacier or peak on its way
            int lastIce = -1;
            for (int k = 0; k < path.Count - 1; k++) if (IsIce(g, path[k])) lastIce = k;
            if (lastIce >= 0) path.RemoveRange(0, lastIce + 1);
            int landCells = path.Count - 1;
            if (landCells < (joins >= 0 ? TributaryMinCells : MainMinCells)) continue;
            // a river that would run close beside another for most of its course is a comb tooth, not a river
            int near = 0;
            for (int k = 0; k < landCells - 4; k++) if (g.AnyWithin(path[k], 2, owner, 0)) near++;
            if (near * 5 > landCells * 2) continue;

            // ---- final line
            int id = lines.Count + 1;
            var t = Line(g, path, joins >= 0 ? lines[owner[joins] - 1] : null, joins >= 0 ? at[joins] : 0, thr, ks, hd);
            if (t == null) continue;
            int kept = (t.X.Count - 1 + Samples - 1) / Samples;               // cells whose point made it into the line
            for (int k = 0; k < Math.Min(kept, landCells); k++) { owner[path[k]] = id; at[path[k]] = k * Samples; }
            lines.Add(t);
        }

        foreach (var t in lines) Rasterise(t);
        int mains = lines.Count;
        for (int r = 0; r < mains; r++) Delta(lines[r], lines, ks);
        foreach (var t in lines) _d.Rivers.Add(ToPath(t));
    }

    bool IsIce(RiverGrid g, int c)
    {
        byte b = _d.Biome[(int)g.Py[c] * _w + (int)g.Px[c]];
        return b is 1 or 2;
    }

    /// <summary>
    /// Final line of a traced cell path: points (x unwrapped), two [¼ ½ ¼] passes to iron out the D8 zigzag, a
    /// Catmull-Rom spline, then meanders. The ends stay put: the head, the mouth (cut at the first water pixel), and a
    /// tributary's end, which is its parent's final point at the join cell. Null when a mouth never reaches water.
    /// </summary>
    [MethodImpl(Hot)]
    Traced Line(RiverGrid g, List<int> path, Traced parent, int parentAt, float thr, double ks, int seedCell)
    {
        int m = path.Count, w = _w;
        double half = w / 2.0;
        var px = new double[m]; var py = new double[m];
        for (int k = 0; k < m; k++)
        {
            double x = g.Px[path[k]], y = g.Py[path[k]];
            if (k == m - 1 && parent != null) { x = parent.X[parentAt]; y = parent.Y[parentAt]; }
            if (k > 0) { while (x - px[k - 1] > half) x -= w; while (px[k - 1] - x > half) x += w; }
            px[k] = x; py[k] = y;
        }
        var tx = new double[m]; var ty = new double[m];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 1; k < m - 1; k++) { tx[k] = .25 * px[k - 1] + .5 * px[k] + .25 * px[k + 1]; ty[k] = .25 * py[k - 1] + .5 * py[k] + .25 * py[k + 1]; }
            for (int k = 1; k < m - 1; k++) { px[k] = tx[k]; py[k] = ty[k]; }
        }

        // Catmull-Rom through the points, flow interpolated between cells
        int ns = (m - 1) * Samples + 1;
        var sx = new double[ns]; var sy = new double[ns]; var sf = new float[ns];
        for (int k = 0; k < m - 1; k++)
        {
            int k0 = Math.Max(0, k - 1), k3 = Math.Min(m - 1, k + 2);
            float f0 = Flow(g.Acc[path[k]], thr), f1 = k + 1 < m - 1 ? Flow(g.Acc[path[k + 1]], thr) : f0;
            for (int q = 0; q < Samples; q++)
            {
                double u = (double)q / Samples, u2 = u * u, u3 = u2 * u;
                int o = k * Samples + q;
                sx[o] = CatmullRom(px[k0], px[k], px[k + 1], px[k3], u, u2, u3);
                sy[o] = CatmullRom(py[k0], py[k], py[k + 1], py[k3], u, u2, u3);
                sf[o] = f0 + (f1 - f0) * (float)u;
            }
        }
        sx[ns - 1] = px[m - 1]; sy[ns - 1] = py[m - 1]; sf[ns - 1] = sf[ns - 2];

        Meander(sx, sy, sf, ks, seedCell);

        // cut at the first water pixel: the mouth, one pixel into the sea or lake
        var t = new Traced();
        for (int k = 0; k < ns; k++)
        {
            // stored at float precision (what WorldData.Rivers keeps), so every test below sees the published points
            t.X.Add((float)sx[k]); t.Y.Add((float)sy[k]); t.F.Add(sf[k]);
            if (k > 0 && !IsLandAt(t.X[k], t.Y[k])) return t;
        }
        return parent != null ? t : null;
    }

    static double CatmullRom(double p0, double p1, double p2, double p3, double u, double u2, double u3) =>
        .5 * (2 * p1 + (p2 - p0) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (3 * p1 - p0 - 3 * p2 + p3) * u3);

    /// <summary>Sideways meanders: a sine whose phase and size drift with smooth noise, a different curve per river,
    /// wider on big rivers and lowland, tapered to nothing at both ends; a swing that would leave the land is halved or
    /// dropped.</summary>
    [MethodImpl(Hot)]
    void Meander(double[] sx, double[] sy, float[] sf, double ks, int seedCell)
    {
        int n = sx.Length;
        if (n < 4) return;
        var arc = new double[n]; var phase = new double[n];
        for (int k = 1; k < n; k++)
        {
            double ds = Math.Sqrt((sx[k] - sx[k - 1]) * (sx[k] - sx[k - 1]) + (sy[k] - sy[k - 1]) * (sy[k] - sy[k - 1]));
            arc[k] = arc[k - 1] + ds;
            phase[k] = phase[k - 1] + ds / (MeanderWave * ks * (1 + .7 * sf[k]));      // bigger rivers swing in longer bends
        }
        double total = arc[n - 1], taperLen = 12 * ks;
        var ox = (double[])sx.Clone(); var oy = (double[])sy.Clone();
        for (int k = 1; k < n - 1; k++)
        {
            double dx = sx[k + 1] - sx[k - 1], dy = sy[k + 1] - sy[k - 1], dl = Math.Sqrt(dx * dx + dy * dy);
            if (dl < 1e-6) continue;
            double e = Math.Min(arc[k], total - arc[k]) / taperLen;
            if (e <= 0) continue;
            double taper = e >= 1 ? 1 : e * e * (3 - 2 * e);
            double hv = HeightAt(sx[k], sy[k]);
            double relief = Math.Clamp((hv - .25) / .35, 0, 1);     // mountain streams run straighter
            double amp = MeanderAmp * ks * (.5 + .9 * sf[k]) * (1 - .6 * relief) * taper;
            double off = amp * Wave(phase[k], _s, seedCell);
            double nx = -dy / dl, ny = dx / dl;
            for (int tries = 0; tries < 2 && off != 0; tries++)
            {
                if (IsLandAt(sx[k] + nx * off, sy[k] + ny * off)) break;
                off = tries == 0 ? off * .5 : 0;
            }
            ox[k] = sx[k] + nx * off; oy[k] = sy[k] + ny * off;
        }
        Array.Copy(ox, sx, n); Array.Copy(oy, sy, n);
    }

    /// <summary>A meander curve in [-1, 1]: bends come at a fairly steady spacing, their phase and size drift.</summary>
    static double Wave(double t, int seed, int r)
    {
        int i = (int)Math.Floor(t / 2);
        double f = t / 2 - i, u = f * f * (3 - 2 * f);
        double a = Noise.H2(i, r, seed + 63), b = Noise.H2(i + 1, r, seed + 63), v = a + (b - a) * u;
        return Sine(t + Noise.H2(r, 7, seed + 64) + .35 * v) * (.6 + .4 * v);
    }

    /// <summary>sin(2πt) as two parabolas: smooth enough for a meander, and plain arithmetic, so bit-identical on
    /// every machine (Math.Sin comes from the C runtime).</summary>
    static double Sine(double t)
    {
        double f = t - Math.Floor(t);
        return f < .5 ? 16 * f * (.5 - f) : -16 * (f - .5) * (1 - f);
    }

    /// <summary>
    /// A small delta for a big river that reaches open water over lowland: two arms leave the main line ~10 px before
    /// the mouth at ±40° and fan out into the water. An arm that doesn't reach water within ~13 px, would cross another
    /// river or would enter the water right beside the main mouth is not drawn; narrow bays and small lakes get none.
    /// </summary>
    [MethodImpl(Hot)]
    void Delta(Traced t, List<Traced> all, double ks)
    {
        int n = t.X.Count;
        if (n < 12 || t.F[n - 1] < .62f || IsLandAt(t.X[n - 1], t.Y[n - 1])) return;   // small, or a tributary
        double back = 10 * ks, acc = 0;
        int f = n - 1;
        while (f > 2 && acc < back) { acc += Math.Sqrt((t.X[f] - t.X[f - 1]) * (t.X[f] - t.X[f - 1]) + (t.Y[f] - t.Y[f - 1]) * (t.Y[f] - t.Y[f - 1])); f--; }
        if (HeightAt(t.X[f], t.Y[f]) > .12) return;
        double mx = t.X[n - 1], my = t.Y[n - 1], hx = mx - t.X[f], hy = my - t.Y[f], hl = Math.Sqrt(hx * hx + hy * hy);
        if (hl < 3) return;
        hx /= hl; hy /= hl;
        for (int d = 4; d <= 16; d += 4) if (IsLandAt(mx + hx * d * ks, my + hy * d * ks)) return;   // not open water ahead
        foreach (int side in new[] { -1, 1 })
        {
            const double cos = .7648421872844885, sin = .644217687237691;             // ±0.7 rad
            double dx = hx * cos - side * hy * sin, dy = side * hx * sin + hy * cos;
            var arm = new Traced();
            double x = t.X[f], y = t.Y[f];
            float flow = t.F[f] * .5f;
            arm.X.Add(x); arm.Y.Add(y); arm.F.Add(flow);
            bool ok = false;
            for (int step = 0; step < (int)(9 * ks); step++)
            {
                dx = dx * .97 + hx * .03; dy = dy * .97 + hy * .03;                 // a slight curve towards the sea
                double dl = Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
                x = (float)(x + dx * 1.5); y = (float)(y + dy * 1.5);
                if (y < 0 || y >= _h) break;
                arm.X.Add(x); arm.Y.Add(y); arm.F.Add(flow);
                if (!IsLandAt(x, y)) { ok = (x - mx) * (x - mx) + (y - my) * (y - my) >= 16 * ks * ks; break; }
                if (step > 1 && _d.River[Pix(x, y)] != 0) break;             // ran into another river
            }
            if (!ok) continue;
            Rasterise(arm);
            all.Add(arm);
        }
    }

    int Pix(double x, double y)
    {
        int ix = (int)Math.Floor(x) % _w;
        if (ix < 0) ix += _w;
        return Math.Clamp((int)Math.Floor(y), 0, _h - 1) * _w + ix;
    }

    bool IsLandAt(double x, double y) => _d.Land[Pix(x, y)] != 0;
    double HeightAt(double x, double y) => _d.Height[Pix(x, y)];

    /// <summary>River pixels along the final line (land pixels only).</summary>
    [MethodImpl(Hot)]
    void Rasterise(Traced t)
    {
        var river = _d.River; var land = _d.Land;
        for (int k = 0; k + 1 < t.X.Count; k++)
        {
            double x0 = t.X[k], y0 = t.Y[k], dx = t.X[k + 1] - x0, dy = t.Y[k + 1] - y0;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) * 2));
            for (int q = 0; q <= steps; q++)
            {
                int i = Pix(x0 + dx * q / steps, y0 + dy * q / steps);
                if (land[i] != 0) river[i] = 1;
            }
        }
    }

    static WorldData.RiverPath ToPath(Traced t)
    {
        int n = t.X.Count;
        var p = new WorldData.RiverPath { Xs = new float[n], Ys = new float[n], Flow = new float[n], MinY = float.MaxValue, MaxY = float.MinValue };
        for (int k = 0; k < n; k++)
        {
            p.Xs[k] = (float)t.X[k]; p.Ys[k] = (float)t.Y[k]; p.Flow[k] = t.F[k];
            p.MinY = Math.Min(p.MinY, p.Ys[k]); p.MaxY = Math.Max(p.MaxY, p.Ys[k]);
        }
        return p;
    }

    /// <summary>0 at the head of a river, 1 for a big river (~40× the starting catchment): log scale.</summary>
    static float Flow(float a, float thr) => (float)Math.Clamp(Log2(Math.Max(1, a / (double)thr)) / 5.321928094887362, 0, 1);

    /// <summary>log2 for v ≥ 1 from the exponent bits and a cubic on the mantissa (error &lt; 0.02): plain arithmetic, so
    /// bit-identical on every machine.</summary>
    static double Log2(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        int e = (int)((bits >> 52) & 0x7FF) - 1023;
        double m = BitConverter.Int64BitsToDouble(bits & 0x000FFFFFFFFFFFFF | 0x3FF0000000000000) - 1;   // [0, 1)
        return e + m * (1.4425449 + m * (-.7181452 + m * .2764548));
    }
}
