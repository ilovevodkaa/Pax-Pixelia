using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace WorldGenTests;

/// <summary>
/// Console checks for WorldGen + NationGen: timings, stats (same format as the mockup's node harness), invariants,
/// determinism across runs and thread counts, per-array hashes, PNGs.
///
///   dotnet run -c Release [-p:GameDir=&lt;game copy&gt;/] -- [1337,42,777] [--size=2560x1440] [--threads=1,3] [--nodet]
///       [--hashes=&lt;dir&gt;] [--compare=&lt;dir&gt;] [--png=&lt;dir&gt;] [--bench=N] [--bless]
/// Two references per seed (in reference/, or --compare):
///  • js_SEED.txt (jsdump.js on docs/mockups/js): relief, climate, colour and provinces must match the mockup bit for bit;
///  • cs_SEED.txt: what the game deliberately does its own way — the drainage-network rivers (and river-dependent
///    fertility) and the nations — pinned to the last blessed C# output; --bless rewrites it after an intended change.
/// Exit code 0 = all invariants hold, results deterministic and identical to both references where they exist.
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
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine($"{Environment.ProcessorCount} logical cores, {W}x{H}");
        CheckNoiseParity();
        CheckHotKernels();

        foreach (int seed in seeds)
        {
            var sw = Stopwatch.StartNew();
            var w = WorldGen.Generate(seed, W, H);
            double tGen = sw.Elapsed.TotalMilliseconds;
            var s = NationGen.CreateInitialState(w);
            double tAll = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"seed {seed} {W}x{H} total {tAll:F0}ms (worldgen {tGen:F0} + nations {tAll - tGen:F0}) stages: " +
                              string.Join(" | ", w.GenTimings.Select(t => $"{t.Stage} {t.Ms:F0}")));
            PrintStats(w, s);
            CheckInvariants(w, s);

            var hashes = Hashes(w, s);
            string full = Fnv(string.Join("\n", hashes));
            if (opt.TryGetValue("hashes", out var hdir)) File.WriteAllLines(Path.Combine(hdir, $"cs_{seed}.txt"), hashes);
            string refDir = opt.GetValueOrDefault("compare") ?? Path.Combine(AppContext.BaseDirectory, "reference");
            string jsRef = Path.Combine(refDir, $"js_{seed}.txt"), csRef = Path.Combine(refDir, $"cs_{seed}.txt");
            if (File.Exists(jsRef)) CompareMockup(hashes, jsRef);
            if (opt.ContainsKey("bless"))
            {
                // also next to the sources, so the blessed file is what the next build copies
                foreach (var dir in new[] { refDir, SourceReferenceDir() }.Where(d => d != null).Distinct())
                    if (Directory.Exists(dir)) File.WriteAllLines(Path.Combine(dir, $"cs_{seed}.txt"), hashes.Where(l => OwnKeys.Contains(l.Split(' ')[0])));
                Console.WriteLine($"  blessed cs_{seed}.txt");
            }
            else if (File.Exists(csRef)) CompareOwn(hashes, csRef);
            else Console.WriteLine($"  no cs_{seed}.txt — run with --bless to pin the rivers and nations");
            CheckRivers(w);

            if (!opt.ContainsKey("nodet"))
            {
                var res = new List<string>();
                foreach (int t in threads.Prepend(-1))
                {
                    WorldGen.MaxThreads = t;
                    var w2 = WorldGen.Generate(seed, W, H);
                    string h2 = Fnv(string.Join("\n", Hashes(w2, NationGen.CreateInitialState(w2))));
                    res.Add($"threads {(t < 0 ? "all" : t)}: {(h2 == full ? "same" : "DIFFERENT")}");
                    if (h2 != full) Fail($"non-deterministic output with {t} threads");
                }
                WorldGen.MaxThreads = -1;
                Console.WriteLine($"  determinism (full hash {full}): {string.Join(", ", res)}");
            }
            if (opt.TryGetValue("png", out var pdir)) WritePngs(w, s, pdir);
        }

        if (opt.TryGetValue("bench", out var nb))
        {
            int n = int.Parse(nb);
            var times = new List<double>();
            for (int k = 0; k < n; k++)
            {
                var sw = Stopwatch.StartNew();
                var w = WorldGen.Generate(seeds[0], W, H);
                NationGen.CreateInitialState(w);
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

    // ---------------------------------------------------------------- stats (node harness format)

    static void PrintStats(WorldData w, GameState s)
    {
        int landPx = 0;
        foreach (byte b in w.Land) landPx += b;
        var szL = new List<int>(); var szS = new List<int>();
        for (int p = 0; p < w.P; p++) (w.PLand[p] != 0 ? szL : szS).Add(w.PSize[p]);
        string Med(List<int> a) { if (a.Count == 0) return "-"; a.Sort(); return $"{a[0]}/{a[a.Count >> 1]}/{a[^1]}"; }
        var nat = s.NationCapital.Select((_, n) => s.Owner.Count(o => o == n));
        int h = 0;
        for (int i = 0; i < w.N; i += 7) h = unchecked(h * 31 + w.Prov[i] + BitConverter.ToInt32(w.BaseColor, i * 4));
        Console.WriteLine($"  P={w.P} land={landPx * 100.0 / w.N:F1}% prov land {szL.Count} sea {szS.Count}; size min/med/max land {Med(szL)} sea {Med(szS)}; " +
                          $"rivers {w.Rivers.Count}; nations {string.Join(",", nat)}; hash {h}");
        var names = new Dictionary<string, int>();
        var order = new List<string>();
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] != 0) continue;
            string k = w.PName[p].Split(' ')[0];
            if (!names.ContainsKey(k)) { names[k] = 0; order.Add(k); }
            names[k]++;
        }
        Console.WriteLine($"  water names {{{string.Join(",", order.Select(k => $"\"{k}\":{names[k]}"))}}} routes {s.Routes.Count} caps {s.NationCapital.Length}" +
                          $"; bodies {w.BodySize.Length} (ocean {w.BodySize[w.Ocean]} px)");
    }

    // ---------------------------------------------------------------- invariants

    static void CheckInvariants(WorldData w, GameState s)
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
        }
        if (w.PName.Distinct().Count() != P) Fail("province names are not unique");
        for (int p = 0; p < P; p++)
        {
            if (w.PSize[p] <= 0) { Fail($"empty province {p}"); break; }
            if (!float.IsFinite(w.PH[p]) || !float.IsFinite(w.PFert[p]) || !float.IsFinite(s.Pop[p]) || w.PFert[p] < 0 || w.PFert[p] > 1) { Fail($"bad stats p{p}"); break; }
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
        // rivers
        foreach (var r in w.Rivers)
            if (r.Xs.Length < 2 || r.Xs.Length != r.Ys.Length || r.Xs.Any(v => !float.IsFinite(v)) || r.MinY > r.MaxY) { Fail("bad river polyline"); break; }
        // nations
        for (int n = 0; n < s.NationCapital.Length; n++)
        {
            int c = s.NationCapital[n];
            if (w.PLand[c] == 0 || s.Owner[c] != n || s.CapitalOf[c] != n) Fail($"capital of nation {n} invalid");
            if (!s.Buildings[c].Contains(Data.Bld.Shrine)) Fail($"capital of nation {n} has no shrine");
        }
        if (s.NationCapital.Distinct().Count() != s.NationCapital.Length) Fail("two nations share a capital");
        for (int p = 0; p < P; p++)
        {
            if (s.Owner[p] >= 0 && w.PLand[p] == 0) { Fail($"owned sea province {p}"); break; }
            if (s.Buildings[p].Count > s.Slots[p]) { Fail($"more buildings than slots at {p}"); break; }
            if (s.Owner[p] >= 0 && s.Religion[p] != Data.Nations[s.Owner[p]].Religion) { Fail($"religion of owned province {p}"); break; }
        }
        foreach (var r in s.Routes)
            for (int k = 1; k < r.Length; k++)
                if (!w.Adj[r[k - 1]].Contains(r[k])) { Fail("trade route not along adjacency"); break; }
        Console.WriteLine(_failures == before ? "  invariants OK" : "  INVARIANTS FAILED");
    }

    // ---------------------------------------------------------------- hashes (format of jsdump.js)

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
    static string Ints<T>(T[] a, Func<T, int> conv) => One(h => { foreach (var v in a) h.I32(conv(v)); });
    static string Raw<T>(T[] a) where T : struct => One(h => h.Bytes(MemoryMarshal.AsBytes(a.AsSpan())));

    static List<string> Hashes(WorldData w, GameState s)
    {
        var o = new List<string>
        {
            $"P {w.P}",
            "land " + Raw(w.Land), "hgt " + Raw(w.Height), "biome " + Raw(w.Biome), "baseCol " + Raw(w.BaseColor), "river " + Raw(w.River), "prov " + Raw(w.Prov),
            "pSize " + Raw(w.PSize), "pLand " + Raw(w.PLand), "pCX " + Raw(w.PCX), "pCY " + Raw(w.PCY), "pBiome " + Raw(w.PBiome), "pH " + Raw(w.PH),
            "pRiver " + Raw(w.PRiver), "pCoast " + Raw(w.PCoast), "pFert " + Raw(w.PFert),
            "adj " + One(h => { foreach (var l in w.Adj) { h.I32(l.Length); foreach (int v in l) h.I32(v); } }),
            "names " + One(h => { foreach (var n in MockupNames(w)) { h.Str(n); h.Str("|"); } }),
            "rivers " + One(h =>
            {
                h.I32(w.Rivers.Count);
                foreach (var r in w.Rivers)
                {
                    h.I32(r.Xs.Length);
                    for (int k = 0; k < r.Xs.Length; k++) { h.F32(r.Xs[k]); h.F32(r.Ys[k]); }
                    h.F32(r.MinY); h.F32(r.MaxY);
                }
            }),
            "caps " + Raw(s.NationCapital) + " " + string.Join(",", s.NationCapital),
            "own " + Ints(s.Owner, v => v), "pop " + Raw(s.Pop), "rel " + Ints(s.Religion, v => v), "mood " + Ints(s.Mood, v => v), "slots " + Ints(s.Slots, v => v),
            "bld " + One(h => { foreach (var l in s.Buildings) { h.I32(l.Count); foreach (var b in l) h.I32((int)b); } }),
            "ore " + One(h => { for (int p = 0; p < w.P; p++) { h.I32(s.Ore[p]); h.I32(s.OreFound[p] ? 1 : 0); } }),
            "cap " + Ints(s.CapitalOf, v => v), "town " + Ints(s.IsTown, v => v ? 1 : 0),
            "routes " + One(h => { h.I32(s.Routes.Count); foreach (var r in s.Routes) { h.I32(r.Length); foreach (int v in r) h.I32(v); } }),
        };
        return o;
    }

    /// <summary>Names as the mockup made them: WorldGen renames later duplicates and keeps the originals in Renamed.</summary>
    static string[] MockupNames(WorldData w)
    {
        var names = (string[])w.PName.Clone();
        foreach (var (p, name) in w.Renamed) names[p] = name;
        return names;
    }

    /// <summary>reference/ beside WorldGenTests.csproj (found by walking up from the build output).</summary>
    static string SourceReferenceDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "WorldGenTests.csproj"))) return Path.Combine(d.FullName, "reference");
        return null;
    }

    /// <summary>Arrays the game computes its own way; everything else must still equal the mockup.</summary>
    static readonly string[] OwnKeys = { "river", "rivers", "pRiver", "pFert", "caps", "own", "pop", "rel", "mood", "slots", "bld", "ore", "cap", "town", "routes" };

    /// <summary>Relief, climate, colour and provinces vs the JS mockup (jsdump.js).</summary>
    static void CompareMockup(List<string> mine, string refFile)
    {
        var theirs = File.ReadAllLines(refFile).Where(l => l.Length > 0).ToList();
        var diff = new List<string>();
        foreach (string b in theirs)
        {
            string key = b.Split(' ')[0];
            if (OwnKeys.Contains(key)) continue;
            string a = mine.FirstOrDefault(l => l.Split(' ')[0] == key);
            if (a != b) diff.Add(key);
        }
        if (diff.Count > 0) Fail("differs from the JS mockup in: " + string.Join(", ", diff));
        else Console.WriteLine($"  vs JS mockup: IDENTICAL ({theirs.Count(l => !OwnKeys.Contains(l.Split(' ')[0]))} arrays: relief, climate, colour, provinces)");
    }

    /// <summary>Rivers and nations vs the last blessed C# output.</summary>
    static void CompareOwn(List<string> mine, string refFile)
    {
        var theirs = File.ReadAllLines(refFile).Where(l => l.Length > 0).ToList();
        var diff = theirs.Where(b => !mine.Contains(b)).Select(b => b.Split(' ')[0]).ToList();
        if (diff.Count > 0) Fail("rivers/nations changed vs the blessed C# reference in: " + string.Join(", ", diff) + " (intended? rerun with --bless)");
        else Console.WriteLine($"  vs blessed C# reference: IDENTICAL ({theirs.Count} arrays: rivers, fertility, nations)");
    }

    /// <summary>The river network: trees that reach water, tributaries that end on another river, no ladders.</summary>
    static void CheckRivers(WorldData w)
    {
        if (w.Rivers.Count == 0) { Fail("no rivers"); return; }
        int mouths = 0, joins = 0, straight = 0;
        foreach (var r in w.Rivers)
        {
            if (r.Flow == null || r.Flow.Length != r.Xs.Length || r.Flow.Any(f => f < 0 || f > 1)) { Fail("river flow missing or out of 0..1"); return; }
            int x = ((int)MathF.Floor(r.Xs[^1]) % w.W + w.W) % w.W, y = Math.Clamp((int)MathF.Floor(r.Ys[^1]), 0, w.H - 1);
            if (w.Land[y * w.W + x] == 0) mouths++;
            else if (w.Rivers.Any(o => o != r && Near(o, r.Xs[^1], r.Ys[^1], w.W))) joins++;
            // a ruler-straight stretch: 24 points (~48 px) all within 0.35 px of their chord
            for (int k = 0; k + 24 < r.Xs.Length; k += 6)
            {
                float ax = r.Xs[k], ay = r.Ys[k], bx = r.Xs[k + 24], by = r.Ys[k + 24], len = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                float dev = 0;
                for (int q = k; q <= k + 24; q++) dev = MathF.Max(dev, MathF.Abs((r.Xs[q] - ax) * (by - ay) - (r.Ys[q] - ay) * (bx - ax)) / Math.Max(len, 1e-3f));
                if (dev < .35f) { straight++; break; }
            }
        }
        Console.WriteLine($"  rivers: {w.Rivers.Count} ({mouths} reach water, {joins} join another river, {straight} with a ruler-straight stretch)");
        if (mouths + joins != w.Rivers.Count) Fail($"{w.Rivers.Count - mouths - joins} rivers end in the middle of the land");
        if (straight > 0) Fail($"{straight} rivers have a ruler-straight stretch");
    }

    static bool Near(WorldData.RiverPath o, float x, float y, int W)
    {
        for (int k = 0; k < o.Xs.Length; k++)
        {
            float dx = MathF.Abs(o.Xs[k] - x) % W; dx = MathF.Min(dx, W - dx);
            if (dx < 2.5f && MathF.Abs(o.Ys[k] - y) < 2.5f) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- PNG output

    static void WritePngs(WorldData w, GameState s, string dir)
    {
        Directory.CreateDirectory(dir);
        int f = Math.Max(1, (w.W + 1599) / 1600), ow = w.W / f, oh = w.H / f;
        var ter = new byte[ow * oh * 4];
        var pol = new byte[ow * oh * 4];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int i = y * f * w.W + x * f, o = (y * ow + x) * 4;
                bool riv = false;
                for (int a = 0; a < f; a++) for (int b = 0; b < f; b++) riv |= w.River[i + a * w.W + b] != 0;
                for (int c = 0; c < 4; c++) ter[o + c] = w.BaseColor[i * 4 + c];
                if (riv) { ter[o] = 80; ter[o + 1] = 160; ter[o + 2] = 224; }
                int p = w.Prov[i], own = s.Owner[p];
                for (int c = 0; c < 4; c++) pol[o + c] = ter[o + c];
                if (own >= 0)
                {
                    var n = Data.Nations[own];
                    pol[o] = (byte)(ter[o] * .45 + n.R * .55); pol[o + 1] = (byte)(ter[o + 1] * .45 + n.G * .55); pol[o + 2] = (byte)(ter[o + 2] * .45 + n.B * .55);
                }
                if (s.CapitalOf[p] >= 0) { pol[o] = 255; pol[o + 1] = 255; pol[o + 2] = 255; }
            }
        Png(Path.Combine(dir, $"map_{w.Seed}.png"), ow, oh, ter);
        Png(Path.Combine(dir, $"pol_{w.Seed}.png"), ow, oh, pol);
        // 1:1 crop around the player capital with province borders
        const int cw = 800, ch = 450;
        var crop = new byte[cw * ch * 4];
        int cap = s.NationCapital[0], cx = w.PCX[cap] - cw / 2, cy = Math.Clamp(w.PCY[cap] - ch / 2, 0, w.H - ch);
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int gx = ((cx + x) % w.W + w.W) % w.W, gy = cy + y, i = gy * w.W + gx, o = (y * cw + x) * 4;
                int p = w.Prov[i], r = w.Prov[gy * w.W + (gx + 1) % w.W], d = gy + 1 < w.H ? w.Prov[i + w.W] : p;
                for (int c = 0; c < 4; c++) crop[o + c] = w.BaseColor[i * 4 + c];
                if (r != p || d != p) { if (w.Land[i] != 0) { crop[o] = crop[o + 1] = crop[o + 2] = 32; } else { crop[o] = 56; crop[o + 1] = 72; crop[o + 2] = 96; } }
                if (w.River[i] != 0) { crop[o] = 64; crop[o + 1] = 120; crop[o + 2] = 176; }
            }
        Png(Path.Combine(dir, $"crop_{w.Seed}.png"), cw, ch, crop);
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
