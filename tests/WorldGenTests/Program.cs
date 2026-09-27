using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace WorldGenTests;

/// <summary>
/// Console checks for WorldGen: timings, stats, invariants, river network quality, determinism across runs and thread
/// counts, golden per-array hashes, PNGs.
///
///   dotnet run -c Release [-p:GameDir=&lt;game copy&gt;/] -- [1337,42,777] [--size=2560x1440] [--threads=1,3] [--nodet]
///       [--png=&lt;dir&gt; [--crops=x,y;x,y]] [--bench=N] [--bless] [--budget=ms]
/// reference/golden_SEED.txt pins every output array of the last blessed generator; after an intended change to the
/// world, look at the PNGs and rerun with --bless. Exit code 0 = invariants hold, output deterministic and golden.
/// </summary>
static class Program
{
    static int _failures;

    static int Main(string[] args)
    {
        var opt = args.Where(a => a.StartsWith("--")).Select(a => a[2..].Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "1");
        var seeds = args.FirstOrDefault(a => !a.StartsWith("--"))?.Split(',').Select(int.Parse).ToArray() ?? new[] { 1337, 42, 777 };
        var size = (opt.GetValueOrDefault("size") ?? "2560x1440").Split('x').Select(int.Parse).ToArray();
        int W = size[0], H = size[1];
        var threads = (opt.GetValueOrDefault("threads") ?? "1,3").Split(',').Select(int.Parse).ToArray();
        double budget = double.Parse(opt.GetValueOrDefault("budget") ?? "600");
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine($"{Environment.ProcessorCount} logical cores, {W}x{H}");
        CheckNoiseParity();
        CheckHotKernels();
        CheckPalette();
        WorldGen.Generate(seeds[0], W, H);                                    // warm-up: timings below exclude JIT

        foreach (int seed in seeds)
        {
            var sw = Stopwatch.StartNew();
            var w = WorldGen.Generate(seed, W, H);
            double tGen = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"seed {seed} {W}x{H} worldgen {tGen:F0}ms stages: " + string.Join(" | ", w.GenTimings.Select(t => $"{t.Stage} {t.Ms:F0}")));
            if (W * H <= 2560 * 1440 && tGen > budget) Fail($"generation took {tGen:F0} ms (budget {budget:F0} ms)");
            PrintStats(w);
            CheckInvariants(w);
            CheckRivers(w);
            CheckTerrain(w);

            var hashes = Hashes(w);
            string full = Fnv(string.Join("\n", hashes));
            string refDir = SourceReferenceDir() ?? Path.Combine(AppContext.BaseDirectory, "reference");
            string golden = Path.Combine(refDir, $"golden_{seed}.txt");
            if (opt.ContainsKey("bless") && W == 2560 && H == 1440)
            {
                Directory.CreateDirectory(refDir);
                File.WriteAllLines(golden, hashes);
                Console.WriteLine($"  blessed {Path.GetFileName(golden)}");
            }
            else if (W == 2560 && H == 1440)
            {
                if (File.Exists(golden)) CompareGolden(hashes, golden);
                else Fail($"no {Path.GetFileName(golden)} — look at the PNGs, then run with --bless");
            }

            if (!opt.ContainsKey("nodet"))
            {
                var res = new List<string>();
                foreach (int t in threads.Prepend(-1))
                {
                    WorldGen.MaxThreads = t;
                    string h2 = Fnv(string.Join("\n", Hashes(WorldGen.Generate(seed, W, H))));
                    res.Add($"threads {(t < 0 ? "all" : t)}: {(h2 == full ? "same" : "DIFFERENT")}");
                    if (h2 != full) Fail($"non-deterministic output with {t} threads");
                }
                WorldGen.MaxThreads = -1;
                Console.WriteLine($"  determinism (full hash {full}): {string.Join(", ", res)}");
            }
            if (opt.TryGetValue("png", out var pdir)) WritePngs(w, pdir, opt.GetValueOrDefault("crops"));
        }

        if (opt.TryGetValue("bench", out var nb))
        {
            int n = int.Parse(nb);
            var times = new List<double>();
            for (int k = 0; k < n; k++)
            {
                var sw = Stopwatch.StartNew();
                var w = WorldGen.Generate(seeds[0], W, H);
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (k == n - 1) Console.WriteLine("  last run stages: " + string.Join(" | ", w.GenTimings.Select(t => $"{t.Stage} {t.Ms:F1}")));
            }
            times.Sort();
            Console.WriteLine($"bench seed {seeds[0]} x{n}: min {times[0]:F0}ms median {times[n / 2]:F0}ms max {times[^1]:F0}ms");
        }

        Console.WriteLine(_failures == 0 ? "ALL OK" : $"{_failures} FAILURE(S)");
        return _failures == 0 ? 0 : 1;
    }

    static void Fail(string msg) { _failures++; Console.WriteLine("  FAIL: " + msg); }

    /// <summary>The generator's private noise copy must equal Core.Noise bit for bit.</summary>
    static void CheckNoiseParity()
    {
        var rnd = new Random(12345);
        int[] ks = { 1, 3, 4, 5, 15, 20, 45, 55, 60, 100, 140, 160 };
        for (int t = 0; t < 200_000; t++)
        {
            double x = rnd.NextDouble() * 2800 - 120, y = rnd.NextDouble() * 1600 - 80;
            int k = ks[rnd.Next(ks.Length)], oct = 1 + rnd.Next(8), s = rnd.Next();
            double a = Noise.Fbm(x, y, k, oct, s, 2560), b = GenNoise.Fbm(x, y, k, oct, s, 2560);
            if (BitConverter.DoubleToInt64Bits(a) != BitConverter.DoubleToInt64Bits(b)) { Fail($"GenNoise != Core.Noise at ({x},{y},k{k},o{oct},s{s})"); return; }
        }
        Console.WriteLine("noise parity OK (GenNoise == Core.Noise on 200k samples)");
    }

    /// <summary>Generation runs right after launch: its per-pixel kernels (lambdas) must be marked AggressiveOptimization.</summary>
    static void CheckHotKernels()
    {
        var lambdas = typeof(WorldGen).Assembly.GetTypes()
            .Where(t => t.FullName!.StartsWith("PaxPixelia.World.") && t.FullName.Contains('+'))
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(m => m.Name.Contains("b__")).ToList();
        int hot = lambdas.Count(m => (m.MethodImplementationFlags & System.Reflection.MethodImplAttributes.AggressiveOptimization) != 0);
        Console.WriteLine($"hot kernels: {hot}/{lambdas.Count} generator lambdas compiled with AggressiveOptimization");
    }

    /// <summary>The ramp function must reproduce the art bible's generated ramps (forest, meadow) exactly.</summary>
    static void CheckPalette()
    {
        var forest = TerrainPalette.Ramp(64, 102, 60);
        var meadow = TerrainPalette.Ramp(110, 144, 74);
        bool ok = forest.SequenceEqual(TerrainPalette.Ramps[8]) && meadow.SequenceEqual(TerrainPalette.Ramps[9]);
        if (TerrainPalette.Ramps.Any(r => r == null || r.Length != 5)) Fail("a terrain material has no 5-step ramp");
        if (!ok) Fail($"TerrainPalette.Ramp differs from ART_BIBLE §2.2: forest {string.Join(",", forest.Select(c => c.ToString("X6")))}");
        else Console.WriteLine($"palette OK ({TerrainPalette.Count} materials × 5 steps, ramp function == ART_BIBLE)");
    }

    // ---------------------------------------------------------------- stats

    static void PrintStats(WorldData w)
    {
        int landPx = 0, riverPx = 0;
        foreach (byte b in w.Land) landPx += b;
        foreach (byte b in w.River) riverPx += b;
        var szL = new List<int>(); var szS = new List<int>();
        for (int p = 0; p < w.P; p++) (w.PLand[p] != 0 ? szL : szS).Add(w.PSize[p]);
        string Med(List<int> a) { if (a.Count == 0) return "-"; a.Sort(); return $"{a[0]}/{a[a.Count >> 1]}/{a[^1]}"; }
        var colours = new HashSet<int>();
        for (int i = 0; i < w.N; i++) colours.Add(BitConverter.ToInt32(w.BaseColor, i * 4));
        Console.WriteLine($"  P={w.P} land={landPx * 100.0 / w.N:F1}% prov land {szL.Count} sea {szS.Count}; size min/med/max land {Med(szL)} sea {Med(szS)}; " +
                          $"rivers {w.Rivers.Count} ({riverPx} px, {Enumerable.Range(0, w.P).Count(p => w.PRiver[p] != 0)} provinces); bodies {w.BodySize.Length}; {colours.Count} colours");
    }

    // ---------------------------------------------------------------- invariants

    static void CheckInvariants(WorldData w)
    {
        int before = _failures, P = w.P, N = w.N, W = w.W;
        for (int i = 0; i < N; i++)
        {
            int p = w.Prov[i];
            if (p < 0 || p >= P) { Fail($"prov out of range at {i}"); break; }
            if (w.PLand[p] != w.Land[i]) { Fail($"PLand mismatch at {i}"); break; }
            if (!float.IsFinite(w.Height[i])) { Fail($"Height NaN at {i}"); break; }
            if (w.Land[i] == 0 ? w.Biome[i] != 0 || w.Height[i] >= 0 : w.Biome[i] == 0 || w.Height[i] <= 0) { Fail($"land/biome/height inconsistent at {i}"); break; }
            if (w.BaseColor[i * 4 + 3] != 255) { Fail($"BaseColor alpha at {i}"); break; }
            if (w.River[i] != 0 && w.Land[i] == 0) { Fail($"river in water at {i}"); break; }
            if (w.TerrMaterial[i] >= TerrainPalette.Count || w.TerrStep[i] > 4 || (w.TerrMaterial[i] == TerrainPalette.Water) != (w.Land[i] == 0)) { Fail($"terrain material/step invalid at {i}"); break; }
            int c = TerrainPalette.Ramps[w.TerrMaterial[i]][w.TerrStep[i]];
            if (w.BaseColor[i * 4] != (byte)(c >> 16) || w.BaseColor[i * 4 + 1] != (byte)(c >> 8) || w.BaseColor[i * 4 + 2] != (byte)c) { Fail($"BaseColor is not ramp[material][step] at {i}"); break; }
        }
        if (w.PName.Distinct().Count() != P) Fail("province names are not unique");
        for (int p = 0; p < P; p++)
        {
            if (w.PSize[p] <= 0) { Fail($"empty province {p}"); break; }
            if (!float.IsFinite(w.PH[p]) || !float.IsFinite(w.PFert[p]) || w.PFert[p] < 0 || w.PFert[p] > 1) { Fail($"bad stats p{p}"); break; }
            if (w.Prov[w.PCY[p] * W + w.PCX[p]] != p) { Fail($"centre pixel of {p} outside it"); break; }
            if (string.IsNullOrEmpty(w.PName[p])) { Fail($"no name p{p}"); break; }
            if (w.BodyLand[w.PBody[p]] != w.PLand[p]) { Fail($"body land mismatch p{p}"); break; }
            var a = w.Adj[p];
            if (a.Length == 0 && P > 1) { Fail($"isolated province {p}"); break; }
            if (a.Contains(p) || a.Distinct().Count() != a.Length) { Fail($"adjacency self/duplicate at {p}"); break; }
            bool asym = false;
            foreach (int q in a) if (!w.Adj[q].Contains(p)) { Fail($"adjacency asymmetric {p}-{q}"); asym = true; break; }
            if (asym) break;
            bool coast = a.Any(q => w.PLand[q] == 0);
            if (w.PLand[p] != 0 && (w.PCoast[p] != 0) != coast) { Fail($"PCoast wrong at {p}"); break; }
        }
        // PRiver = the province has a river pixel
        var hasRiver = new bool[P];
        for (int i = 0; i < N; i++) if (w.River[i] != 0) hasRiver[w.Prov[i]] = true;
        for (int p = 0; p < P; p++) if (hasRiver[p] != (w.PRiver[p] != 0)) { Fail($"PRiver wrong at {p}"); break; }
        // pixel index
        if (w.PixOffset[0] != 0 || w.PixOffset[P] != N) Fail("PixOffset ends");
        else
            for (int p = 0; p < P; p++)
            {
                int a = w.PixOffset[p], e = w.PixOffset[p + 1];
                if (e - a != w.PSize[p]) { Fail($"PixOffset size p{p}"); break; }
                bool bad = false;
                for (int t = a; t < e; t++)
                    if (w.Prov[w.PixList[t]] != p || (t > a && w.PixList[t] <= w.PixList[t - 1])) { bad = true; break; }
                if (bad) { Fail($"PixList inconsistent p{p}"); break; }
            }
        // every province one connected piece (4-neighbour, x wraps) — independent flood fill
        var seen = new bool[N];
        var pieces = new int[P];
        var stack = new int[N];
        Span<int> nb = stackalloc int[4];
        for (int st = 0; st < N; st++)
        {
            if (seen[st]) continue;
            int p = w.Prov[st], sp = 0;
            pieces[p]++;
            stack[sp++] = st; seen[st] = true;
            while (sp > 0)
            {
                int j = stack[--sp], x = j % W, row = j - x;
                nb[0] = row + (x + 1) % W; nb[1] = row + (x + W - 1) % W; nb[2] = row > 0 ? j - W : -1; nb[3] = row < N - W ? j + W : -1;
                foreach (int k in nb) if (k >= 0 && !seen[k] && w.Prov[k] == p) { seen[k] = true; stack[sp++] = k; }
            }
        }
        int split = pieces.Count(c => c != 1);
        if (split > 0) Fail($"{split} provinces not connected ({Enumerable.Range(0, P).Count(p => pieces[p] != 1 && w.PLand[p] != 0)} land)");
        Console.WriteLine(_failures == before ? "  invariants OK" : "  INVARIANTS FAILED");
    }

    // ---------------------------------------------------------------- rivers

    /// <summary>
    /// The river network: finite polylines with flow; every river ends in water or on another river; no ruler-straight
    /// stretches; points ≤ 4 px apart; no «combs» (a river running within 3 px of another for most of its course); river pixels and polylines
    /// agree (every land point of a line is a river pixel, every river pixel lies on a line).
    /// </summary>
    static void CheckRivers(WorldData w)
    {
        if (w.Rivers.Count == 0) { Fail("no rivers"); return; }
        int before = _failures, W = w.W, H = w.H, mouths = 0, joins = 0, arms = 0, straight = 0, combs = 0, turns = 0;
        double length = 0, sinuous = 0;
        float gap = 0;
        var deltas = new List<string>();
        var id = new int[w.N];                                   // river index + 1 per rasterised pixel (last wins)
        for (int r = 0; r < w.Rivers.Count; r++)
            foreach (int i in Pixels(w.Rivers[r], W, H)) id[i] = r + 1;
        for (int i = 0; i < w.N; i++)
            if (w.River[i] != 0 && id[i] == 0) { Fail($"river pixel ({i % W},{i / W}) is on no polyline"); break; }
        for (int r = 0; r < w.Rivers.Count; r++)
        {
            var o = w.Rivers[r];
            if (o.Xs.Length < 2 || o.Xs.Length != o.Ys.Length || o.Xs.Any(v => !float.IsFinite(v)) || o.Ys.Any(v => !float.IsFinite(v)) || o.MinY > o.MaxY)
            { Fail("bad river polyline"); return; }
            if (o.Flow == null || o.Flow.Length != o.Xs.Length || o.Flow.Any(f => f < 0 || f > 1)) { Fail("river flow missing or out of 0..1"); return; }
            if (o.Ys.Min() != o.MinY || o.Ys.Max() != o.MaxY) { Fail("river MinY/MaxY wrong"); return; }
            for (int k = 0; k + 1 < o.Xs.Length; k++)
            {
                int i = Pix(o.Xs[k], o.Ys[k], W, H);
                if (w.Land[i] != 0 && w.River[i] == 0) { Fail($"river line point ({o.Xs[k]:F1},{o.Ys[k]:F1}) is not a river pixel"); return; }
                float dx = o.Xs[k + 1] - o.Xs[k], dy = o.Ys[k + 1] - o.Ys[k];
                if (dx * dx + dy * dy > 16) { Fail($"river points {MathF.Sqrt(dx * dx + dy * dy):F1} px apart at ({o.Xs[k]:F0},{o.Ys[k]:F0})"); return; }
                length += MathF.Sqrt(dx * dx + dy * dy);
                gap = MathF.Max(gap, MathF.Sqrt(dx * dx + dy * dy));
            }
            if (w.Rivers.Any(q => q != o && Near(q, o.Xs[0], o.Ys[0], W, .01f)) && arms++ < 3) deltas.Add($"{o.Xs[0]:F0},{o.Ys[0]:F0}");
            if (w.Land[Pix(o.Xs[^1], o.Ys[^1], W, H)] == 0) mouths++;
            else if (w.Rivers.Any(q => q != o && Near(q, o.Xs[^1], o.Ys[^1], W, .01f))) joins++;
            // a ruler-straight stretch: 24 points (~40 px) all within 0.35 px of their chord (at 2560 wide)
            for (int k = 0; k + 24 < o.Xs.Length; k += 6)
            {
                float ax = o.Xs[k], ay = o.Ys[k], bx = o.Xs[k + 24], by = o.Ys[k + 24], len = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                float dev = 0;
                for (int q = k; q <= k + 24; q++) dev = MathF.Max(dev, MathF.Abs((o.Xs[q] - ax) * (by - ay) - (o.Ys[q] - ay) * (bx - ax)) / Math.Max(len, 1e-3f));
                if (dev < .35f * Math.Min(1, W / 2560f)) { straight++; break; }   // smaller worlds have proportionally smaller bends
            }
            // sinuosity over ~60 px windows (arc / chord); 1.0 = straight
            for (int k = 0; k + 36 < o.Xs.Length; k += 36)
            {
                float arc = 0;
                for (int q = k; q < k + 36; q++) arc += MathF.Sqrt((o.Xs[q + 1] - o.Xs[q]) * (o.Xs[q + 1] - o.Xs[q]) + (o.Ys[q + 1] - o.Ys[q]) * (o.Ys[q + 1] - o.Ys[q]));
                float ch = MathF.Sqrt((o.Xs[k + 36] - o.Xs[k]) * (o.Xs[k + 36] - o.Xs[k]) + (o.Ys[k + 36] - o.Ys[k]) * (o.Ys[k + 36] - o.Ys[k]));
                sinuous += arc / Math.Max(ch, 1e-3f); turns++;
            }
            // comb tooth: most of the river (away from its ends) runs within 3 px of another river
            int near = 0, cnt = 0;
            for (int k = 8; k < o.Xs.Length - 8; k++)
            {
                cnt++;
                int cx = (int)MathF.Floor(o.Xs[k]), cy = (int)MathF.Floor(o.Ys[k]);
                bool hit = false;
                for (int dy = -3; dy <= 3 && !hit; dy++)
                    for (int dx = -3; dx <= 3 && !hit; dx++)
                    {
                        int y = cy + dy;
                        if (y < 0 || y >= H) continue;
                        int v = id[y * W + ((cx + dx) % W + W) % W];
                        hit = v != 0 && v != r + 1;
                    }
                if (hit) near++;
            }
            if (cnt >= 10 && near * 2 > cnt) combs++;
        }
        double sin = turns > 0 ? sinuous / turns : 1;
        Console.WriteLine($"  rivers: {w.Rivers.Count} ({mouths} reach water, {joins} join another river, {arms} delta arms (at {string.Join(" ", deltas)}), {straight} ruler-straight, {combs} combs), " +
                          $"{length:F0} px long, sinuosity {sin:F3}, points ≤ {gap:F1} px apart");
        if (mouths + joins != w.Rivers.Count) Fail($"{w.Rivers.Count - mouths - joins} rivers end in the middle of the land");
        if (straight > 0) Fail($"{straight} rivers have a ruler-straight stretch");
        if (combs > 0) Fail($"{combs} rivers run beside another for most of their course");
        if (sin < 1.04) Fail($"rivers are too straight (sinuosity {sin:F3} < 1.04)");
        if (_failures == before) Console.WriteLine("  river network OK");
    }

    /// <summary>Pixels a polyline passes through (the generator's rasterisation: half-pixel steps).</summary>
    static IEnumerable<int> Pixels(WorldData.RiverPath o, int W, int H)
    {
        for (int k = 0; k + 1 < o.Xs.Length; k++)
        {
            double x0 = o.Xs[k], y0 = o.Ys[k], dx = o.Xs[k + 1] - x0, dy = o.Ys[k + 1] - y0;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) * 2));
            for (int q = 0; q <= steps; q++) yield return Pix(x0 + dx * q / steps, y0 + dy * q / steps, W, H);
        }
    }

    static int Pix(double x, double y, int W, int H) => Math.Clamp((int)Math.Floor(y), 0, H - 1) * W + (((int)Math.Floor(x) % W) + W) % W;

    static bool Near(WorldData.RiverPath o, float x, float y, int W, float eps)
    {
        for (int k = 0; k < o.Xs.Length; k++)
        {
            float dx = MathF.Abs(o.Xs[k] - x) % W; dx = MathF.Min(dx, W - dx);
            if (dx <= eps && MathF.Abs(o.Ys[k] - y) <= eps) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- terrain look

    /// <summary>
    /// Pixel-art rules of ART_BIBLE §2–3: land in clusters rather than salt (few land pixels whose 4 neighbours all
    /// differ from them), a bounded palette, and the expected surfaces present (beaches, snow caps, canopy highlights).
    /// </summary>
    static void CheckTerrain(WorldData w)
    {
        int W = w.W, H = w.H, landPx = 0, lonely = 0, sand = 0, snow = 0;
        var mats = new int[TerrainPalette.Count];
        for (int y = 1; y < H - 1; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (w.Land[i] == 0) continue;
                landPx++;
                mats[w.TerrMaterial[i]]++;
                int c = Col(w, i);
                if (c != Col(w, y * W + (x + 1) % W) && c != Col(w, y * W + (x + W - 1) % W) && c != Col(w, i - W) && c != Col(w, i + W)) lonely++;
            }
        sand = mats[TerrainPalette.Sand]; snow = mats[TerrainPalette.Snow];
        double salt = lonely * 100.0 / Math.Max(1, landPx);
        Console.WriteLine($"  terrain: {salt:F1}% lone land pixels, sand {sand} px, snow {snow} px, " +
                          string.Join(" ", Enumerable.Range(0, TerrainPalette.Count).Where(m => mats[m] > 0).Select(m => $"m{m}:{mats[m] * 100.0 / landPx:F1}%")));
        if (salt > 14) Fail($"terrain is salty: {salt:F1}% of land pixels differ from all 4 neighbours");
        if (sand == 0) Fail("no beaches");
        if (snow == 0) Fail("no snow");
    }

    static int Col(WorldData w, int i) => BitConverter.ToInt32(w.BaseColor, i * 4);

    // ---------------------------------------------------------------- golden hashes

    sealed class Fnv32
    {
        uint _h = 0x811c9dc5;
        public void B(int v) => _h = (_h ^ (byte)v) * 16777619;
        public void I32(int v) { B(v); B(v >> 8); B(v >> 16); B(v >> 24); }
        public void F32(float v) => I32(BitConverter.SingleToInt32Bits(v));
        public void Bytes(ReadOnlySpan<byte> a) { foreach (byte b in a) B(b); }
        public void Str(string s) { foreach (char c in s) { B(c); B(c >> 8); } }
        public string Hex => _h.ToString("x8");
    }

    static string One(Action<Fnv32> f) { var h = new Fnv32(); f(h); return h.Hex; }
    static string Fnv(string s) => One(h => h.Str(s));
    static string Raw<T>(T[] a) where T : struct => One(h => h.Bytes(MemoryMarshal.AsBytes(a.AsSpan())));

    static List<string> Hashes(WorldData w) => new()
    {
        $"P {w.P}",
        "land " + Raw(w.Land), "hgt " + Raw(w.Height), "biome " + Raw(w.Biome), "baseCol " + Raw(w.BaseColor),
        "terrMat " + Raw(w.TerrMaterial), "terrStep " + Raw(w.TerrStep), "river " + Raw(w.River), "prov " + Raw(w.Prov),
        "pSize " + Raw(w.PSize), "pLand " + Raw(w.PLand), "pCX " + Raw(w.PCX), "pCY " + Raw(w.PCY), "pBiome " + Raw(w.PBiome), "pH " + Raw(w.PH),
        "pRiver " + Raw(w.PRiver), "pCoast " + Raw(w.PCoast), "pFert " + Raw(w.PFert), "pBody " + Raw(w.PBody),
        "adj " + One(h => { foreach (var l in w.Adj) { h.I32(l.Length); foreach (int v in l) h.I32(v); } }),
        "names " + One(h => { foreach (var n in w.PName) { h.Str(n); h.Str("|"); } }),
        "rivers " + One(h =>
        {
            h.I32(w.Rivers.Count);
            foreach (var r in w.Rivers)
            {
                h.I32(r.Xs.Length);
                for (int k = 0; k < r.Xs.Length; k++) { h.F32(r.Xs[k]); h.F32(r.Ys[k]); h.F32(r.Flow[k]); }
                h.F32(r.MinY); h.F32(r.MaxY);
            }
        }),
    };

    /// <summary>reference/ beside WorldGenTests.csproj (found by walking up from the build output).</summary>
    static string SourceReferenceDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "WorldGenTests.csproj"))) return Path.Combine(d.FullName, "reference");
        return null;
    }

    static void CompareGolden(List<string> mine, string refFile)
    {
        var theirs = File.ReadAllLines(refFile).Where(l => l.Length > 0).ToList();
        var diff = theirs.Where(b => !mine.Contains(b)).Select(b => b.Split(' ')[0]).ToList();
        diff.AddRange(mine.Select(l => l.Split(' ')[0]).Where(k => !theirs.Any(t => t.Split(' ')[0] == k)));
        if (diff.Count > 0) Fail("output differs from the golden reference in: " + string.Join(", ", diff.Distinct()) + " (intended? check the PNGs and rerun with --bless)");
        else Console.WriteLine($"  vs golden reference: IDENTICAL ({theirs.Count} arrays)");
    }

    // ---------------------------------------------------------------- PNG output

    static void WritePngs(WorldData w, string dir, string crops)
    {
        Directory.CreateDirectory(dir);
        int f = Math.Max(1, (w.W + 1599) / 1600), ow = w.W / f, oh = w.H / f;
        var ter = new byte[ow * oh * 4];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int i = y * f * w.W + x * f, o = (y * ow + x) * 4;
                bool riv = false;
                for (int a = 0; a < f; a++) for (int b = 0; b < f; b++) riv |= w.River[i + a * w.W + b] != 0;
                for (int c = 0; c < 4; c++) ter[o + c] = w.BaseColor[i * 4 + c];
                if (riv) { ter[o] = 77; ter[o + 1] = 127; ter[o + 2] = 166; }
            }
        Png(Path.Combine(dir, $"map_{w.Seed}.png"), ow, oh, ter);
        // ×3 crops with river pixels and province borders, like the map at zoom 3
        var at = (crops ?? $"{w.W / 5},{w.H / 3};{w.W / 2},{w.H / 2};{w.W * 3 / 4},{w.H * 2 / 3}").Split(';').Select(s => s.Split(',').Select(int.Parse).ToArray()).ToList();
        const int cw = 400, ch = 240, z = 3;
        for (int n = 0; n < at.Count; n++)
        {
            var crop = new byte[cw * z * ch * z * 4];
            int cx = at[n][0] - cw / 2, cy = Math.Clamp(at[n][1] - ch / 2, 0, w.H - ch);
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    int gx = ((cx + x) % w.W + w.W) % w.W, gy = cy + y, i = gy * w.W + gx;
                    int p = w.Prov[i], r = w.Prov[gy * w.W + (gx + 1) % w.W], d = gy + 1 < w.H ? w.Prov[i + w.W] : p;
                    byte cr = w.BaseColor[i * 4], cg = w.BaseColor[i * 4 + 1], cb = w.BaseColor[i * 4 + 2];
                    if (r != p || d != p) { cr = (byte)(cr * .72); cg = (byte)(cg * .72); cb = (byte)(cb * .72); }
                    if (w.River[i] != 0) { cr = 77; cg = 127; cb = 166; }
                    for (int a = 0; a < z; a++)
                        for (int b = 0; b < z; b++)
                        {
                            int o = ((y * z + a) * cw * z + x * z + b) * 4;
                            crop[o] = cr; crop[o + 1] = cg; crop[o + 2] = cb; crop[o + 3] = 255;
                        }
                }
            Png(Path.Combine(dir, $"crop_{w.Seed}_{n}.png"), cw * z, ch * z, crop);
        }
    }

    static void Png(string file, int w, int h, byte[] rgba)
    {
        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
            for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(rgba, y * w * 4, w * 4); }
        using var fs = File.Create(file);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var hdr = new byte[13];
        BE(hdr, 0, w); BE(hdr, 4, h); hdr[8] = 8; hdr[9] = 6;
        Chunk(fs, "IHDR", hdr); Chunk(fs, "IDAT", raw.ToArray()); Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(td);
        var crc = new byte[4]; BE(crc, 0, (int)Crc32(td)); s.Write(crc);
    }

    static uint Crc32(byte[] b)
    {
        uint c = 0xffffffff;
        foreach (byte x in b)
        {
            c ^= x;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }
}
