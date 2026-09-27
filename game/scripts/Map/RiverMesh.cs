using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Map;

/// <summary>River polylines → one strip mesh (three copies: x − W, x, x + W, for the wrapped world).</summary>
internal static class RiverMesh
{
    public static ArrayMesh Build(WorldData w)
    {
        if (w.Rivers == null || w.Rivers.Count == 0) return null;
        var strip = new StripMesh();
        var pts = new List<Vector2>(256);
        foreach (var r in w.Rivers)
        {
            pts.Clear();
            for (int k = 0; k < r.Xs.Length; k++) pts.Add(new Vector2(r.Xs[k], r.Ys[k]));
            if (pts.Count < 2) continue;
            var flow = r.Flow;
            // width follows the water it carries: thin at the head, swelling below every confluence
            Func<int, float, float> width = flow != null && flow.Length == pts.Count
                ? (k, _) => .5f + 1.3f * flow[k]
                : static (_, along) => .55f + .95f * MathF.Min(1, along / 320f);
            for (int c = -1; c <= 1; c++) strip.Add(pts, c * w.W, width);
        }
        return strip.Build();
    }
}

/// <summary>Trade routes (province paths through province centres) → one strip mesh, three copies.</summary>
internal static class RouteMesh
{
    /// <summary>Cheap fingerprint of the route list, to rebuild the mesh only when routes really changed.</summary>
    public static int Key(GameState s)
    {
        if (s?.Routes == null) return 0;
        var h = new HashCode();
        h.Add(s.Routes.Count);
        foreach (var r in s.Routes) { if (r == null) continue; h.Add(r.Length); if (r.Length > 0) { h.Add(r[0]); h.Add(r[^1]); } }
        return h.ToHashCode();
    }

    static readonly List<Vector2> _tmp = new();

    /// <summary>One Chaikin corner-cutting pass (end points kept).</summary>
    static void Chaikin(List<Vector2> pts, List<Vector2> tmp)
    {
        if (pts.Count < 3) return;
        tmp.Clear();
        tmp.Add(pts[0]);
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            if (i > 0) tmp.Add(a.Lerp(b, .25f));
            if (i < pts.Count - 2) tmp.Add(a.Lerp(b, .75f));
        }
        tmp.Add(pts[^1]);
        pts.Clear(); pts.AddRange(tmp);
    }

    /// <summary>A route as a smooth polyline in world px (x unwrapped along the way, starting in [0, W)).</summary>
    public static void Polyline(WorldData w, int[] path, List<Vector2> pts)
    {
        pts.Clear();
        float prevX = w.PCX[path[0]] + .5f;
        pts.Add(new Vector2(prevX, w.PCY[path[0]] + .5f));
        for (int k = 1; k < path.Length; k++)
        {
            float x = w.PCX[path[k]] + .5f;
            while (x - prevX > w.W / 2f) x -= w.W;
            while (prevX - x > w.W / 2f) x += w.W;
            pts.Add(new Vector2(x, w.PCY[path[k]] + .5f));
            prevX = x;
        }
        Chaikin(pts, _tmp); Chaikin(pts, _tmp);          // soften the corners at province centres
    }

    public static ArrayMesh Build(WorldData w, GameState s)
    {
        if (w == null || s?.Routes == null || s.Routes.Count == 0) return null;
        var strip = new StripMesh();
        var pts = new List<Vector2>(64);
        foreach (var path in s.Routes)
        {
            if (path == null || path.Length < 2) continue;
            Polyline(w, path, pts);
            for (int c = -1; c <= 1; c++) strip.Add(pts, c * w.W, static (_, _) => 1f);
        }
        return strip.Build();
    }
}
