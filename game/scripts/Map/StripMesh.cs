using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.Map;

/// <summary>
/// Builds one ArrayMesh of many polylines as triangle strips whose width is applied later in the vertex shader
/// (river.gdshader / route.gdshader), so zooming never rebuilds the mesh. Per vertex:
/// VERTEX = centre-line point, UV = (distance along the line, side ±1), COLOR.rg = miter normal*.25+.5, COLOR.b = width/2.
/// </summary>
internal sealed class StripMesh
{
    readonly List<Vector2> _v = new(), _uv = new();
    readonly List<Color> _c = new();
    readonly List<int> _idx = new();

    public int Vertices => _v.Count;

    /// <summary>Add a polyline shifted by dx. width(k, along) gives the width factor (0..2) at point k.</summary>
    public void Add(IReadOnlyList<Vector2> pts, float dx, Func<int, float, float> width)
    {
        int n = pts.Count;
        if (n < 2) return;
        int start = _v.Count;
        float along = 0;
        for (int k = 0; k < n; k++)
        {
            if (k > 0) along += pts[k].DistanceTo(pts[k - 1]);
            Vector2 a = k > 0 ? Perp(pts[k] - pts[k - 1]) : Perp(pts[1] - pts[0]);
            Vector2 b = k < n - 1 ? Perp(pts[k + 1] - pts[k]) : a;
            Vector2 m = a + b;
            m = m.LengthSquared() < 1e-8f ? a : m.Normalized();
            float dot = Math.Max(.5f, m.Dot(a));
            Vector2 nm = m / dot;                          // miter: keeps the strip width constant through bends
            var col = new Color(nm.X * .25f + .5f, nm.Y * .25f + .5f, Math.Clamp(width(k, along), 0, 2) * .5f, 1);
            var p = new Vector2(pts[k].X + dx, pts[k].Y);
            _v.Add(p); _uv.Add(new Vector2(along, -1)); _c.Add(col);
            _v.Add(p); _uv.Add(new Vector2(along, 1)); _c.Add(col);
        }
        for (int k = 0; k < n - 1; k++)
        {
            int i = start + k * 2;
            _idx.Add(i); _idx.Add(i + 1); _idx.Add(i + 2);
            _idx.Add(i + 1); _idx.Add(i + 3); _idx.Add(i + 2);
        }
    }

    static Vector2 Perp(Vector2 d)
    {
        float l = d.Length();
        return l < 1e-6f ? new Vector2(0, 1) : new Vector2(-d.Y / l, d.X / l);
    }

    public ArrayMesh Build()
    {
        if (_v.Count == 0) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
