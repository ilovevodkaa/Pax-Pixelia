using System;
using System.Collections.Generic;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>
/// Preallocated work buffers for the per-step graph passes (vision BFS, land BFS, pocket fill),
/// so scouts walking in real time do not allocate province-sized arrays on every step.
/// </summary>
internal sealed class SimScratch
{
    public readonly int P;
    public readonly sbyte[] Rem;          // vision: remaining range per province, -1 = not reached
    public readonly List<int>[] Buckets;  // vision: provinces waiting at each remaining range
    public readonly List<(int p, int r)> Sources = new(512); // vision: (province, range) seeds
    public readonly Bfs A, B;             // two independent land BFS (auto-target needs both at once)
    public readonly int[] Mark;           // generation-stamped visit marks of the pocket fill
    public readonly int[] Seen;           // generation-stamped marks for de-duplicating change lists
    public readonly int[] Stack;
    int _stamp, _seenStamp;

    public SimScratch(int p)
    {
        P = p;
        Rem = new sbyte[p];
        Buckets = new List<int>[FogOfWar.MaxRange + 1];
        for (int r = 0; r < Buckets.Length; r++) Buckets[r] = new List<int>(256);
        A = new Bfs(p); B = new Bfs(p);
        Mark = new int[p]; Seen = new int[p]; Stack = new int[p];
    }

    /// <summary>A fresh value for Mark: provinces with Mark[p] == stamp count as visited.</summary>
    public int NextStamp()
    {
        if (++_stamp == int.MaxValue) { Array.Clear(Mark); _stamp = 1; }
        return _stamp;
    }

    /// <summary>A fresh value for Seen.</summary>
    public int NextSeenStamp()
    {
        if (++_seenStamp == int.MaxValue) { Array.Clear(Seen); _seenStamp = 1; }
        return _seenStamp;
    }

    public static SimScratch For(WorldData w, GameState s)
    {
        if (s.Scratch == null || s.Scratch.P != w.P) s.Scratch = new SimScratch(w.P);
        return s.Scratch;
    }

    /// <summary>Breadth-first search over land provinces with parent links.</summary>
    public sealed class Bfs
    {
        public readonly int[] Dist, Prev, Queue;
        public int Count;                     // provinces reached, in BFS order: Queue[0..Count)

        public Bfs(int p) { Dist = new int[p]; Prev = new int[p]; Queue = new int[p]; }

        public void RunLand(WorldData w, ReadOnlySpan<int> sources)
        {
            Array.Fill(Dist, -1); Array.Fill(Prev, -1); Count = 0;
            foreach (int s in sources)
                if (s >= 0 && Dist[s] < 0) { Dist[s] = 0; Queue[Count++] = s; }
            for (int h = 0; h < Count; h++)
            {
                int p = Queue[h];
                foreach (int n in w.Adj[p])
                    if (Dist[n] < 0 && w.PLand[n] == 1) { Dist[n] = Dist[p] + 1; Prev[n] = p; Queue[Count++] = n; }
            }
        }

        /// <summary>Path from the BFS source to t (inclusive), or null when t was not reached.</summary>
        public int[] Trace(int t)
        {
            if (t < 0 || Dist[t] < 0) return null;
            var path = new int[Dist[t] + 1];
            for (int p = t, k = path.Length - 1; p >= 0; p = Prev[p]) path[k--] = p;
            return path;
        }
    }
}
