using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using PaxPixelia.Core;

namespace PaxPixelia.World;

/// <summary>
/// Provinces (mockup provSeeds/wgP/genProvinces): relief-following Voronoi cells inside each land mass / water body,
/// fragment and tiny-province merging, per-province statistics, adjacency and names.
/// </summary>
internal sealed partial class WorldBuilder
{
    const int LandCell = 16, SeaCell = 64;      // seed grid spacing, px
    const double ReliefPenalty = 48;            // distance penalty per unit of height difference (land only)
    const int TinyProvince = 22;                // land provinces smaller than this join a neighbour

    /// <summary>Province seeds: jittered grids, land and sea separate; every body gets at least one seed.</summary>
    sealed class Seeds
    {
        public float[] X, Y, H;         // float32 like the mockup's typed arrays
        public int[] Body;
        public int[] LandGrid, SeaGrid; // seed per grid cell or -1
        public int LandW, LandH, SeaW, SeaH;
        public int[] BodyOffset, BodyList; // seeds of body c: BodyList[BodyOffset[c] .. BodyOffset[c+1])
        public int Count => X.Length;
    }

    Seeds ProvinceSeeds()
    {
        int w = _w, h = _h, s = _s;
        var land = _d.Land; var hgt = _d.Height; var body = _bodies.Id;
        var sd = new Seeds
        {
            LandW = w / LandCell, LandH = (h + LandCell - 1) / LandCell,
            SeaW = w / SeaCell, SeaH = (h + SeaCell - 1) / SeaCell
        };
        var xs = new List<float>(); var ys = new List<float>(); var hs = new List<float>(); var bs = new List<int>();
        int Add(double x, double y)
        {
            int i = (int)y * w + (int)x;
            xs.Add((float)x); ys.Add((float)y); bs.Add(body[i]); hs.Add(hgt[i]);
            return xs.Count - 1;
        }
        sd.LandGrid = new int[sd.LandW * sd.LandH]; Array.Fill(sd.LandGrid, -1);
        for (int gy = 0; gy < sd.LandH; gy++)
            for (int gx = 0; gx < sd.LandW; gx++)
            {
                double x = gx * LandCell + (.15 + .7 * Noise.H2(gx, gy, s + 501)) * LandCell;
                double y = Math.Min(h - 1, gy * LandCell + (.15 + .7 * Noise.H2(gx, gy, s + 502)) * LandCell);
                if (land[(int)y * w + (int)x] != 0) sd.LandGrid[gy * sd.LandW + gx] = Add(x, y);
            }
        sd.SeaGrid = new int[sd.SeaW * sd.SeaH]; Array.Fill(sd.SeaGrid, -1);
        for (int gy = 0; gy < sd.SeaH; gy++)
            for (int gx = 0; gx < sd.SeaW; gx++)
            {
                double x = gx * SeaCell + (.2 + .6 * Noise.H2(gx, gy, s + 503)) * SeaCell;
                double y = Math.Min(h - 1, gy * SeaCell + (.2 + .6 * Noise.H2(gx, gy, s + 504)) * SeaCell);
                if (land[(int)y * w + (int)x] == 0) sd.SeaGrid[gy * sd.SeaW + gx] = Add(x, y);
            }
        int nb = _bodies.Count;
        var cnt = new int[nb];
        foreach (int c in bs) cnt[c]++;
        for (int c = 0; c < nb; c++)
            if (cnt[c] == 0) { int f = _bodies.First[c]; Add(f % w + .5, f / w + .5); cnt[c]++; }

        sd.X = xs.ToArray(); sd.Y = ys.ToArray(); sd.H = hs.ToArray(); sd.Body = bs.ToArray();
        sd.BodyOffset = new int[nb + 1];
        for (int c = 0; c < nb; c++) sd.BodyOffset[c + 1] = sd.BodyOffset[c] + cnt[c];
        sd.BodyList = new int[sd.Count];
        var fill = sd.BodyOffset[..nb];
        for (int k = 0; k < sd.Count; k++) sd.BodyList[fill[sd.Body[k]]++] = k;
        return sd;
    }

    /// <summary>Pixel → nearest seed of the same body, with jittered coordinates (ragged borders) and, on land, the height
    /// difference as a third distance axis so borders follow ridges and valleys.</summary>
    void AssignProvinces(Seeds sd)
    {
        int w = _w, h = _h, s = _s, jitK = _k.Jit;
        double half = w / 2.0;
        var land = _d.Land; var hgt = _d.Height; var body = _bodies.Id; var prov = _d.Prov;
        float[] sx = sd.X, sy = sd.Y, sh = sd.H; int[] sb = sd.Body;
        Parallel.For(0, h, _po, [MethodImpl(Hot)] (y) =>
        {
            for (int x = 0, i = y * w; x < w; x++, i++)
            {
                int c = body[i];
                bool isLand = land[i] != 0;
                double jx = x + GenScale.JitA * (Fbm(x, y, jitK, 2, s + 90) * 2 - 1);
                double jy = y + GenScale.JitA * (Fbm(x, y, jitK, 2, s + 91) * 2 - 1);
                int cell = isLand ? LandCell : SeaCell, gw = isLand ? sd.LandW : sd.SeaW, gh = isLand ? sd.LandH : sd.SeaH;
                int[] grid = isLand ? sd.LandGrid : sd.SeaGrid;
                int gx0 = (int)Math.Floor(jx / cell), gy0 = (int)Math.Floor(jy / cell);
                double hi = hgt[i], pw = isLand ? ReliefPenalty : 0, bestD = 1e18;
                int best = -1;
                for (int gy = gy0 - 2; gy <= gy0 + 2; gy++)
                {
                    if (gy < 0 || gy >= gh) continue;
                    int rowBase = gy * gw;
                    for (int gx = gx0 - 2; gx <= gx0 + 2; gx++)
                    {
                        int k = grid[rowBase + (gx < 0 ? gx + gw : gx >= gw ? gx - gw : gx)];
                        if (k < 0 || sb[k] != c) continue;
                        double dx = sx[k] - jx;
                        if (dx > half) dx -= w; else if (dx < -half) dx += w;
                        double dy = sy[k] - jy, pe = (hi - sh[k]) * pw, d = dx * dx + dy * dy + pe * pe;
                        if (d < bestD) { bestD = d; best = k; }
                    }
                }
                if (best < 0) // no seed of this body nearby (tiny island, lake, narrow strait): scan the body's seeds
                    for (int t = sd.BodyOffset[c], e = sd.BodyOffset[c + 1]; t < e; t++)
                    {
                        int k = sd.BodyList[t];
                        double dx = sx[k] - jx;
                        if (dx > half) dx -= w; else if (dx < -half) dx += w;
                        double dy = sy[k] - jy, d = dx * dx + dy * dy;
                        if (d < bestD) { bestD = d; best = k; }
                    }
                prov[i] = best;
            }
        });
    }

    /// <summary>Every province becomes one connected piece of reasonable size, then ids are renumbered in row-major order
    /// of first appearance. The tie-breaks (first contact wins) are those of the mockup's JS Map iteration order.</summary>
    [MethodImpl(Hot)]
    void MergeProvinces(int nSeeds)
    {
        int w = _w, n = _n;
        var prov = _d.Prov; var land = _d.Land;

        // 1. a seed's area may fall apart (relief penalty, jitter): keep its largest piece; the other pieces join the
        //    neighbouring piece they touch most, main pieces first
        var pc = Components.Label(prov, w, _h, _po);
        int nc = pc.Count;
        var mainC = new int[nSeeds]; Array.Fill(mainC, -1);
        var mainS = new int[nSeeds];
        var isMain = new bool[nc];
        for (int k = 0; k < nc; k++)
        {
            int p = prov[pc.First[k]];
            if (pc.Size[k] > mainS[p]) { mainS[p] = pc.Size[k]; mainC[p] = k; }
        }
        int nMain = 0;
        for (int p = 0; p < nSeeds; p++) if (mainC[p] >= 0) { isMain[mainC[p]] = true; nMain++; }
        if (nc > nMain)
        {
            var contacts = new Contacts();
            for (int i = 0; i < n; i++)
            {
                int k = pc.Id[i];
                if (isMain[k]) continue;
                int x = i % w, row = i - x;
                for (int t = 0; t < 4; t++)
                {
                    int j = Neighbour(t, i, x, row);
                    if (j < 0 || land[j] != land[i]) continue;
                    int q = pc.Id[j];
                    if (q != k) contacts.Add(k, q, isMain[q] ? 1_000_000 : 1);
                }
            }
            var uf = new int[nc];
            for (int k = 0; k < nc; k++) uf[k] = k;
            for (int slot = 0; slot < contacts.Count; slot++)
            {
                int bq = contacts.Strongest(slot, _ => true);
                if (bq >= 0) uf[Find(uf, contacts.Key(slot))] = Find(uf, bq);
            }
            var rep = new int[nc]; Array.Fill(rep, -1);
            for (int k = 0; k < nc; k++) if (isMain[k]) rep[Find(uf, k)] = prov[pc.First[k]];
            int extra = nSeeds;
            var fragProv = new int[nc];
            for (int k = 0; k < nc; k++)
            {
                if (isMain[k]) continue;
                int r = Find(uf, k);
                if (rep[r] < 0) rep[r] = extra++;
                fragProv[k] = rep[r];
            }
            Parallel.For(0, _h, _po, [MethodImpl(Hot)] (y) =>
            {
                for (int i = y * w, e = i + w; i < e; i++) { int k = pc.Id[i]; if (!isMain[k]) prov[i] = fragProv[k]; }
            });
            nSeeds = extra;
        }

        // 2. tiny land provinces join the neighbour (not smaller than them) they touch most
        var cnt = new int[nSeeds];
        for (int i = 0; i < n; i++) cnt[prov[i]]++;
        var small = new Contacts();
        for (int i = 0; i < n; i++)
        {
            int p = prov[i];
            if (cnt[p] >= TinyProvince || land[i] == 0) continue;
            int x = i % w, row = i - x;
            for (int t = 0; t < 4; t++)
            {
                int j = Neighbour(t, i, x, row);
                if (j < 0 || land[j] != land[i]) continue;
                int q = prov[j];
                if (q != p) small.Add(p, q, 1);
            }
        }
        var u2 = new int[nSeeds];
        for (int p = 0; p < nSeeds; p++) u2[p] = p;
        for (int slot = 0; slot < small.Count; slot++)
        {
            int p = small.Key(slot), bq = small.Strongest(slot, q => cnt[q] >= cnt[p]);
            if (bq < 0) continue;
            int a = Find(u2, p), b = Find(u2, bq);
            if (a != b) u2[a] = b;
        }

        // 3. renumber in order of first appearance
        var nid = new int[nSeeds]; Array.Fill(nid, -1);
        var root = new int[nSeeds];
        for (int p = 0; p < nSeeds; p++) root[p] = Find(u2, p);
        int P = 0;
        for (int i = 0; i < n; i++)
        {
            int p = root[prov[i]], v = nid[p];
            if (v < 0) v = nid[p] = P++;
            prov[i] = v;
        }
        _d.P = P;
    }

    /// <summary>4-neighbours in the mockup's order: right, left (x wraps), up, down; -1 outside the world.</summary>
    int Neighbour(int t, int i, int x, int row) => t switch
    {
        0 => row + (x + 1 == _w ? 0 : x + 1),
        1 => row + (x == 0 ? _w - 1 : x - 1),
        2 => row > 0 ? i - _w : -1,
        _ => row < _n - _w ? i + _w : -1
    };

    static int Find(int[] uf, int k)
    {
        while (uf[k] != k) { uf[k] = uf[uf[k]]; k = uf[k]; }
        return k;
    }

    /// <summary>Insertion-ordered "key → neighbour → weight" table (the JS Map-of-Maps of the mockup, whose iteration
    /// order breaks the ties). Keys and neighbours are few, so neighbour lists are searched linearly.</summary>
    sealed class Contacts
    {
        readonly Dictionary<int, int> _slotOf = new();
        readonly List<int> _keys = new();
        readonly List<List<(int Q, long W)>> _lists = new();
        public int Count => _keys.Count;
        public int Key(int slot) => _keys[slot];

        public void Add(int key, int q, long weight)
        {
            if (!_slotOf.TryGetValue(key, out int slot))
            {
                slot = _keys.Count; _slotOf[key] = slot; _keys.Add(key); _lists.Add(new List<(int, long)>(4));
            }
            var l = _lists[slot];
            for (int t = 0; t < l.Count; t++)
                if (l[t].Q == q) { l[t] = (q, l[t].W + weight); return; }
            l.Add((q, weight));
        }

        /// <summary>Neighbour with the largest weight among those passing the filter; the first one wins ties; -1 if none.</summary>
        public int Strongest(int slot, Func<int, bool> allowed)
        {
            int best = -1; long bw = 0;
            foreach (var (q, wgt) in _lists[slot])
                if (wgt > bw && allowed(q)) { bw = wgt; best = q; }
            return best;
        }
    }

    /// <summary>Per-province statistics, adjacency, bodies, fertility and names.</summary>
    void ProvinceStats()
    {
        var d = _d;
        int w = _w, h = _h, P = d.P;
        var prov = d.Prov; var land = d.Land; var hgt = d.Height; var biome = d.Biome; var river = d.River;
        var off = d.PixOffset; var list = d.PixList;
        d.PSize = new int[P]; d.PLand = new byte[P]; d.PCX = new int[P]; d.PCY = new int[P]; d.PBiome = new byte[P];
        d.PH = new float[P]; d.PRiver = new byte[P]; d.PCoast = new byte[P]; d.PFert = new float[P]; d.PName = new string[P];
        d.PBody = new int[P];

        var cosX = new double[w]; var sinX = new double[w];
        for (int x = 0; x < w; x++) { double a = x / (double)w * 6.283185307; cosX[x] = Math.Cos(a); sinX[x] = Math.Sin(a); }

        // pixel lists are in row-major order, so these sums add up in exactly the mockup's order
        Parallel.For(0, P, _po, [MethodImpl(Hot)] (p) =>
        {
            int a = off[p], e = off[p + 1], size = e - a;
            double sc = 0, ss = 0, sumY = 0, sumH = 0;
            Span<int> hist = stackalloc int[Data.BiomeName.Length];
            bool riv = false;
            for (int t = a; t < e; t++)
            {
                int i = list[t], y = i / w, x = i - y * w;
                sc += cosX[x]; ss += sinX[x]; sumY += y; sumH += hgt[i];
                hist[biome[i]]++;
                riv |= river[i] != 0;
            }
            // circular mean in x (the world wraps); then the province pixel closest to it
            float cx = (float)((Math.Atan2(ss, sc) / 6.283185307 * w + w) % w), cy = (float)(sumY / size);
            float bestD = 1e9f; int bx = 0, by = 0;
            for (int t = a; t < e; t++)
            {
                int i = list[t], y = i / w, x = i - y * w;
                double dx = Math.Abs(x - (double)cx);
                if (dx > w / 2) dx = w - dx;
                double dy = y - (double)cy, dist = dx * dx + dy * dy;
                if (dist < bestD) { bestD = (float)dist; bx = x; by = y; }
            }
            int bb = 0, bn = 0;
            for (int b = 1; b < hist.Length; b++) if (hist[b] > bn) { bn = hist[b]; bb = b; }
            d.PSize[p] = size; d.PLand[p] = land[list[a]]; d.PRiver[p] = (byte)(riv ? 1 : 0);
            d.PCX[p] = bx; d.PCY[p] = by; d.PH[p] = (float)(sumH / size); d.PBiome[p] = (byte)bb;
            d.PBody[p] = _bodies.Id[by * w + bx];
        });

        BuildAdjacency();

        d.BodySize = _bodies.Size;
        d.BodyLand = new byte[_bodies.Count];
        int ocean = -1, oceanSize = 0;
        for (int c = 0; c < _bodies.Count; c++)
        {
            d.BodyLand[c] = land[_bodies.First[c]];
            if (d.BodyLand[c] == 0 && _bodies.Size[c] > oceanSize) { oceanSize = _bodies.Size[c]; ocean = c; }
        }
        d.Ocean = ocean;

        double lakeMax = 5000 * _k.Ks * _k.Ks;
        Parallel.For(0, P, _po, [MethodImpl(Hot)] (p) =>
        {
            string name = MakeName(p, _s);
            if (d.PLand[p] != 0)
            {
                d.PName[p] = name;
                double f = Fert[d.PBiome[p]];
                if (d.PH[p] > .34) f *= .6;
                if (d.PRiver[p] != 0) f = Math.Min(1, f + .2);
                d.PFert[p] = (float)f;
                return;
            }
            int c = d.PBody[p];
            bool coastal = false;
            foreach (int q in d.Adj[p]) if (d.PLand[q] != 0) { coastal = true; break; }
            d.PName[p] = c != ocean && d.BodySize[c] < lakeMax ? "Озеро " + name
                : !coastal && c == ocean ? "Океан " + name
                : d.PSize[p] < 1400 ? "Залив " + name
                : "Море " + name;
        });
        MakeNamesUnique(d);
    }

    /// <summary>
    /// One deliberate change from the mockup: its syllable names repeat (about 12% of land provinces share a name),
    /// which makes the chronicle and tooltips ambiguous. Later duplicates, in province order, get a fresh name from
    /// another hash salt; water keeps its «Море …» prefix. The mockup's originals go to WorldData.Renamed.
    /// </summary>
    void MakeNamesUnique(WorldData d)
    {
        var used = new HashSet<string>(d.P);
        for (int p = 0; p < d.P; p++)
        {
            string name = d.PName[p];
            if (used.Add(name)) continue;
            int space = d.PLand[p] != 0 ? -1 : name.IndexOf(' ');
            string prefix = space < 0 ? "" : name[..(space + 1)];
            string fresh;
            for (int salt = 1; !used.Add(fresh = prefix + MakeName(p, _s, salt)); salt++) { }
            d.Renamed.Add((p, name));
            d.PName[p] = fresh;
        }
    }

    // the mockup keeps fertility in doubles; Data.BiomeFert is float32, so recover the exact decimal values
    static readonly double[] Fert = Array.ConvertAll(Data.BiomeFert, f => Math.Round((double)f, 3));

    /// <summary>Symmetric adjacency (land + sea); list order = first contact in row-major order (right, then down), as in the mockup.</summary>
    [MethodImpl(Hot)]
    void BuildAdjacency()
    {
        var d = _d;
        int w = _w, h = _h, P = d.P;
        var prov = d.Prov;
        var rowPairs = new long[h][];
        Parallel.For(0, h, _po, () => new HashSet<long>(), [MethodImpl(Hot)] (y, _, seen) =>
        {
            seen.Clear();
            var pairs = new List<long>();
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x, a = prov[i];
                int r = prov[row + (x + 1 == w ? 0 : x + 1)];
                if (r != a) { long key = Pair(a, r); if (seen.Add(key)) pairs.Add(key); }
                if (y + 1 < h)
                {
                    int dn = prov[i + w];
                    if (dn != a) { long key = Pair(a, dn); if (seen.Add(key)) pairs.Add(key); }
                }
            }
            rowPairs[y] = pairs.ToArray();
            return seen;
        }, _ => { });

        var all = new HashSet<long>();
        var adj = new List<int>[P];
        for (int p = 0; p < P; p++) adj[p] = new List<int>(8);
        foreach (var pairs in rowPairs)
            foreach (long key in pairs)
            {
                if (!all.Add(key)) continue;
                int a = (int)(key >> 32), b = (int)(key & 0xffffffff);
                adj[a].Add(b); adj[b].Add(a);
                if (d.PLand[a] != d.PLand[b]) d.PCoast[d.PLand[a] != 0 ? a : b] = 1;
            }
        d.Adj = new int[P][];
        for (int p = 0; p < P; p++) d.Adj[p] = adj[p].ToArray();
    }

    static long Pair(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>Russian-sounding name from 2–3 syllables and a suffix (mockup mkName; salt 0 = the mockup's name).</summary>
    static string MakeName(int k, int seed, int salt = 0)
    {
        var syl = Data.Syl; var suf = Data.Suf;
        int c = 100 * salt;   // hash channels of this salt: 11 + c, 20..22 + c, 31 + c
        int n = 2 + (Noise.H2(k, 11 + c, seed) < .3 ? 1 : 0);
        var sb = new StringBuilder(16);
        for (int j = 0; j < n; j++) sb.Append(syl[(int)(Noise.H2(k, 20 + c + j, seed) * syl.Length)]);
        sb.Append(suf[(int)(Noise.H2(k, 31 + c, seed) * suf.Length)]);
        sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }
}
