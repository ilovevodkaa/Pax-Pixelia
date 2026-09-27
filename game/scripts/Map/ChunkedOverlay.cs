using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Retained screen-space overlay for per-province content (city sprites, buildings, names). The world is cut into
/// 256×256 px chunks, one canvas item each, recorded in "zoomed" pixels relative to the chunk corner:
///  • panning only moves chunk nodes (Godot culls off-screen ones) — nothing is re-recorded;
///  • during a zoom glide chunks are scaled, so their content glides with the map;
///  • after a zoom change, chunks near the screen are re-recorded crisp over the next frames, nearest first,
///    within a per-frame budget (no single-frame hitch); far chunks wait until they scroll in;
///  • data changes re-record only the chunks holding the changed provinces.
/// </summary>
internal abstract partial class ChunkedOverlay : Node2D
{
    public MapView Map;

    const int CS = 256;              // chunk size, world px
    const float Reach = 160;         // screen px content may extend beyond its chunk (labels, sprites)
    const int MemberBudget = 140;    // provinces re-recorded per frame after a zoom change

    OverlayChunk[] _chunks = Array.Empty<OverlayChunk>();
    int _nx, _ny;
    object _forWorld;
    float _zoom = -1;                // zoom (at rest) chunks should be recorded at
    int _level;
    protected List<int>[] Members = Array.Empty<List<int>>();
    readonly List<OverlayChunk> _stale = new();
    Vector2 _centre;
    float _half;
    Comparison<OverlayChunk> _byDistance;

    /// <summary>Should province p be placed in this overlay at all (e.g. has a city)?</summary>
    protected abstract bool Wants(int p);

    /// <summary>Draw the chunk's members at its recorded zoom/level; local position of a world point is <see cref="Local"/>.</summary>
    protected internal abstract void DrawChunk(OverlayChunk c);

    protected static Vector2 Local(OverlayChunk c, float wx, float wy) => new((wx - c.X0) * c.RecZoom, (wy - c.Y0) * c.RecZoom);

    /// <summary>
    /// Update membership and mark chunks for re-recording: changed = null → everything,
    /// otherwise only the chunks holding those provinces.
    /// </summary>
    public virtual void Refresh(IReadOnlyList<int> changed = null)
    {
        var g = Game.I;
        if (!g.IsReady) return;
        var w = g.World;
        if (_forWorld != w) { Rebuild(w); changed = null; }
        if (changed == null)
        {
            foreach (var m in Members) m.Clear();
            for (int p = 0; p < w.P; p++) if (Wants(p)) Members[ChunkOf(w, p)].Add(p);
            foreach (var c in _chunks) c.Dirty = true;
        }
        else
            foreach (int p in changed)
            {
                if ((uint)p >= (uint)w.P) continue;
                int ci = ChunkOf(w, p);
                var m = Members[ci];
                bool want = Wants(p), has = m.Contains(p);
                if (want && !has) m.Add(p); else if (!want && has) m.Remove(p);
                _chunks[ci].Dirty = true;
            }
        UpdateView();
    }

    int ChunkOf(World.WorldData w, int p) => Math.Min(_ny - 1, w.PCY[p] / CS) * _nx + Math.Min(_nx - 1, w.PCX[p] / CS);

    void Rebuild(World.WorldData w)
    {
        foreach (var c in _chunks) c.QueueFree();
        _forWorld = w;
        _nx = (w.W + CS - 1) / CS; _ny = (w.H + CS - 1) / CS;
        _chunks = new OverlayChunk[_nx * _ny];
        Members = new List<int>[_nx * _ny];
        for (int i = 0; i < _chunks.Length; i++)
        {
            Members[i] = new List<int>();
            _chunks[i] = new OverlayChunk { Layer = this, Index = i, X0 = i % _nx * CS, Y0 = i / _nx * CS, Name = "C" + i };
            AddChild(_chunks[i]);
        }
        _zoom = -1;
    }

    /// <summary>Place chunks for the current view and record what must be recorded now.</summary>
    public void UpdateView()
    {
        if (Map == null || !Map.HasWorld || _chunks.Length == 0) return;
        var v = Map.View;
        if (v.Zoom <= 0) return;             // camera has not placed the view yet
        if (v.AtRest || _zoom <= 0) { _zoom = v.Zoom; _level = v.Level; }
        float cs = CS * v.Zoom;
        _stale.Clear();
        foreach (var c in _chunks)
        {
            float sx = v.FirstX(c.X0, cs + Reach), sy = v.ScreenY(c.Y0);
            c.Position = new Vector2(sx, sy);
            if (c.RecZoom > 0) c.Scale = new Vector2(v.Zoom / c.RecZoom, v.Zoom / c.RecZoom);
            bool near = sx < v.Screen.X + Reach && sy < v.Screen.Y + Reach && sy + cs > -Reach;
            if (!near) continue;
            if (c.Dirty || c.RecZoom <= 0) Record(c, v.Zoom);          // new data or never drawn: now
            else if (c.RecZoom != _zoom || c.RecLevel != _level) _stale.Add(c);
        }
        if (_stale.Count > 0) RecordStale(v);
    }

    public override void _Process(double delta)
    {
        if (_stale.Count > 0) UpdateView();
    }

    /// <summary>Zoom-stale chunks keep their scaled old recording; re-record them nearest-first within the budget.</summary>
    void RecordStale(in MapViewport v)
    {
        _centre = v.Screen / 2;
        _half = CS * v.Zoom / 2;
        _stale.Sort(_byDistance ??= CloserToCentre);
        int spent = 0;
        foreach (var c in _stale)
        {
            if (spent > 0 && spent + Members[c.Index].Count > MemberBudget) break;
            spent += Members[c.Index].Count;
            Record(c, v.Zoom);
        }
    }

    int CloserToCentre(OverlayChunk a, OverlayChunk b)
    {
        var h = new Vector2(_half, _half);
        return (a.Position + h).DistanceSquaredTo(_centre).CompareTo((b.Position + h).DistanceSquaredTo(_centre));
    }

    void Record(OverlayChunk c, float viewZoom)
    {
        c.Dirty = false;
        c.RecZoom = _zoom; c.RecLevel = _level;
        c.Scale = new Vector2(viewZoom / _zoom, viewZoom / _zoom);
        c.QueueRedraw();
    }

    // ---------- shared drawing helpers ----------

    protected static bool FogOn => Game.I.State.FogEnabled;
    protected static bool Known(int p) => !FogOn || Game.I.State.Fog[p] > 0;
    protected bool KnownAt(int p) { var w = Game.I.World; return Known(p) && (!FogOn || Map.Fog.IsClear(w.PCX[p], w.PCY[p], 9)); }
}
