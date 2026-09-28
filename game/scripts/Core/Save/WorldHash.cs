using System;
using System.Runtime.InteropServices;
using PaxPixelia.World;

namespace PaxPixelia.Core.Save;

/// <summary>
/// Fingerprint of a generated world, stored in every save: a load regenerates the world from its seed and refuses it
/// when this differs (a changed generator would put the saved provinces on other land). Covers what the rules and
/// the save depend on — the province map, the per-province facts, adjacency and names — not the render-only pixels
/// (height, colour, river lines), so a palette tweak does not orphan saves. ≈10 ms for a 2560×1440 world.
/// </summary>
public static class WorldHash
{
    public static ulong Of(WorldData w)
    {
        var h = new Hasher();
        h.Add(w.Seed); h.Add(w.W); h.Add(w.H); h.Add(w.P);
        h.Add(MemoryMarshal.AsBytes(w.Prov.AsSpan()));
        h.Add(MemoryMarshal.AsBytes(w.PSize.AsSpan()));
        h.Add(w.PLand);
        h.Add(MemoryMarshal.AsBytes(w.PCX.AsSpan()));
        h.Add(MemoryMarshal.AsBytes(w.PCY.AsSpan()));
        h.Add(w.PBiome);
        h.Add(MemoryMarshal.AsBytes(w.PH.AsSpan()));
        h.Add(w.PRiver);
        h.Add(w.PCoast);
        h.Add(MemoryMarshal.AsBytes(w.PFert.AsSpan()));
        foreach (var a in w.Adj) { h.Add(a.Length); h.Add(MemoryMarshal.AsBytes(a.AsSpan())); }
        foreach (var n in w.PName) h.Add(MemoryMarshal.AsBytes((n ?? "").AsSpan()));
        return h.Value;
    }

    /// <summary>FNV-1a over 64-bit words (fast on multi-megabyte arrays; the tail goes byte by byte).</summary>
    struct Hasher
    {
        ulong _h;
        public Hasher() { _h = 14695981039346656037UL; }
        public void Add(long v) => _h = (_h ^ (ulong)v) * 1099511628211UL;

        public void Add(ReadOnlySpan<byte> data)
        {
            Add(data.Length);
            var words = MemoryMarshal.Cast<byte, ulong>(data);
            foreach (ulong x in words) _h = (_h ^ x) * 1099511628211UL;
            for (int i = words.Length * 8; i < data.Length; i++) _h = (_h ^ data[i]) * 1099511628211UL;
        }

        public readonly ulong Value => _h;
    }
}
