using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace PaxPixelia.World;

/// <summary>
/// Connected components of equal keys (4-neighbour, x wraps, y does not). Ids follow the row-major order of each
/// component's first pixel — exactly what the mockup's scan-order flood fill (labelBy) produces, so every tie-break
/// downstream stays the same. Works on horizontal runs with union-find instead of flooding pixel by pixel.
/// </summary>
public sealed class Components
{
    const MethodImplOptions Hot = MethodImplOptions.AggressiveOptimization;

    public int[] Id;     // component per pixel
    public int[] Size;   // pixels per component
    public int[] First;  // first pixel (row-major) per component
    public int Count;

    [MethodImpl(Hot)]
    public static Components Label<T>(T[] key, int w, int h, ParallelOptions po) where T : unmanaged, IEquatable<T>
    {
        // runs of equal keys: row y owns runs [rowRun[y], rowRun[y+1]), run r starts at x = runX[r]
        var rowRun = new int[h + 1];
        Parallel.For(0, h, po, [MethodImpl(Hot)] (y) =>
        {
            int row = y * w, n = 1;
            for (int x = 1; x < w; x++) if (!key[row + x].Equals(key[row + x - 1])) n++;
            rowRun[y + 1] = n;
        });
        for (int y = 0; y < h; y++) rowRun[y + 1] += rowRun[y];
        int nRuns = rowRun[h];
        var runX = new int[nRuns];
        Parallel.For(0, h, po, [MethodImpl(Hot)] (y) =>
        {
            int row = y * w, r = rowRun[y];
            runX[r++] = 0;
            for (int x = 1; x < w; x++) if (!key[row + x].Equals(key[row + x - 1])) runX[r++] = x;
        });

        var parent = new int[nRuns];
        for (int r = 0; r < nRuns; r++) parent[r] = r;
        for (int y = 0; y < h; y++)
        {
            int a = rowRun[y], a1 = rowRun[y + 1], row = y * w;
            if (a1 - a > 1 && key[row].Equals(key[row + w - 1])) Union(parent, a, a1 - 1); // x wrap
            if (y + 1 == h) continue;
            int b = a1, b1 = rowRun[y + 2], next = row + w;
            // sweep the two rows; the current runs a and b always overlap in x
            while (a < a1 && b < b1)
            {
                if (key[row + runX[a]].Equals(key[next + runX[b]])) Union(parent, a, b);
                int aEnd = a + 1 < a1 ? runX[a + 1] : w, bEnd = b + 1 < b1 ? runX[b + 1] : w;
                if (aEnd <= bEnd) a++;
                if (bEnd <= aEnd) b++;
            }
        }

        var label = new int[nRuns];
        Array.Fill(label, -1);
        var runLabel = new int[nRuns];
        var size = new int[nRuns];
        var first = new int[nRuns];
        int count = 0;
        for (int y = 0; y < h; y++)
            for (int r = rowRun[y], e = rowRun[y + 1]; r < e; r++)
            {
                int root = Find(parent, r), l = label[root];
                if (l < 0) { l = label[root] = count++; first[l] = y * w + runX[r]; }
                size[l] += (r + 1 < e ? runX[r + 1] : w) - runX[r];
                runLabel[r] = l;
            }

        var id = new int[w * h];
        Parallel.For(0, h, po, [MethodImpl(Hot)] (y) =>
        {
            int row = y * w;
            for (int r = rowRun[y], e = rowRun[y + 1]; r < e; r++)
            {
                int l = runLabel[r], x1 = r + 1 < e ? runX[r + 1] : w;
                for (int x = runX[r]; x < x1; x++) id[row + x] = l;
            }
        });
        Array.Resize(ref size, count);
        Array.Resize(ref first, count);
        return new Components { Id = id, Size = size, First = first, Count = count };
    }

    static int Find(int[] parent, int k)
    {
        while (parent[k] != k) { parent[k] = parent[parent[k]]; k = parent[k]; }
        return k;
    }

    static void Union(int[] parent, int a, int b)
    {
        a = Find(parent, a); b = Find(parent, b);
        if (a < b) parent[b] = a; else if (b < a) parent[a] = b;
    }
}
