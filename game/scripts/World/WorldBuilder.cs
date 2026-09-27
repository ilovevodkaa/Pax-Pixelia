using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PaxPixelia.World;

/// <summary>
/// One world generation run: holds the temporaries between stages:
/// relief → sea level → heights + biomes → bodies → (province assignment ‖ rivers) → colour → merge → province stats.
/// Per-pixel passes are Parallel.For over rows where every row writes only its own outputs, so results don't depend
/// on scheduling. Relief, climate and provinces still follow the approved JS mockup's formulas (doubles, float32 at storage).
/// </summary>
internal sealed partial class WorldBuilder
{
    // generation runs once right after launch: compile hot loops fully optimised at once instead of tier-0 first
    const MethodImplOptions Hot = MethodImplOptions.AggressiveOptimization;

    readonly WorldData _d;
    readonly int _s, _w, _h, _n;
    readonly GenScale _k;
    readonly ParallelOptions _po;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lap;

    float[] _base, _ridge;      // raw elevation, ridge strength
    double _min, _max, _sea;    // raw elevation range (double, before float32 storage) and sea level
    Components _bodies;         // land masses / water bodies after cleanup

    readonly CancellationToken _cancel;

    public WorldBuilder(int seed, int w, int h, int maxThreads, CancellationToken cancel = default)
    {
        _s = seed; _w = w; _h = h; _n = w * h;
        _k = new GenScale(w);
        _cancel = cancel;
        _po = new ParallelOptions { MaxDegreeOfParallelism = maxThreads, CancellationToken = cancel };
        _d = new WorldData
        {
            Seed = seed, W = w, H = h, N = _n,
            Land = new byte[_n], Height = new float[_n], Biome = new byte[_n], BaseColor = new byte[_n * 4], River = new byte[_n], Prov = new int[_n]
        };
    }

    public WorldData Build(Action<string> progress)
    {
        progress?.Invoke("Рельеф…");
        Relief(); Lap("relief");
        SeaLevel(); Lap("sea");
        progress?.Invoke("Климат…");
        HeightsAndBiomes(); Lap("climate");
        _bodies = Components.Label(_d.Land, _w, _h, _po); Lap("bodies");

        progress?.Invoke("Реки и провинции…");
        var seeds = ProvinceSeeds();
        // rivers are inherently sequential: trace them on one thread while the parallel passes run (disjoint outputs)
        var rivers = Task.Run(() => { double t0 = _clock.Elapsed.TotalMilliseconds; Rivers(); return _clock.Elapsed.TotalMilliseconds - t0; });
        AssignProvinces(seeds); Lap("assign");
        double riverMs = rivers.Result; Lap("rivers wait");      // the colour pass greens the banks of desert rivers
        _d.GenTimings.Add(("(rivers, concurrent)", riverMs));
        Colour(); Lap("colour");

        progress?.Invoke("Провинции…");
        MergeProvinces(seeds.Count); Lap("merge");
        WorldGen.BuildPixelIndex(_d); Lap("index");
        ProvinceStats(); Lap("stats");
        return _d;
    }

    void Lap(string stage)
    {
        _cancel.ThrowIfCancellationRequested();
        double t = _clock.Elapsed.TotalMilliseconds;
        _d.GenTimings.Add((stage, t - _lap));
        _lap = t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Fbm(double x, double y, int k, int oct, int s) => GenNoise.Fbm(x, y, k, oct, s, _w);
}
