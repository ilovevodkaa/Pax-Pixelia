using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Map;

/// <summary>
/// CPU half of the fog-of-war look (port of buildFogTex/paintRect from docs/mockups/js/fog.js + render.js):
///  • <see cref="Cloud"/>: the stylised cloud texture on 2×2 blocks — rank-normalised warped noise (R) and a tone index
///    (G = level*3 + flat/lit-top/shaded-bottom), built once per world;
///  • <see cref="Dist"/>: per-pixel chamfer distance (3 per px, capped) to the explored/unexplored frontier, which the
///    shader turns into the frayed, Bayer-dithered cloud edge. Updated incrementally around provinces whose explored
///    flag flipped; the cap makes a rect + margin computation exact, so updates stay local.
/// </summary>
internal sealed class FogField
{
    public const int Far = 24;          // chamfer units; must match FOG_FAR in map_common.gdshaderinc
    const int Margin = Far / 3 + 1;     // px of context a local recompute needs

    public byte[] Dist = Array.Empty<byte>();
    public byte[] Cloud = Array.Empty<byte>();
    public int CloudW, CloudH;

    WorldData _w;
    bool[] _ex = Array.Empty<bool>();   // explored flags the current Dist was built from
    int[] _bx0, _bx1, _by0, _by1;       // province bboxes, x unwrapped around PCX
    readonly Work _work = new();        // main-thread scratch for incremental updates
    readonly List<int> _flipped = new();

    static readonly float[] Bay = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    /// <summary>Explored = the province is not hidden under clouds (any fog state but 0).</summary>
    public bool IsExplored(int p) => _ex.Length > p && _ex[p];

    public void Init(WorldData w, GameState s)
    {
        _w = w;
        Dist = new byte[w.N];
        _ex = new bool[w.P];
        for (int p = 0; p < w.P; p++) _ex[p] = s.Fog[p] > 0;
        BuildBoxes(w);
        BuildCloud(w);
        ComputeAll();
    }

    /// <summary>Re-read explored flags (changed = null → all provinces). Returns true when Dist changed.</summary>
    public bool Update(GameState s, IReadOnlyList<int> changed)
    {
        if (_w == null) return false;
        _flipped.Clear();
        if (changed == null) { for (int p = 0; p < _w.P; p++) Check(s, p); }
        else foreach (int p in changed) if ((uint)p < (uint)_w.P) Check(s, p);
        if (_flipped.Count == 0) return false;
        if (_flipped.Count > _w.P / 8) { ComputeAll(); return true; }
        foreach (var r in Cluster(_flipped)) ComputeRect(r.x0 - Margin, r.y0 - Margin, r.x1 + Margin + 1, r.y1 + Margin + 1, _work);
        return true;
    }

    void Check(GameState s, int p)
    {
        bool e = s.Fog[p] > 0;
        if (e != _ex[p]) { _ex[p] = e; _flipped.Add(p); }
    }

    /// <summary>True if world pixel (x unwrapped, y) is explored ground well clear of the cloud edge (scouts, their paths).</summary>
    public bool IsClear(float x, float y, int minDist = 18)
    {
        var w = _w; if (w == null) return true;
        int X = ((int)MathF.Floor(x) % w.W + w.W) % w.W, Y = Math.Clamp((int)MathF.Floor(y), 0, w.H - 1), i = Y * w.W + X;
        return _ex[w.Prov[i]] && Dist[i] >= minDist;
    }

    // ---------------- distance field ----------------

    sealed class Work { public byte[] E = Array.Empty<byte>(); public ushort[] D = Array.Empty<ushort>(); }

    void ComputeAll()
    {
        const int band = 48;
        int h = _w.H, bands = (h + band - 1) / band;
        Parallel.For(0, bands, () => new Work(), (b, _, work) =>
        {
            ComputeRect(0, b * band, _w.W, Math.Min(h, (b + 1) * band), work);
            return work;
        }, _ => { });
    }

    /// <summary>Exact capped chamfer distance for pixels of [x0,x1)×[y0,y1) (x unwrapped), using a margin of context.</summary>
    void ComputeRect(int x0, int y0, int x1, int y1, Work work)
    {
        var wd = _w; int W = wd.W, H = wd.H;
        y0 = Math.Max(0, y0); y1 = Math.Min(H, y1);
        if (y1 <= y0 || x1 <= x0) return;
        int X0 = x0 - Margin, Y0 = Math.Max(0, y0 - Margin), Y1 = Math.Min(H, y1 + Margin);
        int w = x1 - x0 + 2 * Margin, h = Y1 - Y0, n = w * h;
        if (work.E.Length < n) { work.E = new byte[n]; work.D = new ushort[n]; }
        var e = work.E; var d = work.D; var prov = wd.Prov; var ex = _ex;
        for (int y = 0, o = 0; y < h; y++)
        {
            int row = (Y0 + y) * W, wx = ((X0 % W) + W) % W;
            for (int x = 0; x < w; x++, o++) { e[o] = ex[prov[row + wx]] ? (byte)1 : (byte)0; if (++wx == W) wx = 0; }
        }
        // forward pass (frontier pixels = 0), then backward pass; 3/4 chamfer weights
        for (int y = 0, k = 0; y < h; y++)
            for (int x = 0; x < w; x++, k++)
            {
                byte c = e[k];
                if ((x > 0 && e[k - 1] != c) || (x < w - 1 && e[k + 1] != c) || (y > 0 && e[k - w] != c) || (y < h - 1 && e[k + w] != c)) { d[k] = 0; continue; }
                int v = 999, q;
                if (x > 0) { q = d[k - 1] + 3; if (q < v) v = q; }
                if (y > 0)
                {
                    int u = k - w; q = d[u] + 3; if (q < v) v = q;
                    if (x > 0) { q = d[u - 1] + 4; if (q < v) v = q; }
                    if (x < w - 1) { q = d[u + 1] + 4; if (q < v) v = q; }
                }
                d[k] = (ushort)v;
            }
        for (int y = h - 1, k = w * h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--, k--)
            {
                int v = d[k]; if (v == 0) continue;
                int q;
                if (x < w - 1) { q = d[k + 1] + 3; if (q < v) v = q; }
                if (y < h - 1)
                {
                    int u = k + w; q = d[u] + 3; if (q < v) v = q;
                    if (x < w - 1) { q = d[u + 1] + 4; if (q < v) v = q; }
                    if (x > 0) { q = d[u - 1] + 4; if (q < v) v = q; }
                }
                d[k] = (ushort)v;
            }
        var dist = Dist;
        for (int y = y0; y < y1; y++)
        {
            int row = y * W, dr = (y - Y0) * w + Margin, wx = ((x0 % W) + W) % W;
            for (int x = 0, cnt = Math.Min(x1 - x0, W); x < cnt; x++)
            {
                int v = d[dr + x];
                dist[row + wx] = v > 255 ? (byte)255 : (byte)v;
                if (++wx == W) wx = 0;
            }
        }
    }

    void BuildBoxes(WorldData w)
    {
        int P = w.P, W = w.W, half = W / 2;
        _bx0 = new int[P]; _bx1 = new int[P]; _by0 = new int[P]; _by1 = new int[P];
        Parallel.For(0, P, p =>
        {
            int c = w.PCX[p], a = w.PixOffset[p], e = w.PixOffset[p + 1], x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
            for (int k = a; k < e; k++)
            {
                int i = w.PixList[k], y = i / W, x = i - y * W - c;
                if (x > half) x -= W; else if (x < -half) x += W;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
            if (e == a) { x0 = x1 = 0; y0 = y1 = w.PCY[p]; }
            _bx0[p] = c + x0; _bx1[p] = c + x1; _by0[p] = y0; _by1[p] = y1;
        });
    }

    /// <summary>Greedy merge of province boxes into rects no bigger than ~384 px (wrap-aware).</summary>
    List<(int x0, int x1, int y0, int y1)> Cluster(List<int> ps)
    {
        var cl = new List<(int x0, int x1, int y0, int y1, int cx)>();
        int W = _w.W;
        foreach (int p in ps)
        {
            int x0 = _bx0[p], x1 = _bx1[p], y0 = _by0[p], y1 = _by1[p];
            bool hit = false;
            for (int i = 0; i < cl.Count; i++)
            {
                var c = cl[i];
                int o = (int)MathF.Round((float)(_w.PCX[p] - c.cx) / W) * W;
                int a = Math.Min(c.x0, x0 - o), b = Math.Max(c.x1, x1 - o), u = Math.Min(c.y0, y0), v = Math.Max(c.y1, y1);
                if (b - a < 384 && v - u < 384) { cl[i] = (a, b, u, v, c.cx); hit = true; break; }
            }
            if (!hit) cl.Add((x0, x1, y0, y1, _w.PCX[p]));
        }
        var r = new List<(int, int, int, int)>(cl.Count);
        foreach (var c in cl) r.Add((c.x0, c.x1, c.y0, c.y1));
        return r;
    }

    // ---------------- cloud texture ----------------

    void BuildCloud(WorldData wd)
    {
        int W = wd.W, H = wd.H, S = wd.Seed, bw = W >> 1, bh = (H + 1) >> 1, n = bw * bh;
        int k = Math.Max(2, (int)MathF.Round(W / 96f)), kw = Math.Max(1, (int)MathF.Round(W / 512f));
        const int G = 32;
        int gw = (W + G - 1) / G, gh = (H + G - 1) / G + 1;
        var wx = new float[gw * gh]; var wy = new float[gw * gh];   // low-frequency domain warp on a 32 px grid
        Parallel.For(0, gh, gy =>
        {
            for (int gx = 0; gx < gw; gx++)
            {
                wx[gy * gw + gx] = (float)(60 * (Noise.Fbm(gx * G, gy * G, kw, 2, S + 411, W) - .5));
                wy[gy * gw + gx] = (float)(60 * (Noise.Fbm(gx * G, gy * G, kw, 2, S + 412, W) - .5));
            }
        });
        var v = new float[n];
        Parallel.For(0, bh, by =>
        {
            int y = by * 2, gy = Math.Min(gh - 2, y / G);
            float fy = (float)y / G - gy, ey = 1 - fy;
            for (int bx = 0; bx < bw; bx++)
            {
                int x = bx * 2, gx = x / G;
                float fx = (float)x / G - gx, ex = 1 - fx;
                int g0 = gy * gw + gx, g1 = gy * gw + (gx + 1) % gw;
                float ox = (wx[g0] * ex + wx[g1] * fx) * ey + (wx[g0 + gw] * ex + wx[g1 + gw] * fx) * fy;
                float oy = (wy[g0] * ex + wy[g1] * fx) * ey + (wy[g0 + gw] * ex + wy[g1 + gw] * fx) * fy;
                v[by * bw + bx] = (float)Noise.Fbm(x + ox, y + oy, k, 3, S + 410, W);
            }
        });
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (float e in v) { if (e < mn) mn = e; if (e > mx) mx = e; }
        // rank-normalise through a histogram so the four levels always cover fixed shares of the sky
        var hist = new float[1025]; float sc = 1024 / (mx - mn > 0 ? mx - mn : 1);
        foreach (float e in v) hist[(int)((e - mn) * sc)]++;
        for (int b = 1; b < 1025; b++) hist[b] += hist[b - 1];
        for (int b = 0; b < 1025; b++) hist[b] /= n;
        var fogN = new byte[n]; var lvl = new byte[n];
        float[] th = { 0, .36f, .64f, .86f, 1 };
        for (int i = 0; i < n; i++)
        {
            float u = hist[(int)((v[i] - mn) * sc)];
            fogN[i] = (byte)Math.Min(255, (int)(u * 255));
            lvl[i] = (byte)(u < th[1] ? 0 : u < th[2] ? 1 : u < th[3] ? 2 : 3);
        }
        CloudW = bw; CloudH = bh; Cloud = new byte[n * 2];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                int i = by * bw + bx, l = lvl[i], xl = by * bw + (bx > 0 ? bx - 1 : bw - 1);
                float f = (fogN[i] / 255f - th[l]) / (th[l + 1] - th[l]);
                int ld = l < 3 && (f - .62f) / .38f > (Bay[((by & 3) << 2) | (bx & 3)] + .5f) / 16f ? l + 1 : l;   // dither the top of each level into the next
                int edge = by > 0 && lvl[i - bw] < l ? 1 : (by > 1 && lvl[xl - 2 * bw] > l) || (by > 0 && lvl[xl - bw] > l) ? 2 : 0;
                Cloud[i * 2] = fogN[i];
                Cloud[i * 2 + 1] = (byte)(ld * 3 + edge);
            }
    }
}
