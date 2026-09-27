using System;
using System.Runtime.CompilerServices;
using PaxPixelia.Core;

namespace PaxPixelia.World;

/// <summary>
/// The drainage network rivers follow, on a coarse grid of C×C-pixel cells (4 px on a 2560-wide world). Traced on
/// pixels, D8 flow ran in ruler-straight runs along the 8 compass directions; traced on cells and drawn as a spline
/// through one jittered point per cell, a river bends like a river. Every land cell drains to one neighbour: a priority
/// flood from the water over a drainage elevation (terrain + raw elevation + domain-warped valleys that gather the rain)
/// that also fills pits, so everything reaches a sea or a lake. Sequential and deterministic.
/// </summary>
internal sealed class RiverGrid
{
    const double Drain = .5;                        // raw-elevation weight: flat interiors of big continents still drain to the coast
    // valleys ~250, ~100 and ~40 px across: they beat the gentle continental slopes, so channels wander and converge
    // instead of running down a coastal plain side by side
    const double ValleyHuge = .25, ValleyBig = .07, ValleySmall = .018;
    const int Levels = 1 << 15;                     // flood priority buckets
    const int PitSpread = 90;                       // flood levels over which the cells of a filled pit are spread by noise

    /// <summary>Rain per pixel by biome: dry steppes and deserts feed few rivers, jungles and mountains many.</summary>
    static readonly float[] Rain = { 0, .3f, .8f, 1f, .4f, .9f, .4f, 1f, 1f, .8f, .6f, .35f, 1.2f, .45f, .05f };

    public readonly int C, Gw, Gh, Count;
    public readonly bool[] IsLand;      // most of the cell is land
    public readonly float[] Acc;        // rain-weighted catchment, px
    public readonly int[] Rcv;          // receiver cell (a water cell at the coast); -1 for water cells
    public readonly int[] Order;        // land cells, downstream first
    public readonly int[] Rank;         // position in Order (land cells)
    public readonly float[] Px, Py;     // the cell's point: jittered, on a pixel of the cell's own kind (land or water)

    readonly WorldData _d;

    public RiverGrid(WorldData d, float[] bas, double sea, int seed, int cell)
    {
        _d = d; C = cell;
        Gw = d.W / C; Gh = (d.H + C - 1) / C; Count = Gw * Gh;
        IsLand = new bool[Count]; Acc = new float[Count]; Rcv = new int[Count]; Rank = new int[Count];
        Px = new float[Count]; Py = new float[Count];
        var elev = new float[Count];
        Sample(bas, sea, seed, elev);
        Points(seed);
        Order = Flood(elev, seed);
        for (int k = 0; k < Order.Length; k++) Rank[Order[k]] = k;
        for (int k = Order.Length - 1; k >= 0; k--)
        {
            int c = Order[k], r = Rcv[c];
            if (r >= 0 && IsLand[r]) Acc[r] += Acc[c];
        }
    }

    public int CellOf(int x, int y) => (y / C) * Gw + x / C;

    /// <summary>Land share, mean drainage elevation and rain of every cell, plus the valley noise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    void Sample(float[] bas, double sea, int s, float[] elev)
    {
        int w = _d.W, h = _d.H, kHuge = Math.Max(2, w / 256), kBig = Math.Max(2, w / 100), kSmall = Math.Max(2, w / 40), kWarp = Math.Max(2, w / 180);
        double warpA = 32.0 * w / 2560;
        var land = _d.Land; var hgt = _d.Height; var biome = _d.Biome;
        for (int gy = 0; gy < Gh; gy++)
        {
            int y0 = gy * C, y1 = Math.Min(h, y0 + C);
            for (int gx = 0; gx < Gw; gx++)
            {
                int c = gy * Gw + gx, cnt = 0;
                double e = 0, rain = 0, top = 0, peak = 0;
                for (int y = y0; y < y1; y++)
                    for (int i = y * w + gx * C, end = i + C; i < end; i++)
                    {
                        if (land[i] == 0) continue;
                        double v = hgt[i] + Drain * (bas[i] - sea);
                        cnt++; e += v; rain += Rain[biome[i]];
                        if (v > top) top = v;
                        if (hgt[i] > peak) peak = hgt[i];
                    }
                Acc[c] = (float)rain;
                if (cnt * 2 <= C * (y1 - y0)) continue;
                IsLand[c] = true;
                double cx = gx * C + C * .5, cy = gy * C + C * .5;
                double qx = cx + warpA * (GenNoise.Fbm(cx, cy, kWarp, 2, s + 66, w) * 2 - 1);
                double qy = cy + warpA * (GenNoise.Fbm(cx, cy, kWarp, 2, s + 67, w) * 2 - 1);
                // the cell's highest pixel counts half, so a thin ridge stays a divide; valleys fade out on hills and
                // mountains, where the relief itself steers the water
                double valleys = ValleyHuge * GenNoise.Fbm(qx, qy, kHuge, 2, s + 69, w) + ValleyBig * GenNoise.Fbm(qx, qy, kBig, 2, s + 61, w)
                                 + ValleySmall * GenNoise.Fbm(qx, qy, kSmall, 2, s + 68, w);
                elev[c] = (float)(.5 * e / cnt + .5 * top + valleys * (1 - Noise.Smooth(.3, .6, peak)));
            }
        }
    }

    /// <summary>One point per cell, jittered inside it; moved to the nearest pixel of the cell's kind when the jitter
    /// lands on the other kind (coastal cells), so land cells keep their river on land and water cells hold a mouth.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    void Points(int s)
    {
        int w = _d.W, h = _d.H;
        var land = _d.Land;
        for (int gy = 0; gy < Gh; gy++)
            for (int gx = 0; gx < Gw; gx++)
            {
                int c = gy * Gw + gx;
                double px = gx * C + C * .5 + (Noise.H2(gx, gy, s + 65) - .5) * C * .8;
                double py = Math.Min(h - .5, gy * C + C * .5 + (Noise.H2(gx, gy, s + 64) - .5) * C * .8);
                byte kind = (byte)(IsLand[c] ? 1 : 0);
                if (land[(int)py * w + (int)px] != kind)
                {
                    double best = double.MaxValue, bx = px, by = py;
                    for (int y = gy * C, y1 = Math.Min(h, y + C); y < y1; y++)
                        for (int x = gx * C; x < gx * C + C; x++)
                        {
                            if (land[y * w + x] != kind) continue;
                            double dx = x + .5 - px, dy = y + .5 - py, d2 = dx * dx + dy * dy;
                            if (d2 < best) { best = d2; bx = x + .5; by = y + .5; }
                        }
                    px = bx; py = by;
                }
                Px[c] = (float)px; Py[c] = (float)py;
            }
    }

    /// <summary>Priority flood from the coasts (bucket queue, FIFO inside a bucket). Returns the land cells in flood
    /// order and sets every receiver.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int[] Flood(float[] elev, int s)
    {
        int n = Count, pitK = Math.Max(2, _d.W / 24);
        float eMin = float.MaxValue, eMax = float.MinValue;
        int landCount = 0;
        for (int c = 0; c < n; c++)
        {
            if (!IsLand[c]) continue;
            landCount++;
            if (elev[c] < eMin) eMin = elev[c];
            if (elev[c] > eMax) eMax = elev[c];
        }
        if (landCount == 0) return Array.Empty<int>();
        float scale = (Levels - 1) / Math.Max(1e-6f, eMax - eMin);
        var next = new int[n];
        var head = new int[Levels]; var tail = new int[Levels];
        Array.Fill(head, -1);
        Span<int> nb = stackalloc int[8];
        void Push(int c, int lv)
        {
            next[c] = -1;
            if (head[lv] < 0) head[lv] = c; else next[tail[lv]] = c;
            tail[lv] = c;
        }
        for (int c = 0; c < n; c++)
        {
            if (!IsLand[c]) { Rcv[c] = -1; continue; }
            Rcv[c] = -2;                                           // not reached yet
            int cnt = Neighbours(c, nb);
            for (int k = 0; k < cnt; k++)
            {
                if (IsLand[nb[k]]) continue;
                Rcv[c] = nb[k];                                    // drains straight into the water
                Push(c, (int)((elev[c] - eMin) * scale));
                break;
            }
        }
        var order = new int[landCount];
        int no = 0;
        for (int lv = 0; lv < Levels; lv++)
            for (int c = head[lv]; c >= 0; c = next[c])
            {
                order[no++] = c;
                int cnt = Neighbours(c, nb);
                for (int k = 0; k < cnt; k++)
                {
                    int j = nb[k];
                    if (Rcv[j] != -2) continue;
                    Rcv[j] = c;
                    int lj = (int)((elev[j] - eMin) * scale);
                    // A pit is filled up to its spill level. Its cells are queued in the order of a smooth noise just above
                    // that level, so channels across the filled flat meander instead of running as straight BFS rays.
                    if (lj < lv) lj = Math.Min(Levels - 1, lv + (int)(PitSpread * GenNoise.Fbm(j % Gw * C, j / Gw * C, pitK, 1, s + 62, _d.W)));
                    Push(j, lj);
                }
            }
        if (no < landCount) Array.Resize(ref order, no);           // land cut off from all water (none in practice)
        return order;
    }

    /// <summary>The 8 neighbours of cell c in a fixed order (sides first), x wrapped, none beyond the top/bottom rows.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Neighbours(int c, Span<int> nb)
    {
        int y = c / Gw, x = c - y * Gw;
        int l = x == 0 ? c + Gw - 1 : c - 1, r = x == Gw - 1 ? c - Gw + 1 : c + 1, k = 0;
        nb[k++] = r; nb[k++] = l;
        if (y > 0) nb[k++] = c - Gw;
        if (y < Gh - 1) nb[k++] = c + Gw;
        if (y > 0) { nb[k++] = r - Gw; nb[k++] = l - Gw; }
        if (y < Gh - 1) { nb[k++] = r + Gw; nb[k++] = l + Gw; }
        return k;
    }

    /// <summary>Cells within Chebyshev radius rad of c (x wrapped), c itself excluded.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool AnyWithin(int c, int rad, int[] owner, int except)
    {
        int y = c / Gw, x = c - y * Gw;
        for (int dy = -rad; dy <= rad; dy++)
        {
            int yy = y + dy;
            if (yy < 0 || yy >= Gh) continue;
            for (int dx = -rad; dx <= rad; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int xx = x + dx;
                if (xx < 0) xx += Gw; else if (xx >= Gw) xx -= Gw;
                int o = owner[yy * Gw + xx];
                if (o != 0 && o != except) return true;
            }
        }
        return false;
    }
}
