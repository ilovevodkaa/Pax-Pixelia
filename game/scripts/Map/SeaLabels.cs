using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Sparse sea names (port of seaRank/seaLabels in docs/mockups/js/render.js). Candidates are ranked once per world:
/// open water first (own size + a share of neighbouring sea zones; zones touching land penalised; lakes and bays out);
/// at ×1 only the most open ocean. Each frame a greedy pass keeps generous spacing so a water region carries one name
/// at most; last frame's labels go first (no reshuffling while panning); labels must lie fully over known water and
/// clear of nation names, UI cards and screen edges.
/// </summary>
internal sealed class SeaLabels
{
    int[] _order = Array.Empty<int>();
    float[] _score = Array.Empty<float>();
    bool[] _open = Array.Empty<bool>();
    SpacedText[] _text = Array.Empty<SpacedText>();
    float[] _w11 = Array.Empty<float>();
    object _forWorld;

    readonly List<int> _prev = new(), _next = new();
    readonly List<(int p, float x, float y)> _out = new();
    int _prevLevel = -1;

    const float TopUi = 54 + 26, BottomUi = 28, SideUi = 28;

    void Rank()
    {
        var w = Game.I.World;
        _forWorld = w;
        _prev.Clear();
        var sizes = new List<int>();
        for (int p = 0; p < w.P; p++) if (w.PLand[p] == 0) sizes.Add(w.PSize[p]);
        sizes.Sort();
        // the mockup's 2500 px suits its sea zones; fall back to the largest quarter for other generators
        int thr = sizes.Count == 0 ? int.MaxValue : Math.Min(2500, sizes[(int)(sizes.Count * .75f)]);
        var c = new List<(int p, float s, bool open)>();
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] == 1 || w.PSize[p] < thr) continue;
            float sc = w.PSize[p]; int co = 0;
            foreach (int q in w.Adj[p]) if (w.PLand[q] == 1) co++; else sc += w.PSize[q] * .35f;
            c.Add((p, co > 0 ? sc * .6f : sc, co == 0));
        }
        c.Sort((a, b) => b.s.CompareTo(a.s));
        _order = new int[c.Count]; _score = new float[c.Count]; _open = new bool[c.Count];
        for (int i = 0; i < c.Count; i++) { _order[i] = c[i].p; _score[i] = c[i].s; _open[i] = c[i].open; }
        _text = new SpacedText[w.P];
        _w11 = new float[w.P];
        Array.Fill(_w11, -1);
    }

    public void Draw(CanvasItem ci, in MapViewport v, List<Rect2> boxes, UiBlockers ui)
    {
        var g = Game.I; var w = g.World; var s = g.State;
        if (_forWorld != w) Rank();
        int z = v.Level;
        const int fs = 11;                     // the pixel font 1:1, spaced caps like an old atlas
        const float ls = 3;
        float sp = z == 1 ? 560 : z == 2 ? 460 : 520, sp2 = sp * sp;
        float lim = z == 1 && _order.Length > 0 ? _score[Math.Min(_order.Length - 1, _order.Length >> 3)] : 0;
        var font = MapFonts.Pixel;
        bool fog = s.FogEnabled;

        _next.Clear();
        if (_prevLevel == z) _next.AddRange(_prev);
        for (int i = 0; i < _order.Length; i++)
        {
            int p = _order[i];
            if (z > 1 || (_open[i] && _score[i] >= lim)) if (!_prev.Contains(p) || _prevLevel != z) _next.Add(p);
        }
        _out.Clear();
        foreach (int p in _next)
        {
            if (fog && s.Fog[p] == 0) continue;
            float sy = v.ScreenY(w.PCY[p]);
            if (sy - fs < TopUi || sy + fs > v.Screen.Y - BottomUi) continue;
            float sx = v.FirstX(w.PCX[p], 0);
            if (sx > v.Screen.X) continue;
            if (_w11[p] < 0) _w11[p] = (_text[p] ??= new SpacedText(w.PName[p].ToUpperInvariant())).Width(font, fs, ls);
            float tw = _w11[p];
            float x0 = sx - tw / 2 - 10, y0 = sy - fs / 2f - 6, bw = tw + 20, bh = fs + 12;
            if (x0 < SideUi || x0 + bw > v.Screen.X - SideUi) continue;
            bool ok = true;
            foreach (var q in _out) { float dx = q.x - sx, dy = (q.y - sy) * 1.5f; if (dx * dx + dy * dy < sp2) { ok = false; break; } }
            if (!ok) continue;
            var box = new Rect2(x0, y0, bw, bh);
            if (ui.Hits(box, 14)) continue;
            foreach (var b in boxes) if (b.Intersects(box)) { ok = false; break; }
            if (!ok) continue;
            for (int k = 0; k <= 4 && ok; k++)
            {
                float X = x0 + bw * k / 4;
                ok = Water(v, X, sy, fog) && ((k & 1) == 1 || (Water(v, X, y0, fog) && Water(v, X, y0 + bh, fog)));
            }
            if (!ok) continue;
            _out.Add((p, sx, sy));
            boxes.Add(box);
        }
        _prev.Clear();
        foreach (var o in _out) _prev.Add(o.p);
        _prevLevel = z;
        foreach (var (p, x, y) in _out) _text[p].Draw(ci, font, fs, ls, x, y, 0, 0, MapPalette.SeaText, TextFx.Shadow, 1);
    }

    static bool Water(in MapViewport v, float X, float Y, bool fog)
    {
        var w = Game.I.World; var s = Game.I.State;
        var wp = v.ToWorld(new Vector2(X, Y));
        int wy = (int)MathF.Floor(wp.Y);
        if (wy < 0 || wy >= w.H) return false;
        int wx = ((int)MathF.Floor(wp.X) % w.W + w.W) % w.W;
        int q = w.Prov[wy * w.W + wx];
        return w.PLand[q] == 0 && (!fog || s.Fog[q] > 0);
    }
}
