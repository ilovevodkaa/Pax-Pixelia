using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PaxPixelia.Core;

namespace PaxPixelia.World;

/// <summary>Rivers (mockup genRivers): greedy descent from springs in the hills until the sea or another river.</summary>
internal sealed partial class WorldBuilder
{
    const int RiverMaxSteps = 1200, RiverMinLength = 28;
    const double RiverClimb = .03;      // a step may go up at most this much (crosses tiny pits)
    const double RiverDrain = .5;       // raw-elevation weight, see Rivers()

    /// <summary>Spring attempts and the river cap scale with the map area. The descent key adds a little raw elevation
    /// so the flat interiors of big continents still drain to the coast instead of trapping rivers. Sequential by nature:
    /// every river may end in an earlier one.</summary>
    [MethodImpl(Hot)]
    void Rivers()
    {
        int w = _w, h = _h, s = _s;
        var land = _d.Land; var hgt = _d.Height; var biome = _d.Biome; var river = _d.River; var bas = _base;
        double sea = _sea;
        int attempts = (int)JsMath.Round(_n / 50.0), cap = (int)JsMath.Round(_n / 9200.0), made = 0;
        var stamp = new int[_n];   // attempt number that last visited the pixel
        var path = new List<int>(RiverMaxSteps);
        Span<int> nb = stackalloc int[4];

        double Key(int j) => land[j] != 0 ? hgt[j] + RiverDrain * (bas[j] - sea) + Noise.H2(j, 3, s) * .003 : -1;

        for (int k = 0; k < attempts && made < cap; k++)
        {
            int i = (int)(Noise.H2(k, 9, s) * h) * w + (int)(Noise.H2(k, 7, s) * w);
            if (land[i] == 0 || hgt[i] < .3 || hgt[i] > .78 || biome[i] <= 2) continue;
            path.Clear();
            bool reached = false;
            for (int step = 0; step < RiverMaxSteps; step++)
            {
                path.Add(i); stamp[i] = k + 1;
                int x = i % w, y = i / w, best = -1;
                double bestKey = 1e9, cur = Key(i);
                nb[0] = y * w + (x + 1) % w; nb[1] = y * w + (x + w - 1) % w; nb[2] = y > 0 ? i - w : -1; nb[3] = y < h - 1 ? i + w : -1;
                foreach (int j in nb)
                {
                    if (j < 0 || stamp[j] == k + 1) continue;
                    double kj = Key(j);
                    if (kj < bestKey) { bestKey = kj; best = j; }
                }
                if (best < 0 || bestKey > cur + RiverClimb) break;
                if (land[best] == 0 || river[best] != 0) { reached = true; break; }
                i = best;
            }
            if (!reached || path.Count <= RiverMinLength || Curls(path, w)) continue;
            foreach (int j in path) river[j] = 1;
            made++;
            _d.Rivers.Add(Polyline(path, w));
        }
    }

    /// <summary>A tracer circling a flat pit leaves a spiral: true when the path comes back within 10 px of where it was 40 steps earlier.</summary>
    static bool Curls(List<int> path, int w)
    {
        for (int k = 0; k + 40 < path.Count; k++)
        {
            int a = path[k], b = path[k + 40];
            int dx = Math.Abs(a % w - b % w);
            if (dx > w / 2) dx = w - dx;
            int dy = a / w - b / w;
            if (dx * dx + dy * dy < 100) return true;
        }
        return false;
    }

    /// <summary>Pixel path → smoothed polyline (every 2nd point, ±4 moving average), x unwrapped to stay continuous.</summary>
    static WorldData.RiverPath Polyline(List<int> path, int w)
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
        int m = (n + 1) / 2 + 1;
        var xs = new float[m]; var ys = new float[m];
        int o = 0;
        for (int k = 0; k < n; k += 2)
        {
            if (k == 0) { xs[o] = (float)px[0]; ys[o++] = (float)py[0]; continue; }
            double ax = 0, ay = 0; int c = 0;
            for (int q = Math.Max(0, k - 4), e = Math.Min(n - 1, k + 4); q <= e; q++) { ax += px[q]; ay += py[q]; c++; }
            xs[o] = (float)(ax / c); ys[o++] = (float)(ay / c);
        }
        xs[o] = (float)px[n - 1]; ys[o++] = (float)py[n - 1];
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (float y in ys) { minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        return new WorldData.RiverPath { Xs = xs, Ys = ys, MinY = minY, MaxY = maxY };
    }
}
