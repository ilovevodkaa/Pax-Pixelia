using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.Map;

/// <summary>
/// Plots for the building icons of a province (ART_BIBLE §10.2), computed on the map side from the terrain: spread-out
/// pixels well inside the province (water may be close — piers stand on the shore), each tagged with what suits it:
/// farms on flat fertile land, sawmills in forest, quarries on hills, piers on the coast, shrines and markets near
/// the centre. Computed lazily per province and cached for the world; deterministic (hash of the world seed).
/// </summary>
internal sealed class BuildingPlots
{
    public readonly struct Plot
    {
        public readonly short X, Y;            // world px (x may be off by ±W from the province centre: unwrapped)
        public readonly byte Biome;
        public readonly bool Hill, Coast, River;
        public Plot(int x, int y, byte biome, bool hill, bool coast, bool river)
        { X = (short)x; Y = (short)y; Biome = biome; Hill = hill; Coast = coast; River = river; }
    }

    const int MinDist2 = 16;                   // 4 world px between plots: icons (≈4 world px from ×5) never touch
    const int MaxPlots = 24;

    WorldData _w;
    Plot[][] _plots = Array.Empty<Plot[]>();
    readonly List<(uint key, int x, int y)> _cand = new();
    readonly List<Plot> _acc = new();

    public void Reset(WorldData w) { _w = w; _plots = new Plot[w.P][]; }

    public Plot[] Of(int p) => _plots[p] ??= Compute(p);

    Plot[] Compute(int p)
    {
        var w = _w;
        _cand.Clear();
        int cx = w.PCX[p], half = w.W / 2;
        for (int k = w.PixOffset[p]; k < w.PixOffset[p + 1]; k++)
        {
            int i = w.PixList[k], x = i % w.W, y = i / w.W;
            if (!Inside(p, x, y)) continue;
            int ux = x - cx;
            if (ux > half) ux -= w.W; else if (ux < -half) ux += w.W;
            _cand.Add((Hash(x, y, w.Seed), cx + ux, y));
        }
        _cand.Sort((a, b) => a.key.CompareTo(b.key));
        _acc.Clear();
        foreach (var (_, x, y) in _cand)
        {
            bool ok = true;
            foreach (var a in _acc) { int dx = a.X - x, dy = a.Y - y; if (dx * dx + dy * dy < MinDist2) { ok = false; break; } }
            if (!ok) continue;
            int i = y * w.W + Wrap(x);
            _acc.Add(new Plot(x, y, w.Biome[i], w.Height[i] > .34f, NearWater(x, y), w.River[i] != 0));
            if (_acc.Count >= MaxPlots) break;
        }
        return _acc.ToArray();
    }

    int Wrap(int x) => ((x % _w.W) + _w.W) % _w.W;

    /// <summary>Every pixel up to 2 px away (8 directions) is this province or water.</summary>
    bool Inside(int p, int x, int y)
    {
        var w = _w;
        for (int d = 1; d <= 2; d++)
            for (int k = 0; k < 8; k++)
            {
                int dx = k switch { 0 or 3 or 5 => -d, 2 or 4 or 7 => d, _ => 0 };
                int dy = k switch { 0 or 1 or 2 => -d, 5 or 6 or 7 => d, _ => 0 };
                int yy = y + dy;
                if (yy < 0 || yy >= w.H) return false;
                int i = yy * w.W + Wrap(x + dx);
                if (w.Prov[i] != p && w.Land[i] != 0) return false;
            }
        return true;
    }

    bool NearWater(int x, int y)
    {
        var w = _w;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int yy = y + dy;
                if (yy >= 0 && yy < w.H && w.Land[yy * w.W + Wrap(x + dx)] == 0) return true;
            }
        return false;
    }

    static uint Hash(int x, int y, int seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    /// <summary>How well a building suits a plot (higher is better); d = distance to the province centre, world px.</summary>
    public static float Score(Data.Bld b, in Plot q, float d)
    {
        bool forest = q.Biome is 5 or 8 or 12;
        float near = -.04f * d;
        return b switch
        {
            Data.Bld.Farm => (q.Hill || forest ? 0 : Data.BiomeFert[q.Biome]) + (q.River ? .3f : 0) + near * .5f,
            Data.Bld.Lumber => (forest ? 1.2f : 0) + near * .5f,
            Data.Bld.Quarry => (q.Hill ? 1.2f : 0) + near * .5f,
            Data.Bld.Fishery => (q.Coast ? 2f : -1f) + near * .3f,
            Data.Bld.Pasture => (q.Biome is 6 or 9 or 10 or 11 or 13 && !q.Hill ? .9f : .2f) + near * .5f,
            _ => 1 + near * 2,                  // shrine, market, granary: by the town
        };
    }
}
