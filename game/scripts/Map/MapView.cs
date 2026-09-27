using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// World map renderer. Layer stack (bottom → top):
///   World (camera-transformed, world px): MapSurface (map.gdshader: terrain, modes, borders, hover, fog) →
///   river casing → river water → trade-route casing → trade-route dashes;
///   screen-space overlays: sprites (cities, buildings) → labels → scouts.
/// GPU data is refreshed only when Game raises WorldReady / MapModeChanged / ProvincesChanged / FogChanged
/// (coalesced to once per frame). MapCamera drives the view through <see cref="SetView"/>.
/// </summary>
public partial class MapView : Node2D
{
    internal static MapView Current { get; private set; }

    internal MapViewport View;
    internal readonly FogField Fog = new();
    internal readonly MapTextures Tex = new();
    internal bool HasWorld { get; private set; }

    Node2D _world;
    MapSurface _surface;
    MeshLayer _riverCasing, _riverWater, _routeCasing, _routeDash;
    SpriteOverlay _sprites;
    NameOverlay _names;
    LabelOverlay _labels;
    ScoutOverlay _scouts;
    ShaderMaterial _mapMat, _riverCasingMat, _riverMat, _routeCasingMat, _routeMat;
    readonly List<ShaderMaterial> _mats = new();       // all map materials: they share the province/fog uniforms
    static readonly StringName UZoom = "zoom", UHovered = "hovered", USelected = "selected", UFogOn = "fog_on", UWater = "water_color";

    readonly ChangeSet _provChanges = new(), _fogChanges = new();
    bool _modeDirty;
    int _routesKey;
    int[] _stamp = System.Array.Empty<int>();
    int _stampGen;
    readonly List<int> _expanded = new();

    public override void _EnterTree() => Current = this;

    public override void _Ready()
    {
        _mapMat = NewMat("res://shaders/map.gdshader");
        _riverCasingMat = NewMat("res://shaders/river.gdshader"); _riverCasingMat.SetShaderParameter("casing", true);
        _riverMat = NewMat("res://shaders/river.gdshader");
        _routeCasingMat = NewMat("res://shaders/route.gdshader"); _routeCasingMat.SetShaderParameter("casing", true);
        _routeMat = NewMat("res://shaders/route.gdshader");

        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _surface = new MapSurface { Name = "Surface", Material = _mapMat };
        _world.AddChild(_surface);
        _riverCasing = new MeshLayer { Name = "RiverCasing", Material = _riverCasingMat };
        _riverWater = new MeshLayer { Name = "RiverWater", Material = _riverMat };
        _routeCasing = new MeshLayer { Name = "RouteCasing", Material = _routeCasingMat, Visible = false };
        _routeDash = new MeshLayer { Name = "RouteDash", Material = _routeMat, Visible = false };
        _world.AddChild(_riverCasing); _world.AddChild(_riverWater); _world.AddChild(_routeCasing); _world.AddChild(_routeDash);

        _sprites = new SpriteOverlay { Name = "Sprites", Map = this };
        _names = new NameOverlay { Name = "Names", Map = this };
        _labels = new LabelOverlay { Name = "Labels", Map = this };
        _scouts = new ScoutOverlay { Name = "Scouts", Map = this };
        AddChild(_sprites); AddChild(_names); AddChild(_labels); AddChild(_scouts);

        var g = Game.I;
        g.WorldReady += OnWorldReady;
        g.MapModeChanged += OnModeChanged;
        g.ProvincesChanged += OnProvincesChanged;
        g.FogChanged += OnFogChanged;
        g.ProvinceHovered += OnHovered;
        g.ProvinceSelected += OnSelected;
        g.TimeControlChanged += OnTimeControl;
        if (g.IsReady) OnWorldReady();
    }

    public override void _ExitTree()
    {
        var g = Game.I;
        if (g != null)
        {
            g.WorldReady -= OnWorldReady;
            g.MapModeChanged -= OnModeChanged;
            g.ProvincesChanged -= OnProvincesChanged;
            g.FogChanged -= OnFogChanged;
            g.ProvinceHovered -= OnHovered;
            g.ProvinceSelected -= OnSelected;
            g.TimeControlChanged -= OnTimeControl;
        }
        if (Current == this) Current = null;
    }

    ShaderMaterial NewMat(string path)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>(path) };
        _mats.Add(m);
        return m;
    }

    // ---------------- events ----------------

    void OnWorldReady()
    {
        var g = Game.I; var w = g.World; var s = g.State;
        if (w == null || s == null) { HasWorld = false; return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Fog.Init(w, s);
        long tFog = sw.ElapsedMilliseconds;
        Tex.Build(w, Fog);
        foreach (var m in _mats) Tex.Bind(m, w);
        _mapMat.SetShaderParameter("base_tex", Tex.Base);
        _mapMat.SetShaderParameter("ptint_tex", Tex.Tint);
        _mapMat.SetShaderParameter("pown_tex", Tex.Own);
        Tex.UpdateProvinces(w, s, g.Mode);
        SetFogUniform(s.FogEnabled);
        _surface.WorldSize = new Vector2(w.W, w.H);

        var rivers = RiverMesh.Build(w);
        _riverCasing.Mesh = rivers; _riverWater.Mesh = rivers;
        RebuildRoutes();
        ApplyMode(g.Mode);
        OnHovered(g.Hovered); OnSelected(g.Selected);
        HasWorld = true;
        _provChanges.Clear(); _fogChanges.Clear(); _modeDirty = false;
        _stamp = new int[w.P];
        _sprites.Refresh(); _names.Refresh(); _labels.Refresh(); _scouts.QueueRedraw();
        GD.Print($"map: fog field {tFog} ms, textures+meshes {sw.ElapsedMilliseconds - tFog} ms, river verts {(rivers == null ? 0 : rivers.SurfaceGetArrayLen(0))}");
    }

    void OnModeChanged(MapMode m) { if (!HasWorld) return; ApplyMode(m); _modeDirty = true; }
    void OnProvincesChanged(IReadOnlyList<int> ps) { if (HasWorld) _provChanges.Add(ps); }
    void OnFogChanged(IReadOnlyList<int> ps) { if (HasWorld) _fogChanges.Add(ps); }

    void OnHovered(int p) => _mapMat.SetShaderParameter(UHovered, p);
    void OnSelected(int p) => _mapMat.SetShaderParameter(USelected, p);
    void OnTimeControl(bool paused, int speed) => _scouts.QueueRedraw();

    void ApplyMode(MapMode m)
    {
        bool trade = m == MapMode.Trade;
        _routeCasing.Visible = _routeDash.Visible = trade;
        _riverMat.SetShaderParameter(UWater, m is MapMode.Terrain or MapMode.Political ? MapPalette.RiverWater : MapPalette.RiverWaterMuted);
    }

    void SetFogUniform(bool on) { foreach (var m in _mats) m.SetShaderParameter(UFogOn, on); }

    void RebuildRoutes()
    {
        _routesKey = RouteMesh.Key(Game.I.State);
        var mesh = RouteMesh.Build(Game.I.World, Game.I.State);
        _routeCasing.Mesh = mesh; _routeDash.Mesh = mesh;
    }

    public override void _Process(double delta)
    {
        MapDebug.Stats(delta);
        if (!Game.I.IsReady) { HasWorld = false; return; }   // «Новый мир» in progress: freeze until WorldReady
        if (!HasWorld) return;
        var w = Game.I.World; var s = Game.I.State;
        bool fog = _fogChanges.Any, prov = _provChanges.Any;
        if (!fog && !prov && !_modeDirty) return;
        if (fog)
        {
            SetFogUniform(s.FogEnabled);
            if (Fog.Update(s, _fogChanges.All ? null : _fogChanges.List)) Tex.UploadFog(w, Fog);
            // cities/names near the cloud edge depend on the distance field: include neighbours
            var ps = _fogChanges.All ? null : Expand(_fogChanges.List);
            _sprites.Refresh(ps); _names.Refresh(ps);
            _scouts.QueueRedraw();
        }
        if (prov)
        {
            var ps = _provChanges.All ? null : _provChanges.List;
            _sprites.Refresh(ps); _names.Refresh(ps);
            if (RouteMesh.Key(s) != _routesKey) RebuildRoutes();
        }
        Tex.UpdateProvinces(w, s, Game.I.Mode);
        if (fog || prov) _labels.Refresh();
        _fogChanges.Clear(); _provChanges.Clear(); _modeDirty = false;
    }

    /// <summary>Provinces plus their neighbours, without duplicates (reused buffer).</summary>
    List<int> Expand(List<int> ps)
    {
        var w = Game.I.World;
        _expanded.Clear();
        if (++_stampGen == int.MaxValue) { System.Array.Clear(_stamp); _stampGen = 1; }
        foreach (int p in ps)
        {
            if ((uint)p >= (uint)w.P) continue;
            if (_stamp[p] != _stampGen) { _stamp[p] = _stampGen; _expanded.Add(p); }
            foreach (int q in w.Adj[p]) if (_stamp[q] != _stampGen) { _stamp[q] = _stampGen; _expanded.Add(q); }
        }
        return _expanded;
    }

    // ---------------- view ----------------

    /// <summary>Called by MapCamera whenever zoom or position change (before overlays draw this frame).</summary>
    internal void SetView(in MapViewport v)
    {
        bool zoomChanged = v.Zoom != View.Zoom;
        View = v;
        _world.Transform = new Transform2D(new Vector2(v.Zoom, 0), new Vector2(0, v.Zoom), v.Origin);
        if (zoomChanged) foreach (var m in _mats) m.SetShaderParameter(UZoom, v.Zoom);
        _sprites.UpdateView(); _names.UpdateView(); _labels.QueueRedraw(); _scouts.QueueRedraw();
    }

    /// <summary>Province id under a world position (x is wrapped), or -1 outside the world.</summary>
    public int ProvinceAt(Vector2 world)
    {
        var w = Game.I.World; if (w == null) return -1;
        int x = Mathf.PosMod(Mathf.FloorToInt(world.X), w.W), y = Mathf.FloorToInt(world.Y);
        return y < 0 || y >= w.H ? -1 : w.Prov[y * w.W + x];
    }
}
