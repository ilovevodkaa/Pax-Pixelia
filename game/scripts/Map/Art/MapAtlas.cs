using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.Map;

internal enum UnitKind { Scout, Caravan, Ship, Nomads }

/// <summary>
/// The map's sprite atlas, baked from the concept grids (<see cref="SpriteData"/>, ART_BIBLE §2, §10–§11) once per
/// nation roster: a column per nation with the magenta mask ramp (A/N/D) replaced by that nation's light/base/dark
/// ramp, plus column 0 holding white silhouettes for drop shadows and rims. A dark 1 px outline is added round every
/// solid pixel (never round smoke, steam or glow). One texture, so a whole overlay chunk batches into a few draws.
/// Sprites are drawn at whole-pixel scales with nearest filtering.
/// </summary>
internal static class MapAtlas
{
    public const int Eras = 11;
    const int Cell = 18;                       // largest sprite (16×16 city) + gap: nearest sampling never bleeds

    sealed class Def
    {
        public int W, H;
        public char[] Px;                      // after outline; '.' = empty
        public bool[] Solid;                   // casts a shadow (outline included, smoke excluded)
        public Vector2I Top;                   // topmost solid pixel (the capital's flag pole stands on it)
    }

    static readonly List<Def> Defs = new();
    static readonly int Bld0, Unit0, Idle0, Flag0, TargetFlagId, RhombusId, DotId, Site0, Puff0;

    static MapAtlas()
    {
        foreach (var rows in SpriteData.Cities) Add(rows, false);
        Bld0 = Defs.Count;
        foreach (var rows in SpriteData.Buildings) Add(rows, true);
        Unit0 = Defs.Count;
        foreach (var frames in new[] { SpriteData.Scout, SpriteData.Caravan, SpriteData.Ship, SpriteData.Nomads })
        {
            foreach (var f in frames) Add(f, true);
            foreach (var f in frames) Add(Mirror(f), true);
        }
        // a paused scout stands on frame 0 while its torch keeps flickering (ART_BIBLE §11): legs of frame 0, flame of k
        Idle0 = Defs.Count;
        for (int left = 0; left < 2; left++)
            for (int k = 0; k < 4; k++)
            {
                var rows = (string[])SpriteData.Scout[0].Clone();
                for (int r = 0; r < 3; r++) rows[r] = SpriteData.Scout[k][r];
                Add(left == 1 ? Mirror(rows) : rows, true);
            }
        Flag0 = Defs.Count;
        for (int f = 0; f < 4; f++) Add(WavingFlag(f), true);
        TargetFlagId = Defs.Count;
        Add(new[] { "NNNN.", "NNNNd", "NAAd.", "d....", "d....", "d....", "d...." }, true);
        RhombusId = Defs.Count;
        Add(new[] { "..A..", ".ANN.", "ANNND", ".NDD.", "..D.." }, true);
        DotId = Defs.Count;
        Add(new[] { "p" }, true);
        Site0 = Defs.Count;
        foreach (var rows in SpriteData.Sites) Add(rows, true);
        Puff0 = Defs.Count;
        foreach (var rows in SpriteData.Puff) Add(rows, true);
    }

    // ---------------------------------------------------------------- ids

    public static int City(int era) => Math.Clamp(era, 0, Eras - 1);
    public static int Building(int k) => Bld0 + Math.Clamp(k, 0, SpriteData.Buildings.Length - 1);
    public static int Unit(UnitKind k, int frame, bool left) => Unit0 + (int)k * 8 + (left ? 4 : 0) + (frame & 3);
    public static int ScoutIdle(int frame, bool left) => Idle0 + (left ? 4 : 0) + (frame & 3);
    public static int CapitalFlag(int frame) => Flag0 + (frame & 3);
    public static int TargetFlag => TargetFlagId;
    public static int Rhombus => RhombusId;
    public static int Dot => DotId;
    /// <summary>The construction scaffold of an era (frame 0..3), 10×12 + outline: the building's icon rises inside it.</summary>
    public static int Site(int era, int frame) => Site0 + Math.Clamp(era, 0, Eras - 1) * 4 + (frame & 3);
    /// <summary>The dust of a building just finished (frame 0..1).</summary>
    public static int Puff(int frame) => Puff0 + (frame & 1);

    public static Vector2I Size(int id) { var d = Defs[id]; return new Vector2I(d.W, d.H); }
    /// <summary>Topmost solid pixel of a sprite (in sprite px from its top-left corner).</summary>
    public static Vector2I Top(int id) => Defs[id].Top;

    // ---------------------------------------------------------------- building the defs

    static string[] Mirror(string[] rows)
    {
        var o = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++) { var c = rows[i].ToCharArray(); Array.Reverse(c); o[i] = new string(c); }
        return o;
    }

    /// <summary>Flag on a pole: an 8×5 cloth with a 2-step sine running along its columns (4 frames).</summary>
    static string[] WavingFlag(int f)
    {
        const int cw = 8, ch = 5, h = 13;
        var g = new char[h, cw + 1];
        for (int y = 0; y < h; y++) for (int x = 0; x <= cw; x++) g[y, x] = '.';
        g[0, 0] = 'Y';
        for (int y = 1; y < h; y++) g[y, 0] = 'd';
        for (int c = 1; c <= cw; c++)
        {
            int dy = ((c - 1 + 8 - 2 * f) & 7) < 4 ? 0 : 1;
            int next = ((c + 8 - 2 * f) & 7) < 4 ? 0 : 1;
            for (int r = 0; r < ch; r++)
            {
                char k = r == 0 && dy == 0 ? 'A' : dy == 1 && next == 0 ? 'D' : 'N';
                g[1 + dy + r, c] = r == ch - 1 && dy == 1 ? 'D' : k;
            }
        }
        var rows = new string[h];
        for (int y = 0; y < h; y++) { var sb = new char[cw + 1]; for (int x = 0; x <= cw; x++) sb[x] = g[y, x]; rows[y] = new string(sb); }
        return rows;
    }

    static void Add(string[] rows, bool pad)
    {
        int p = pad ? 1 : 0, w = 0;
        foreach (var r in rows) w = Math.Max(w, r.Length);
        int W = w + 2 * p, H = rows.Length + 2 * p;
        var px = new char[W * H];
        var solid = new bool[W * H];
        Array.Fill(px, '.');
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char ch = rows[y][x];
                if (ch is '.' or ' ') continue;
                int i = (y + p) * W + x + p;
                px[i] = ch;
                solid[i] = SpriteData.NoOutline.IndexOf(ch) < 0;
            }
        var edge = new List<int>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (px[i] != '.') continue;
                if ((x > 0 && solid[i - 1]) || (x < W - 1 && solid[i + 1]) || (y > 0 && solid[i - W]) || (y < H - 1 && solid[i + W])) edge.Add(i);
            }
        foreach (int i in edge) { px[i] = 'K'; solid[i] = true; }
        var top = new Vector2I(W / 2, 0);
        for (int i = 0; i < px.Length; i++)
            if (solid[i] && px[i] != 'K') { top = new Vector2I(i % W, i / W); break; }
        Defs.Add(new Def { W = W, H = H, Px = px, Solid = solid, Top = top });
    }

    // ---------------------------------------------------------------- baking

    static ImageTexture _tex;
    static int _cols;
    static int _key;

    public static Texture2D Texture => _tex ?? Bake(Core.Game.I?.Nations ?? Core.Data.Nations);

    /// <summary>Bake the atlas for a nation roster (no-op when the colours are unchanged).</summary>
    public static Texture2D Bake(Core.Data.Nation[] nations)
    {
        var h = new HashCode();
        foreach (var n in nations) { h.Add(n.R); h.Add(n.G); h.Add(n.B); }
        int key = h.ToHashCode() ^ nations.Length;
        if (_tex != null && key == _key) return _tex;
        _key = key;
        _cols = nations.Length + 1;
        int iw = _cols * Cell, ih = Defs.Count * Cell;
        var buf = new byte[iw * ih * 4];
        var pal = new uint[128];
        foreach (var (k, rgba) in SpriteData.Palette) if (k < 128) pal[k] = rgba;
        for (int col = 0; col < _cols; col++)
        {
            if (col > 0)
            {
                var nn = nations[col - 1];
                var (light, dark) = Ramp(nn.R, nn.G, nn.B);
                pal['A'] = light; pal['N'] = Pack(nn.R, nn.G, nn.B); pal['D'] = dark;
            }
            for (int s = 0; s < Defs.Count; s++)
            {
                var d = Defs[s];
                int ox = col * Cell, oy = s * Cell;
                for (int y = 0; y < d.H; y++)
                    for (int x = 0; x < d.W; x++)
                    {
                        int i = y * d.W + x;
                        char ch = d.Px[i];
                        if (ch == '.') continue;
                        uint c = col == 0 ? (d.Solid[i] ? 0xFFFFFFFFu : 0u) : ch < 128 ? pal[ch] : 0xFF00FFFFu;
                        if (c == 0) continue;
                        int o = ((oy + y) * iw + ox + x) * 4;
                        buf[o] = (byte)(c >> 24); buf[o + 1] = (byte)(c >> 16); buf[o + 2] = (byte)(c >> 8); buf[o + 3] = (byte)c;
                    }
            }
        }
        var img = Image.CreateFromData(iw, ih, false, Image.Format.Rgba8, buf);
        if (_tex == null) _tex = ImageTexture.CreateFromImage(img);
        else if (_tex.GetWidth() == iw && _tex.GetHeight() == ih) _tex.Update(img);
        else _tex.SetImage(img);
        return _tex;
    }

    static uint Pack(int r, int g, int b) => (uint)(r << 24 | g << 16 | b << 8 | 255);

    /// <summary>Nation ramp (ART_BIBLE §2 rule 2, pp.ramp): light shifts towards yellow, dark towards blue-violet.</summary>
    static (uint light, uint dark) Ramp(byte r, byte g, byte b)
    {
        new Color(r / 255f, g / 255f, b / 255f).ToHsv(out float h, out float s, out float v);
        return (Mk(1 / 6f, .035f, .78f, 1.26f), Mk(.70f, .04f, 1.08f, .64f));

        uint Mk(float target, float amt, float ds, float dv)
        {
            float hh = h;
            if (s > .05f)
            {
                float d = ((target - h + .5f) % 1f + 1f) % 1f - .5f;
                hh = ((h + Math.Clamp(d, -amt, amt)) % 1f + 1f) % 1f;
            }
            var c = Color.FromHsv(hh, Math.Clamp(s * ds, 0, 1), Math.Clamp(v * dv, 0, 1));
            return Pack((int)(c.R * 255 + .5f), (int)(c.G * 255 + .5f), (int)(c.B * 255 + .5f));
        }
    }

    // ---------------------------------------------------------------- drawing

    static readonly Color ShadowTint = new(0, 0, 0, .43f);

    static Rect2 Region(int id, int col)
    {
        var d = Defs[id];
        return new Rect2(Math.Clamp(col, 0, _cols - 1) * Cell, id * Cell, d.W, d.H);
    }

    /// <summary>Screen rect of sprite id centred at (cx, cy) with ps screen px per sprite px (snapped to whole px).</summary>
    public static Rect2 Dest(int id, float cx, float cy, int ps)
    {
        var d = Defs[id];
        float w = d.W * ps, h = d.H * ps;
        return new Rect2(MathF.Round(cx - w / 2), MathF.Round(cy - h / 2), w, h);
    }

    /// <summary>Draw sprite id (nation n's colours; n &lt; 0 = neutral) centred at a point, with its drop shadow.</summary>
    public static void Draw(CanvasItem ci, int id, int n, float cx, float cy, int ps)
    {
        DrawShadow(ci, id, cx, cy, ps);
        DrawSprite(ci, id, n, cx, cy, ps);
    }

    /// <summary>Only the drop shadow: one sprite pixel right and down, black 43% (a layer draws all shadows first).</summary>
    public static void DrawShadow(CanvasItem ci, int id, float cx, float cy, int ps)
    {
        var tex = Texture;
        var r = Dest(id, cx, cy, ps);
        ci.DrawTextureRectRegion(tex, new Rect2(r.Position + new Vector2(ps, ps), r.Size), Region(id, 0), ShadowTint);
    }

    public static void DrawSprite(CanvasItem ci, int id, int n, float cx, float cy, int ps) =>
        ci.DrawTextureRectRegion(Texture, Dest(id, cx, cy, ps), Region(id, n + 1));

    /// <summary>Only the bottom `rows` sprite rows of id (a building rising on its plot), where the whole sprite would
    /// stand, with their drop shadow.</summary>
    public static void DrawBottom(CanvasItem ci, int id, int n, float cx, float cy, int ps, int rows)
    {
        var d = Defs[id];
        rows = Math.Clamp(rows, 0, d.H);
        if (rows == 0) return;
        int cut = d.H - rows;
        var r = Dest(id, cx, cy, ps);
        var dst = new Rect2(r.Position.X, r.Position.Y + cut * ps, r.Size.X, rows * ps);
        Rect2 Src(int col) { var s = Region(id, col); return new Rect2(s.Position.X, s.Position.Y + cut, s.Size.X, rows); }
        ci.DrawTextureRectRegion(Texture, new Rect2(dst.Position + new Vector2(ps, ps), dst.Size), Src(0), ShadowTint);
        ci.DrawTextureRectRegion(Texture, dst, Src(n + 1));
    }

    /// <summary>A 1 px rim round the silhouette (4 offsets): a figure standing on a label, a capital's nation rim.</summary>
    public static void DrawRim(CanvasItem ci, int id, float cx, float cy, int ps, Color color, int px = 1)
    {
        var tex = Texture;
        var r = Dest(id, cx, cy, ps);
        var src = Region(id, 0);
        ci.DrawTextureRectRegion(tex, new Rect2(r.Position + new Vector2(-px, 0), r.Size), src, color);
        ci.DrawTextureRectRegion(tex, new Rect2(r.Position + new Vector2(px, 0), r.Size), src, color);
        ci.DrawTextureRectRegion(tex, new Rect2(r.Position + new Vector2(0, -px), r.Size), src, color);
        ci.DrawTextureRectRegion(tex, new Rect2(r.Position + new Vector2(0, px), r.Size), src, color);
    }
}
