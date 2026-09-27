using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// The surface primitive of the design: gradient fill (vertical or horizontal), 1px hairline border (solid or dashed,
/// per side), per-corner radius, stacked soft shadows, inset rules (the top bar's double rule, 3px nation-colour accents)
/// and an optional inner frame. A single StyleBoxFlat can do none of the stacking, so Box composes a few of them.
/// Configure with the fluent setters before handing it to a control; only <see cref="SetAccent"/> is meant for later changes.
/// </summary>
public partial class Box : StyleBox
{
    Color _top = Colors.Transparent, _bottom = Colors.Transparent;
    bool _horizontal;
    Color _border = Colors.Transparent;
    int _bl, _bt, _br, _bb;
    bool _dashed;
    int _rtl, _rtr, _rbr, _rbl;
    readonly List<(Color Color, int Size, Vector2 Offset)> _shadows = new();
    readonly List<(int Y, int H, Color Color, bool FromBottom)> _rules = new();
    (int H, Color Color) _accent;
    (int Inset, Color Color) _inner;

    StyleBoxFlat[] _shadowBoxes = Array.Empty<StyleBoxFlat>();
    StyleBoxFlat _fill, _edge, _innerEdge;
    bool _dirty = true;

    // polygon cache for the gradient overlay (last rect drawn)
    Rect2 _polyRect;
    Vector2[] _poly;
    Color[] _polyCol;

    // ---------------- fluent configuration ----------------
    public Box Fill(Color c) { _top = _bottom = c; return Touch(); }
    public Box Fill(Color top, Color bottom, bool horizontal = false) { _top = top; _bottom = bottom; _horizontal = horizontal; return Touch(); }
    public Box Border(Color c, int w = 1) => Border(c, w, w, w, w);
    public Box Border(Color c, int l, int t, int r, int b) { _border = c; _bl = l; _bt = t; _br = r; _bb = b; return Touch(); }
    public Box Dashed() { _dashed = true; return Touch(); }
    public Box Radius(int r) => Radius(r, r, r, r);
    public Box Radius(int tl, int tr, int br, int bl) { _rtl = tl; _rtr = tr; _rbr = br; _rbl = bl; return Touch(); }
    public Box Shadow(Color c, int size, float offsetY = 0) { _shadows.Add((c, size, new Vector2(0, offsetY))); return Touch(); }
    /// <summary>Horizontal band inside the box, drawn under the border; y from the top (or from the bottom).</summary>
    public Box Rule(int y, int h, Color c, bool fromBottom = false) { _rules.Add((y, h, c, fromBottom)); return Touch(); }
    /// <summary>Band of <paramref name="h"/> px at the very bottom, drawn over the border (the 3px owner-colour rule).</summary>
    public Box Accent(Color c, int h = 3) { _accent = (h, c); return Touch(); }
    /// <summary>1px frame inset from the outer edge (the loading plate's double frame).</summary>
    public Box Inner(int inset, Color c) { _inner = (inset, c); return Touch(); }
    public Box Pad(float all) => Pad(all, all, all, all);
    public Box Pad(float l, float t, float r, float b)
    {
        ContentMarginLeft = l; ContentMarginTop = t; ContentMarginRight = r; ContentMarginBottom = b;
        return this;
    }

    public void SetAccent(Color c)
    {
        if (_accent.Color == c) return;
        _accent.Color = c;
        EmitChanged();
    }

    Box Touch() { _dirty = true; _polyRect = default; return this; }

    public Box()
    {
        ContentMarginLeft = ContentMarginTop = ContentMarginRight = ContentMarginBottom = 0;
    }

    // ---------------- drawing ----------------
    public override Rect2 _GetDrawRect(Rect2 rect)
    {
        var r = rect;
        foreach (var s in _shadows) r = r.Merge(new Rect2(rect.Position + s.Offset, rect.Size).Grow(s.Size));
        return r;
    }

    public override void _Draw(Rid ci, Rect2 rect)
    {
        if (_dirty) Bake();
        foreach (var s in _shadowBoxes) s.Draw(ci, rect);

        if (_fill != null)
        {
            _fill.Draw(ci, rect);
            if (_top != _bottom) DrawGradient(ci, rect);
        }

        foreach (var (y, h, c, fromBottom) in _rules)
        {
            float top = fromBottom ? rect.End.Y - y - h : rect.Position.Y + y;
            float l = rect.Position.X + _bl, w = rect.Size.X - _bl - _br;
            RenderingServer.CanvasItemAddRect(ci, new Rect2(l, top, w, h), c);
        }

        if (_border.A > 0)
        {
            if (_dashed) DrawDashed(ci, rect);
            else _edge.Draw(ci, rect);
        }

        if (_innerEdge != null) _innerEdge.Draw(ci, rect.Grow(-_inner.Inset));

        if (_accent.H > 0 && _accent.Color.A > 0)
            RenderingServer.CanvasItemAddRect(ci, new Rect2(rect.Position.X, rect.End.Y - _accent.H, rect.Size.X, _accent.H), _accent.Color);
    }

    void Bake()
    {
        _dirty = false;
        _shadowBoxes = new StyleBoxFlat[_shadows.Count];
        for (int i = 0; i < _shadows.Count; i++)
        {
            var (c, size, off) = _shadows[i];
            var sb = Flat(Colors.Transparent);
            sb.ShadowColor = c; sb.ShadowSize = size; sb.ShadowOffset = off;
            _shadowBoxes[i] = sb;
        }
        _fill = _top.A > 0 || _bottom.A > 0 ? Flat(_bottom) : null;
        _edge = null;
        if (_border.A > 0 && !_dashed)
        {
            _edge = Flat(Colors.Transparent);
            _edge.DrawCenter = false;
            _edge.BorderColor = _border;
            _edge.BorderWidthLeft = _bl; _edge.BorderWidthTop = _bt; _edge.BorderWidthRight = _br; _edge.BorderWidthBottom = _bb;
        }
        _innerEdge = null;
        if (_inner.Inset > 0)
        {
            _innerEdge = Flat(Colors.Transparent);
            _innerEdge.DrawCenter = false;
            _innerEdge.BorderColor = _inner.Color;
            _innerEdge.SetBorderWidthAll(1);
            _innerEdge.CornerRadiusTopLeft = Math.Max(0, _rtl - _inner.Inset); _innerEdge.CornerRadiusTopRight = Math.Max(0, _rtr - _inner.Inset);
            _innerEdge.CornerRadiusBottomRight = Math.Max(0, _rbr - _inner.Inset); _innerEdge.CornerRadiusBottomLeft = Math.Max(0, _rbl - _inner.Inset);
        }
    }

    StyleBoxFlat Flat(Color bg)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = _rtl, CornerRadiusTopRight = _rtr, CornerRadiusBottomRight = _rbr, CornerRadiusBottomLeft = _rbl,
            CornerDetail = 6,
            AntiAliasing = _rtl + _rtr + _rbr + _rbl > 0,
            AntiAliasingSize = .6f,
        };
        return sb;
    }

    /// <summary>
    /// Gradient overlay: a rounded polygon inset by the border (at least 1px) so the anti-aliased edge of the flat
    /// fill underneath stays visible and the aliased polygon edge only ever meets a nearly identical colour.
    /// </summary>
    void DrawGradient(Rid ci, Rect2 rect)
    {
        if (rect != _polyRect || _poly == null)
        {
            _polyRect = rect;
            int inset = Math.Max(1, Math.Max(Math.Max(_bl, _bt), Math.Max(_br, _bb)));
            if (_bl + _bt + _br + _bb == 0 && _rtl + _rtr + _rbr + _rbl == 0) inset = 0;
            var r = rect.Grow(-inset);
            int max = (int)(Mathf.Min(r.Size.X, r.Size.Y) / 2);
            if (max <= 0) return;
            var pts = new List<Vector2>(40);
            Corner(pts, new Vector2(r.Position.X, r.Position.Y), Math.Min(max, _rtl - inset), 1, 1, Mathf.Pi);
            Corner(pts, new Vector2(r.End.X, r.Position.Y), Math.Min(max, _rtr - inset), -1, 1, Mathf.Pi * 1.5f);
            Corner(pts, new Vector2(r.End.X, r.End.Y), Math.Min(max, _rbr - inset), -1, -1, 0);
            Corner(pts, new Vector2(r.Position.X, r.End.Y), Math.Min(max, _rbl - inset), 1, -1, Mathf.Pi * .5f);
            // arcs of neighbouring corners may meet in one point (pill/round shapes): drop duplicates or triangulation fails
            for (int i = pts.Count - 1; i > 0; i--) if (pts[i].DistanceSquaredTo(pts[i - 1]) < 1e-4f) pts.RemoveAt(i);
            if (pts.Count > 1 && pts[0].DistanceSquaredTo(pts[^1]) < 1e-4f) pts.RemoveAt(pts.Count - 1);
            _poly = pts.ToArray();
            _polyCol = new Color[_poly.Length];
            for (int i = 0; i < _poly.Length; i++)
            {
                float t = _horizontal ? (_poly[i].X - rect.Position.X) / Math.Max(1, rect.Size.X) : (_poly[i].Y - rect.Position.Y) / Math.Max(1, rect.Size.Y);
                _polyCol[i] = _top.Lerp(_bottom, Mathf.Clamp(t, 0, 1));
            }
        }
        RenderingServer.CanvasItemAddPolygon(ci, _poly, _polyCol);
    }

    /// <summary>Arc points of one rounded corner (clockwise). sx/sy point from the corner towards the arc centre.</summary>
    static void Corner(List<Vector2> pts, Vector2 corner, int radius, int sx, int sy, float a0)
    {
        if (radius <= 0) { pts.Add(corner); return; }
        var c = corner + new Vector2(sx * radius, sy * radius);
        int seg = radius <= 3 ? 2 : radius <= 8 ? 4 : 8;
        for (int i = 0; i <= seg; i++)
        {
            float a = a0 + Mathf.Pi * .5f * i / seg;
            pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    /// <summary>CSS-like 1px dashed border (3px dash / 3px gap) with solid anti-aliased corner arcs.</summary>
    void DrawDashed(Rid ci, Rect2 rect)
    {
        const int dash = 3, gap = 3;
        float x0 = rect.Position.X, y0 = rect.Position.Y, x1 = rect.End.X, y1 = rect.End.Y;
        int r = _rtl; // dashed boxes use a uniform radius
        for (float x = x0 + r; x < x1 - r; x += dash + gap)
        {
            float w = Math.Min(dash, x1 - r - x);
            RenderingServer.CanvasItemAddRect(ci, new Rect2(x, y0, w, 1), _border);
            RenderingServer.CanvasItemAddRect(ci, new Rect2(x, y1 - 1, w, 1), _border);
        }
        for (float y = y0 + r; y < y1 - r; y += dash + gap)
        {
            float h = Math.Min(dash, y1 - r - y);
            RenderingServer.CanvasItemAddRect(ci, new Rect2(x0, y, 1, h), _border);
            RenderingServer.CanvasItemAddRect(ci, new Rect2(x1 - 1, y, 1, h), _border);
        }
        if (r <= 0) return;
        Span<Vector2> arc = stackalloc Vector2[5];
        Span<Color> col = stackalloc Color[5];
        col.Fill(_border);
        float rr = r - .5f;
        ArcAt(ci, arc, col, new Vector2(x0 + r, y0 + r), rr, Mathf.Pi);
        ArcAt(ci, arc, col, new Vector2(x1 - r, y0 + r), rr, Mathf.Pi * 1.5f);
        ArcAt(ci, arc, col, new Vector2(x1 - r, y1 - r), rr, 0);
        ArcAt(ci, arc, col, new Vector2(x0 + r, y1 - r), rr, Mathf.Pi * .5f);
    }

    static void ArcAt(Rid ci, Span<Vector2> arc, Span<Color> col, Vector2 c, float r, float a0)
    {
        for (int i = 0; i < arc.Length; i++)
        {
            float a = a0 + Mathf.Pi * .5f * i / (arc.Length - 1);
            arc[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        RenderingServer.CanvasItemAddPolyline(ci, arc, col, 1, true);
    }
}
