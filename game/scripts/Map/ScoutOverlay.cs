using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Map;

/// <summary>
/// Scouts: a small pixel figure with a flickering torch, the rest of its route as a marching dotted line and a
/// target flag (manual orders). Everything is hidden over unexplored land and the frayed cloud edge.
/// </summary>
internal partial class ScoutOverlay : MapOverlay
{
    readonly List<Vector2> _pts = new();       // remaining route, world px (x unwrapped from the scout)
    readonly List<Vector2> _samples = new();   // route resampled every 2 world px; NaN marks a hidden gap
    Vector2[] _dash = new Vector2[256];
    int _dashN;

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Process(double delta)
    {
        if (Game.I.IsReady && Game.I.State.Scouts.Count > 0) QueueRedraw();
    }

    public override void _Draw()
    {
        if (Map == null || !Map.HasWorld || !Game.I.IsReady) return;
        var s = Game.I.State;
        if (s.Scouts.Count == 0) return;
        var v = Map.View; var w = Game.I.World;
        float z = v.Zoom;
        int pz = PixelSprites.CityScale(v.Level);
        double t = Time.GetTicksMsec();
        int frame = s.Paused ? 0 : (int)(t / 260) & 1;
        float lw = Math.Max(2, MathF.Round(v.Level * .5f));
        float dashOffset = (float)(t / 60 % (lw * 3));

        foreach (var sc in s.Scouts)
        {
            if (sc.Path == null || sc.Path.Length == 0) continue;
            BuildRoute(sc, w);
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
                    if (!sc.Auto && (!FogOn || Map.Fog.IsClear(end.X, end.Y, 18)))
                        DrawSprite(Spr.Flag, GameState.LocalPlayer, end.X * z + off.X + pz * 2, end.Y * z + off.Y - pz * 3, pz);
                }
                if (visible) DrawSprite(frame == 0 ? Spr.Scout0 : Spr.Scout1, GameState.LocalPlayer, sx0, sy0 - pz * 3, pz);
            }
        }
    }

    void BuildRoute(GameState.Scout sc, World.WorldData w)
    {
        _pts.Clear();
        var R = sc.Path;
        int k = Math.Clamp(sc.Step, 0, R.Length - 1), a = R[k], b = R[Math.Min(k + 1, R.Length - 1)];
        float ax = w.PCX[a] + .5f, bx = w.PCX[b] + .5f;
        if (bx - ax > w.W / 2f) bx -= w.W; else if (ax - bx > w.W / 2f) bx += w.W;
        float t = Math.Clamp(sc.Progress, 0, 1);
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
