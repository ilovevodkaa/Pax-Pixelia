using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// The map's small life, redrawn every frame (few items): capitals' flags waving over their castles (×2+), and trade
/// traffic on the routes (ART_BIBLE §11) — pack mules on land, cogs on the sea legs, at most 3 per route, only where
/// the player sees now. Trade mode shows it from ×2; Political only from ×4, one quiet caravan per route. Motion
/// follows game time (it stops on pause, standing on frame 0) while flags keep fluttering in real time.
/// </summary>
internal partial class LifeOverlay : MapOverlay
{
    const float TrafficSpeed = 6;              // world px per second of unpaused time (visual only, never Sim)

    readonly List<Vector2[]> _routes = new();  // smooth route polylines, world px
    readonly List<float[]> _cum = new();       // cumulative length at each point
    readonly List<(int from, int to)> _ends = new();
    readonly List<Vector2> _tmp = new();
    int _routesKey = int.MinValue;
    object _forWorld;
    double _clock;

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Process(double delta)
    {
        if (Map == null || !Map.HasWorld || !Game.I.IsReady) return;
        if (!Game.I.State.Paused) _clock += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Map == null || !Map.HasWorld || !Game.I.IsReady) return;
        var v = Map.View;
        if (v.Zoom <= 0) return;
        double t = Time.GetTicksMsec() / 1000.0;
        if (v.Level >= 2) DrawFlags(v, t);
        var mode = Game.I.Mode;
        if ((mode == MapMode.Trade && v.Level >= 2) || (mode == MapMode.Political && v.Level >= 4)) DrawTraffic(v, t, mode == MapMode.Trade ? 3 : 1);
    }

    void DrawFlags(in MapViewport v, double t)
    {
        var w = Game.I.World; var s = Game.I.State; var mem = Map.Memory;
        var plan = Map.Labels.Get(v.Level);
        int fs = Lod.FlagScale(v.Level);
        var fsize = (Vector2)MapAtlas.Size(MapAtlas.CapitalFlag(0)) * fs;
        foreach (int p in s.NationCapital)
        {
            if (p < 0 || mem.CapitalOf(p) < 0 || !plan.Sprite[p]) continue;
            int nation = mem.Colours(p), era = mem.Era(p);
            float sy = v.ScreenY(w.PCY[p] + .5f);
            if (sy < -120 || sy > v.Screen.Y + 120) continue;
            int id = MapAtlas.CapitalFlag((int)(t / .15) + p);
            for (float sx = v.FirstX(w.PCX[p] + .5f, 120); sx < v.Screen.X + 120; sx += v.WZ)
            {
                var city = Lod.CitySpriteRect(sx, sy, v.Level, true, era);
                var c = Lod.FlagOrigin(city, v.Level, era) + fsize / 2;
                MapAtlas.Draw(this, id, nation, c.X, c.Y, fs);
            }
        }
    }

    // ---------------------------------------------------------------- traffic

    void SyncRoutes()
    {
        var w = Game.I.World; var s = Game.I.State;
        int key = RouteMesh.Key(s);
        if (key == _routesKey && _forWorld == w) return;
        _routesKey = key; _forWorld = w;
        _routes.Clear(); _cum.Clear(); _ends.Clear();
        foreach (var path in s.Routes)
        {
            if (path == null || path.Length < 2) continue;
            RouteMesh.Polyline(w, path, _tmp);
            var pts = _tmp.ToArray();
            var cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + pts[i].DistanceTo(pts[i - 1]);
            _routes.Add(pts); _cum.Add(cum); _ends.Add((path[0], path[^1]));
        }
    }

    void DrawTraffic(in MapViewport v, double t, int perRoute)
    {
        SyncRoutes();
        var w = Game.I.World; var s = Game.I.State;
        int ps = Lod.UnitScale(v.Level);
        bool paused = s.Paused;
        for (int r = 0; r < _routes.Count; r++)
        {
            var pts = _routes[r]; var cum = _cum[r];
            float len = cum[^1];
            if (len < 8) continue;
            int n = Math.Clamp((int)(len / 70), 1, perRoute);
            for (int k = 0; k < n; k++)
            {
                bool back = (k & 1) == 1;
                float d = (float)((_clock * TrafficSpeed + len * (k + .37f * r) / n) % len);
                if (back) d = len - d;
                int i = Array.BinarySearch(cum, d);
                if (i < 0) i = Math.Max(1, ~i);
                i = Math.Clamp(i, 1, pts.Length - 1);
                float seg = cum[i] - cum[i - 1];
                var pos = pts[i - 1].Lerp(pts[i], seg > 0 ? (d - cum[i - 1]) / seg : 0);
                if (!Seen(pos)) continue;
                bool left = (pts[i].X - pts[i - 1].X) * (back ? -1 : 1) < 0;
                int wx = ((int)MathF.Floor(pos.X) % w.W + w.W) % w.W, wy = Math.Clamp((int)pos.Y, 0, w.H - 1);
                bool sea = w.Land[wy * w.W + wx] == 0;
                var kind = sea ? UnitKind.Ship : UnitKind.Caravan;
                int frame = paused ? 0 : (int)(t * 1000 / (sea ? 320 : 200)) + k;
                int id = MapAtlas.Unit(kind, frame, left);
                int cap = back ? _ends[r].to : _ends[r].from;
                int nation = Math.Max((int)s.CapitalOf[cap], 0);
                float h = MapAtlas.Size(id).Y * ps;
                float sy = v.ScreenY(pos.Y) - h * .35f;
                if (sy < -40 || sy > v.Screen.Y + 40) continue;
                for (float sx = v.FirstX(pos.X, 40); sx < v.Screen.X + 40; sx += v.WZ)
                    MapAtlas.Draw(this, id, nation, sx, sy, ps);
            }
        }
    }

    /// <summary>Visible to the player right now (not just explored) and clear of the frayed cloud edge.</summary>
    bool Seen(Vector2 pos)
    {
        var s = Game.I.State;
        if (!FogOn) return true;
        int p = Map.ProvinceAt(pos);
        return p >= 0 && s.Fog[p] == 2 && Map.Fog.IsClear(pos.X, pos.Y, 9);
    }
}
