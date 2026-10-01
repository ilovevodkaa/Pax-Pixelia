using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Map;

/// <summary>
/// Scouts: the concept's torch-bearer (4-frame walk facing its way, 180 ms a frame; on pause it stands on frame 0 while
/// the torch keeps flickering) with a dark rim so it stands in front of map names, the rest of its route as a marching
/// dotted line and a flag where the route ends. Everything is hidden over unexplored land and the frayed cloud edge.
/// Tribes of the nomad phase walk the same way (the nomads' sprite in their nation's colour); only the player's own
/// tribe shows its route. The player's own units are hit targets (<see cref="UnitAt"/>): the picked one wears gold corner
/// brackets, and while it is picked the province under the cursor shows the way it would take on a right click (gold
/// dashes and a flag) or a red cross where it cannot go.
/// </summary>
internal partial class ScoutOverlay : MapOverlay
{
    readonly List<Vector2> _pts = new();       // remaining route, world px (x unwrapped from the scout)
    readonly List<Vector2> _samples = new();   // route resampled every 2 world px; NaN marks a hidden gap
    Vector2[] _dash = new Vector2[256];
    int _dashN;
    static readonly Color Rim = new(22 / 255f, 26 / 255f, 31 / 255f, .85f);
    static readonly Color Pick = new(1f, .84f, .32f), PickBad = new(.94f, .32f, .26f), PickShade = new(22 / 255f, 26 / 255f, 31 / 255f, .8f);
    const float MinHit = 22;   // screen px: a unit stays clickable when zoomed out
    readonly List<(Rect2 Rect, UnitRef Unit)> _hits = new();   // this frame's figures, in draw order

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Process(double delta)
    {
        if (Game.I.IsReady && (Game.I.State.Scouts.Count > 0 || Game.I.State.AnyNomads || Game.I.SelectedUnit.Any)) QueueRedraw();
    }

    /// <summary>The player's unit drawn at a screen point (the topmost), or none.</summary>
    internal UnitRef UnitAt(Vector2 screen)
    {
        for (int i = _hits.Count - 1; i >= 0; i--) if (_hits[i].Rect.HasPoint(screen)) return _hits[i].Unit;
        return default;
    }

    public override void _Draw()
    {
        _hits.Clear();
        if (Map == null || !Map.HasWorld || !Game.I.IsReady) return;
        var s = Game.I.State;
        if (s.Scouts.Count == 0 && !s.AnyNomads) return;
        var picked = Game.I.SelectedUnit;
        int viewer = Game.I.Viewer;
        var v = Map.View; var w = Game.I.World;
        float z = v.Zoom;
        int pz = Lod.UnitScale(Math.Max(v.Level, 2));
        double t = Time.GetTicksMsec();
        int frame = (int)(t / 180);
        float lw = Math.Max(2, MathF.Round(v.Level * .5f));
        float dashOffset = (float)(t / 60 % (lw * 3));

        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            if (nat.Camp < 0) continue;
            bool mine = n == viewer, walking = nat.CampPath != null;
            // the tribe glides between ticks like the scouts (one tick is 1/24 of a hop)
            float lead = walking && nat.CampStep + 1 < nat.CampPath.Length ? Game.I.TickLead : 0;
            if (walking) BuildRoute(nat.CampPath, nat.CampStep, Math.Min(.999f, (nat.CampSub + lead) / Nomads.StepTicks), w);
            else { _pts.Clear(); _pts.Add(new Vector2(w.PCX[nat.Camp] + .5f, w.PCY[nat.Camp] + .5f)); }
            var pos = _pts[0];
            float sy0 = v.ScreenY(pos.Y);
            if (sy0 < -400 || sy0 > v.Screen.Y + 400) continue;
            if (mine) Resample();
            bool visible = !FogOn || Map.Fog.IsClear(pos.X, pos.Y, 18);
            int tpz = pz + (mine ? 1 : 0);
            for (float sx0 = v.FirstX(pos.X, 400); sx0 < v.Screen.X + 400; sx0 += v.WZ)
            {
                var off = new Vector2(sx0 - pos.X * z, v.Origin.Y);
                if (mine && _pts.Count > 1)
                {
                    BuildDashes(off, z, lw, dashOffset);
                    if (_dashN > 1)
                    {
                        var span = new ReadOnlySpan<Vector2>(_dash, 0, _dashN);
                        DrawMultiline(span, new Color(22 / 255f, 26 / 255f, 31 / 255f, .72f), lw + 2);
                        DrawMultiline(span, Colors.White, lw);
                    }
                    var end = _pts[^1];
                    DrawSprite(MapAtlas.TargetFlag, n, end.X * z + off.X + pz * 2, end.Y * z + off.Y - pz * 3, pz);
                }
                if (!visible) continue;
                bool left = _pts.Count > 1 && _pts[1].X < _pts[0].X;
                int figure = MapAtlas.Unit(UnitKind.Nomads, walking && !s.Paused ? frame : 0, left);
                MapAtlas.DrawRim(this, figure, sx0, sy0 - tpz * 4, tpz, Rim);
                DrawSprite(figure, n, sx0, sy0 - tpz * 4, tpz);
                if (!mine) continue;
                var me = new UnitRef(UnitSel.Tribe, n);
                var box = MapAtlas.Dest(figure, sx0, sy0 - tpz * 4, tpz);
                _hits.Add((HitBox(box), me));
                if (picked == me) DrawBrackets(box, tpz);
            }
        }

        foreach (var sc in s.Scouts)
        {
            if (sc.Path == null || sc.Path.Length == 0) continue;
            BuildRoute(sc.Path, sc.Step, sc.Progress, w);
            var pos = _pts[0];
            float sy0 = v.ScreenY(pos.Y);
            if (sy0 < -400 || sy0 > v.Screen.Y + 400) continue;
            Resample();
            bool visible = !FogOn || Map.Fog.IsClear(pos.X, pos.Y, 18);
            for (float sx0 = v.FirstX(pos.X, 400); sx0 < v.Screen.X + 400; sx0 += v.WZ)
            {
                var off = new Vector2(sx0 - pos.X * z, v.Origin.Y);
                if (_pts.Count > 1)
                {
                    BuildDashes(off, z, lw, dashOffset);
                    if (_dashN > 1)
                    {
                        var span = new ReadOnlySpan<Vector2>(_dash, 0, _dashN);
                        DrawMultiline(span, new Color(22 / 255f, 26 / 255f, 31 / 255f, .72f), lw + 2);
                        DrawMultiline(span, Colors.White, lw);
                    }
                    var end = _pts[^1];
                    if (!FogOn || Map.Fog.IsClear(end.X, end.Y, 18))
                        DrawSprite(MapAtlas.TargetFlag, GameState.LocalPlayer, end.X * z + off.X + pz * 2, end.Y * z + off.Y - pz * 3, pz);
                }
                if (!visible) continue;
                bool left = _pts.Count > 1 && _pts[1].X < _pts[0].X;
                int figure = s.Paused ? MapAtlas.ScoutIdle(frame, left) : MapAtlas.Unit(UnitKind.Scout, frame, left);
                MapAtlas.DrawRim(this, figure, sx0, sy0 - pz * 4, pz, Rim);
                DrawSprite(figure, GameState.LocalPlayer, sx0, sy0 - pz * 4, pz);
                if (sc.Nation != viewer) continue;
                var me = new UnitRef(UnitSel.Scout, sc.Id);
                var box = MapAtlas.Dest(figure, sx0, sy0 - pz * 4, pz);
                _hits.Add((HitBox(box), me));
                if (picked == me) DrawBrackets(box, pz);
            }
        }

        if (picked.Any) DrawOrderPreview(w, v, z, pz, lw, dashOffset);
    }

    static Rect2 HitBox(Rect2 r) => r.Grow(Math.Max(2, (MinHit - Math.Min(r.Size.X, r.Size.Y)) / 2));

    static readonly (int X, int Y)[] Corners = { (0, 0), (1, 0), (0, 1), (1, 1) };

    /// <summary>Gold corner brackets round the picked unit (a dark line under them so they read on snow and sand).</summary>
    void DrawBrackets(Rect2 r, int ps)
    {
        r = r.Grow(ps + 2);
        float a = Math.Max(4, ps * 2), t = Math.Max(2, ps / 2);
        for (int pass = 0; pass < 2; pass++)
        {
            var c = pass == 0 ? PickShade : Pick;
            var q = pass == 0 ? r.Grow(1) : r;
            float th = pass == 0 ? t + 2 : t, len = pass == 0 ? a + 2 : a;
            foreach (var (cx, cy) in Corners)
            {
                float x = cx == 0 ? q.Position.X : q.End.X - len, y = cy == 0 ? q.Position.Y : q.End.Y - th;
                DrawRect(new Rect2(x, y, len, th), c);
                x = cx == 0 ? q.Position.X : q.End.X - th; y = cy == 0 ? q.Position.Y : q.End.Y - len;
                DrawRect(new Rect2(x, y, th, len), c);
            }
        }
    }

    /// <summary>Where the picked unit would go on a right click at the hovered province: gold dashes and a flag, or a red
    /// cross where it cannot go. The route hides under the clouds like every other route.</summary>
    void DrawOrderPreview(World.WorldData w, MapViewport v, float z, int pz, float lw, float dashOffset)
    {
        int p = Game.I.Hovered;
        if (p < 0 || Game.I.IsTargeting) return;
        var path = Game.I.PreviewOrder(p, out bool ok);
        var sc = Game.I.SelectedScout;
        if (path != null && path.Length > 1)
        {
            bool hop = sc != null && sc.Sub > 0;
            BuildRoute(path, 0, hop ? sc.Progress : 0, w);
            Resample();
        }
        else _pts.Clear();
        var at = new Vector2(w.PCX[p] + .5f, w.PCY[p] + .5f);
        var anchor = _pts.Count > 0 ? _pts[0] : at;
        float sy = v.ScreenY(at.Y);
        if (sy < -40 || sy > v.Screen.Y + 40) return;
        // the end mark sits on the hovered province: its copy nearest the start across the world wrap
        float ex = at.X;
        while (ex - anchor.X > w.W / 2f) ex -= w.W;
        while (anchor.X - ex > w.W / 2f) ex += w.W;
        for (float sx0 = v.FirstX(anchor.X, 400); sx0 < v.Screen.X + 400; sx0 += v.WZ)
        {
            var off = new Vector2(sx0 - anchor.X * z, v.Origin.Y);
            if (_pts.Count > 1)
            {
                BuildDashes(off, z, lw, dashOffset);
                if (_dashN > 1)
                {
                    var span = new ReadOnlySpan<Vector2>(_dash, 0, _dashN);
                    DrawMultiline(span, PickShade, lw + 2);
                    DrawMultiline(span, ok ? Pick : PickBad, lw);
                }
            }
            var e = new Vector2(ex * z + off.X, at.Y * z + off.Y);
            if (ok && _pts.Count > 1) DrawSprite(MapAtlas.TargetFlag, GameState.LocalPlayer, e.X + pz * 2, e.Y - pz * 3, pz);
            else if (!ok) DrawCross(e, Math.Max(5, pz * 3));
        }
    }

    void DrawCross(Vector2 c, float r)
    {
        DrawLine(c + new Vector2(-r, -r), c + new Vector2(r, r), PickShade, 5);
        DrawLine(c + new Vector2(-r, r), c + new Vector2(r, -r), PickShade, 5);
        DrawLine(c + new Vector2(-r, -r), c + new Vector2(r, r), PickBad, 3);
        DrawLine(c + new Vector2(-r, r), c + new Vector2(r, -r), PickBad, 3);
    }

    void BuildRoute(int[] R, int step, float progress, World.WorldData w)
    {
        _pts.Clear();
        int k = Math.Clamp(step, 0, R.Length - 1), a = R[k], b = R[Math.Min(k + 1, R.Length - 1)];
        float ax = w.PCX[a] + .5f, bx = w.PCX[b] + .5f;
        if (bx - ax > w.W / 2f) bx -= w.W; else if (ax - bx > w.W / 2f) bx += w.W;
        float t = Math.Clamp(progress, 0, 1);
        _pts.Add(new Vector2(ax + (bx - ax) * t, w.PCY[a] + .5f + (w.PCY[b] - w.PCY[a]) * t));
        float px = bx;
        for (int j = k + 1; j < R.Length; j++)
        {
            float x = w.PCX[R[j]] + .5f;
            while (x - px > w.W / 2f) x -= w.W;
            while (px - x > w.W / 2f) x += w.W;
            _pts.Add(new Vector2(x, w.PCY[R[j]] + .5f));
            px = x;
        }
    }

    void Resample()
    {
        _samples.Clear();
        bool fog = FogOn;
        for (int k = 1; k < _pts.Count; k++)
        {
            Vector2 p0 = _pts[k - 1], p1 = _pts[k];
            int n = Math.Max(1, (int)MathF.Ceiling(p0.DistanceTo(p1) / 2));
            for (int j = k == 1 ? 0 : 1; j <= n; j++)
            {
                var q = p0.Lerp(p1, j / (float)n);
                _samples.Add(!fog || Map.Fog.IsClear(q.X, q.Y, 18) ? q : new Vector2(float.NaN, float.NaN));
            }
        }
    }

    /// <summary>Screen-space dash segments (pairs) along the visible stretches; dashes march towards the target.</summary>
    void BuildDashes(Vector2 off, float z, float lw, float offset)
    {
        _dashN = 0;
        float dash = lw * 1.5f, period = dash * 2, dist = -offset;
        for (int i = 1; i < _samples.Count; i++)
        {
            var a = _samples[i - 1]; var b = _samples[i];
            if (float.IsNaN(a.X) || float.IsNaN(b.X)) continue;
            Vector2 sa = a * z + off, sb = b * z + off;
            float len = sa.DistanceTo(sb);
            float u = 0;
            while (u < len)
            {
                float ph = ((dist + u) % period + period) % period;
                float step = Math.Min(len - u, ph < dash ? dash - ph : period - ph);
                if (ph < dash) Push(sa.Lerp(sb, u / len), sa.Lerp(sb, (u + step) / len));
                u += step;
            }
            dist += len;
        }
    }

    void Push(Vector2 a, Vector2 b)
    {
        if (_dashN + 2 > _dash.Length) Array.Resize(ref _dash, _dash.Length * 2);
        _dash[_dashN++] = a; _dash[_dashN++] = b;
    }
}
