using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.Map;

/// <summary>
/// Decides, per zoom level and data change, which map texts and city sprites are shown and where nation names go, so
/// nothing on the map overlaps — like HOI4, a name with no room is left out rather than drawn over something else.
/// Priority: capitals (sprite + name) → towns (a town gives way to a capital or a more important town) → nation names
/// (placed clear of every city, see <see cref="NationLabels"/>) → province names (biggest provinces first, from ×4).
/// Geometry is kept in "level px": world px × the level's zoom, x wrapping at W × zoom, so the chunked overlays
/// (recorded at that zoom) and the per-frame nation names agree exactly at rest. Recomputing a level costs ~1 ms.
/// </summary>
internal sealed class LabelPlan
{
    public const int CapSize = 14, TownSize = 12, ProvSize = 11;
    const float NamePad = 2;             // px kept free around a province name (4 px between two names)

    /// <summary>What one zoom level shows.</summary>
    public sealed class Tier
    {
        public int Level;
        public float Z;                  // zoom of the level at rest
        public int Version = -1;
        public bool[] Name = Array.Empty<bool>();     // city or province name drawn
        public bool[] Sprite = Array.Empty<bool>();   // city sprite drawn
        public readonly NationLabels.Place[] Nations = new NationLabels.Place[Data.Nations.Length];
    }

    readonly Tier[] _tiers = new Tier[9];
    readonly NationLabels _nations = new();
    readonly RectGrid _grid = new();
    readonly List<Rect2> _cities = new();          // shown city sprites and names (level px): obstacles for everyone else
    readonly List<int> _changed = new();
    bool[] _oldName = Array.Empty<bool>(), _oldSprite = Array.Empty<bool>();
    FogField _fog;
    WorldData _world;
    int _version;
    int _selected = -1;

    // per world
    float[] _wCap = Array.Empty<float>(), _wTown = Array.Empty<float>(), _wProv = Array.Empty<float>();
    int[] _bySize = Array.Empty<int>();            // land provinces, biggest first
    int _p50, _p75;                                // land province size percentiles

    public NationLabels Nations => _nations;

    /// <summary>A new world: caches are rebuilt, every level recomputed on demand.</summary>
    public void Reset(WorldData w, FogField fog)
    {
        _world = w; _fog = fog;
        _wCap = NewWidths(w.P); _wTown = NewWidths(w.P); _wProv = NewWidths(w.P);
        var land = new List<int>();
        for (int p = 0; p < w.P; p++) if (w.PLand[p] == 1) land.Add(p);
        land.Sort((a, b) => w.PSize[a] != w.PSize[b] ? w.PSize[b].CompareTo(w.PSize[a]) : a.CompareTo(b));
        _bySize = land.ToArray();
        _p75 = _bySize.Length == 0 ? 0 : w.PSize[_bySize[_bySize.Length / 4]];
        _p50 = _bySize.Length == 0 ? 0 : w.PSize[_bySize[_bySize.Length / 2]];
        _selected = -1;
        Invalidate();
    }

    /// <summary>Ownership, fog or cities changed: territories are re-measured and every level goes stale.</summary>
    public void Invalidate()
    {
        _version++;
        if (_world != null && Game.I.IsReady && Game.I.World == _world) _nations.Refresh();
    }

    /// <summary>The selected province is a soft obstacle for nation names (they avoid its outline when they can).</summary>
    public bool SetSelected(int p)
    {
        if (p == _selected) return false;
        _selected = p;
        _version++;
        return true;
    }

    /// <summary>The plan of a level, computed if stale.</summary>
    public Tier Get(int level)
    {
        level = Math.Clamp(level, 0, _tiers.Length - 1);
        var t = _tiers[level] ??= new Tier { Level = level, Z = MapViewport.ZoomOf(level) };
        if (t.Version != _version && _world != null && Game.I.World == _world) Compute(t, false);
        return t;
    }

    /// <summary>Recompute a level now and list the provinces whose name or sprite appeared or disappeared (for the
    /// chunked overlays to re-record). The list is reused.</summary>
    public List<int> Update(int level)
    {
        _changed.Clear();
        level = Math.Clamp(level, 0, _tiers.Length - 1);
        var t = _tiers[level];
        if (t == null || t.Version < 0 || _world == null || Game.I.World != _world) { Get(level); return _changed; }
        if (t.Version != _version) Compute(t, true);
        return _changed;
    }

    // ------------------------------------------------------------------------------------------------ compute

    void Compute(Tier t, bool diff)
    {
        var w = _world; var s = Game.I.State;
        int P = w.P, L = t.Level;
        float z = t.Z;
        if (t.Name.Length != P) { t.Name = new bool[P]; t.Sprite = new bool[P]; diff = false; }
        if (diff)
        {
            if (_oldName.Length != P) { _oldName = new bool[P]; _oldSprite = new bool[P]; }
            Array.Copy(t.Name, _oldName, P); Array.Copy(t.Sprite, _oldSprite, P);
        }
        Array.Clear(t.Name); Array.Clear(t.Sprite);
        t.Version = _version;
        _grid.Reset(w.W * z, w.H * z);
        _cities.Clear();

        PlaceCities(t, w, s);
        Rect2? sel = _selected >= 0 && _selected < P ? SelectionBox(w, _selected, z) : null;
        _nations.Layout(t, _cities, sel, Visible);
        PlaceProvinceNames(t, w, s);

        if (diff)
            for (int p = 0; p < P; p++)
                if (t.Name[p] != _oldName[p] || t.Sprite[p] != _oldSprite[p]) _changed.Add(p);
    }

    /// <summary>Capitals always; a town's sprite gives way to a capital, its name to any city sprite or earlier name.</summary>
    void PlaceCities(Tier t, WorldData w, Sim.GameState s)
    {
        int L = t.Level;
        if (L < 2) return;
        float z = t.Z;
        foreach (int cap in s.NationCapital)
        {
            if (cap < 0 || !Visible(cap)) continue;
            t.Sprite[cap] = true;
            Obstacle(SpriteRect(w, cap, true, L, z));
            if (L < 3) continue;
            t.Name[cap] = true;
            Obstacle(NameRect(w, cap, true, L, z));
        }
        if (L < 3) return;
        int capitals = _cities.Count;
        for (int p = 0; p < w.P; p++)
        {
            if (!s.IsTown[p] || s.CapitalOf[p] >= 0 || !Visible(p)) continue;
            var r = SpriteRect(w, p, false, L, z);
            if (HitsAny(r, 0, capitals)) continue;              // the capital wins
            t.Sprite[p] = true;
            Obstacle(r);
        }
        for (int p = 0; p < w.P; p++)
        {
            if (!t.Sprite[p] || s.CapitalOf[p] >= 0) continue;
            var r = NameRect(w, p, false, L, z);
            var own = SpriteRect(w, p, false, L, z);
            bool clear = true;
            foreach (var o in _cities) if (o != own && Hit(r, o)) { clear = false; break; }
            if (!clear) continue;
            t.Name[p] = true;
            Obstacle(r);
        }
    }

    /// <summary>From ×4: the biggest provinces first (×4 only the top quarter, ×5 the top half, all from ×6), each kept
    /// only where it hits no placed name, city or nation name.</summary>
    void PlaceProvinceNames(Tier t, WorldData w, Sim.GameState s)
    {
        int L = t.Level;
        if (L < 4) return;
        int min = L == 4 ? _p75 : L == 5 ? _p50 : 0;
        float z = t.Z;
        foreach (int p in _bySize)
        {
            if (w.PSize[p] < min) break;
            if (s.CapitalOf[p] >= 0 || s.IsTown[p] || !Visible(p)) continue;
            var r = ProvRect(w, p, z).Grow(NamePad);
            if (_grid.Hits(r) || _nations.Hits(t, r)) continue;
            t.Name[p] = true;
            _grid.Add(r);
        }
    }

    void Obstacle(Rect2 r) { _cities.Add(r); _grid.Add(r); }

    bool HitsAny(Rect2 r, int from, int to)
    {
        for (int i = from; i < to; i++) if (Hit(r, _cities[i])) return true;
        return false;
    }

    bool Hit(Rect2 a, Rect2 b) => RectGrid.WrapIntersects(a, b, _grid.WrapWidth);

    /// <summary>Self-test audit of a level: shown names (city and province) that overlap each other or a city sprite,
    /// and nation names that hit any of those. 0 when the plan did its job.</summary>
    internal int CountOverlaps(int level)
    {
        var t = Get(level);
        var w = _world; var s = Game.I.State;
        float z = t.Z, wrap = w.W * z;
        var names = new List<Rect2>(); var sprites = new List<(Rect2 r, int p)>();
        for (int p = 0; p < w.P; p++)
        {
            bool cap = s.CapitalOf[p] >= 0;
            if (t.Sprite[p]) sprites.Add((SpriteRect(w, p, cap, level, z), p));
            if (!t.Name[p]) continue;
            names.Add(cap || s.IsTown[p] ? NameRect(w, p, cap, level, z) : ProvRect(w, p, z));
        }
        int bad = 0;
        for (int i = 0; i < names.Count; i++)
        {
            var a = names[i].Grow(-1);   // touching edges are fine
            for (int j = i + 1; j < names.Count; j++) if (RectGrid.WrapIntersects(a, names[j].Grow(-1), wrap)) bad++;
            foreach (var (r, _) in sprites) if (RectGrid.WrapIntersects(a, r.Grow(-1), wrap)) bad++;
            if (_nations.Hits(t, a)) bad++;
        }
        foreach (var (r, _) in sprites) if (_nations.Hits(t, r.Grow(-1))) bad++;
        return bad;
    }

    // ------------------------------------------------------------------------------------------------ geometry

    /// <summary>Visible to the local player and clear of the frayed cloud edge.</summary>
    public bool Visible(int p)
    {
        var s = Game.I.State;
        if (!s.FogEnabled) return true;
        return s.Fog[p] > 0 && (_fog == null || _fog.IsClear(_world.PCX[p], _world.PCY[p], 9));
    }

    public static float NameOffset(bool cap, int ps) => PixelSprites.Size(cap ? Spr.Capital : Spr.Town).Y * ps / 2f + 9;

    public static Rect2 SpriteRect(WorldData w, int p, bool cap, int level, float z)
    {
        var size = (Vector2)PixelSprites.Size(cap ? Spr.Capital : Spr.Town) * PixelSprites.CityScale(level, cap);
        return new Rect2(new Vector2((w.PCX[p] + .5f) * z, (w.PCY[p] + .5f) * z) - size / 2, size);
    }

    public Rect2 NameRect(WorldData w, int p, bool cap, int level, float z)
    {
        float tw = CityNameWidth(p, cap), h = cap ? CapSize : TownSize;
        float cy = (w.PCY[p] + .5f) * z + NameOffset(cap, PixelSprites.CityScale(level, cap));
        return new Rect2((w.PCX[p] + .5f) * z - tw / 2 - 2, cy - h / 2 - 1, tw + 4, h + 2);
    }

    Rect2 ProvRect(WorldData w, int p, float z)
    {
        float tw = ProvNameWidth(p);
        return new Rect2(w.PCX[p] * z - tw / 2, w.PCY[p] * z - z * 3 - ProvSize / 2f - 1, tw, ProvSize + 2);
    }

    public float CityNameWidth(int p, bool cap) =>
        Width(cap ? _wCap : _wTown, p, cap ? MapFonts.Display700 : MapFonts.Ui500, cap ? CapSize : TownSize);

    public float ProvNameWidth(int p) => Width(_wProv, p, MapFonts.Ui400, ProvSize);

    float Width(float[] cache, int p, Font f, int size)
    {
        if (cache[p] < 0) cache[p] = f.GetStringSize(_world.PName[p], HorizontalAlignment.Left, -1, size).X;
        return cache[p];
    }

    /// <summary>Bounding box of province p in level px (x measured from its anchor, so it may cross the seam).</summary>
    static Rect2 SelectionBox(WorldData w, int p, float z)
    {
        int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, half = w.W / 2;
        for (int k = w.PixOffset[p]; k < w.PixOffset[p + 1]; k++)
        {
            int i = w.PixList[k], x = i % w.W - w.PCX[p], y = i / w.W;
            if (x > half) x -= w.W; else if (x < -half) x += w.W;
            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
        }
        if (x1 < x0) return new Rect2(w.PCX[p] * z, w.PCY[p] * z, 0, 0);
        return new Rect2((w.PCX[p] + x0) * z - 1, y0 * z - 1, (x1 - x0 + 1) * z + 2, (y1 - y0 + 1) * z + 2);
    }

    static float[] NewWidths(int n) { var a = new float[n]; Array.Fill(a, -1f); return a; }
}

/// <summary>Rects in level px on a coarse grid (x wraps), for quick "does this label hit anything placed?" tests.</summary>
internal sealed class RectGrid
{
    const float Cell = 64;
    int _gw, _gh;
    int[] _head = Array.Empty<int>();
    readonly List<Rect2> _rects = new();
    readonly List<int> _nodeRect = new(), _nodeNext = new();
    public float WrapWidth { get; private set; }

    public void Reset(float wrapWidth, float height)
    {
        WrapWidth = wrapWidth;
        _gw = Math.Max(1, (int)MathF.Ceiling(wrapWidth / Cell));
        _gh = Math.Max(1, (int)MathF.Ceiling(height / Cell) + 2);
        if (_head.Length < _gw * _gh) _head = new int[_gw * _gh];
        Array.Fill(_head, -1, 0, _gw * _gh);
        _rects.Clear(); _nodeRect.Clear(); _nodeNext.Clear();
    }

    public void Add(Rect2 r)
    {
        int id = _rects.Count;
        _rects.Add(r);
        Cells(r, out int cx0, out int cx1, out int cy0, out int cy1);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int c = cy * _gw + ((cx % _gw) + _gw) % _gw;
                _nodeRect.Add(id); _nodeNext.Add(_head[c]); _head[c] = _nodeRect.Count - 1;
            }
    }

    public bool Hits(Rect2 r)
    {
        Cells(r, out int cx0, out int cx1, out int cy0, out int cy1);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                for (int n = _head[cy * _gw + ((cx % _gw) + _gw) % _gw]; n >= 0; n = _nodeNext[n])
                    if (WrapIntersects(r, _rects[_nodeRect[n]], WrapWidth)) return true;
        return false;
    }

    void Cells(Rect2 r, out int cx0, out int cx1, out int cy0, out int cy1)
    {
        cx0 = (int)MathF.Floor(r.Position.X / Cell); cx1 = (int)MathF.Floor(r.End.X / Cell);
        if (cx1 - cx0 >= _gw) { cx0 = 0; cx1 = _gw - 1; }
        cy0 = Math.Clamp((int)MathF.Floor(r.Position.Y / Cell) + 1, 0, _gh - 1);
        cy1 = Math.Clamp((int)MathF.Floor(r.End.Y / Cell) + 1, 0, _gh - 1);
    }

    /// <summary>Rect overlap on the cylinder: b is tried at its own x and one wrap width either side.</summary>
    public static bool WrapIntersects(Rect2 a, Rect2 b, float wrap)
    {
        if (a.Position.Y >= b.End.Y || b.Position.Y >= a.End.Y) return false;
        float dx = b.Position.X - a.Position.X;
        if (wrap > 0) dx -= MathF.Round((dx + (b.Size.X - a.Size.X) / 2) / wrap) * wrap;
        return dx < a.Size.X && -dx < b.Size.X;
    }
}
