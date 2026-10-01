using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// The pixel surface primitive: square corners only, solid fill, optional Bayer-dithered gradient band and film grain,
/// 2px frames (solid or pixel-dashed, per side), a HARD offset shadow (an L of solid pixels, never blurred),
/// inset rules and the hover accent bar at the left. Configure with the fluent setters before handing it to a control.
/// Drawing is a handful of rects per box; textures (dither bands, grain) are generated once and cached.
/// </summary>
public partial class Box : StyleBox
{
    Color _fill = Colors.Transparent;
    Color _border = Colors.Transparent;
    int _bl, _bt, _br, _bb;
    int _dash;
    Vector2I _shadow;
    Color _shadowColor = Pal.Shadow;
    readonly List<(int Y, int H, Color Color, bool FromBottom)> _rules = new();
    (int W, Color Color) _accentLeft;
    (Color Top, Color Bottom, int Band, int Px) _dither;
    Texture2D _ditherTex;
    bool _grain;

    public Box() { ContentMarginLeft = ContentMarginTop = ContentMarginRight = ContentMarginBottom = 0; }

    // ---------------- fluent configuration ----------------
    public Box Fill(Color c) { _fill = c; return this; }
    /// <summary>Vertical gradient over the top <paramref name="band"/> px, quantised to 4 tones with a 4×4 Bayer
    /// pattern in <paramref name="px"/>-sized virtual pixels (the Mr. President skies, on a panel).</summary>
    public Box Dither(Color top, Color bottom, int band, int px = 2) { _dither = (top, bottom, band, px); _ditherTex = null; return this; }
    public Box Grain(bool on = true) { _grain = on; return this; }
    public Box Border(Color c, int w = 2) => Border(c, w, w, w, w);
    public Box Border(Color c, int l, int t, int r, int b) { _border = c; _bl = l; _bt = t; _br = r; _bb = b; return this; }
    /// <summary>Pixel-dashed frame: dashes and gaps of <paramref name="dash"/> px.</summary>
    public Box Dashed(int dash = 4) { _dash = dash; return this; }
    /// <summary>Hard drop shadow offset right/down by (x, y) — solid, no blur.</summary>
    public Box Shadow(int x = 4, int y = -1, Color? c = null) { _shadow = new Vector2I(x, y < 0 ? x : y); if (c is { } cc) _shadowColor = cc; return this; }
    /// <summary>Horizontal band inside the frame; y from the top (or from the bottom).</summary>
    public Box Rule(int y, int h, Color c, bool fromBottom = false) { _rules.Add((y, h, c, fromBottom)); return this; }
    /// <summary>Bar of <paramref name="w"/> px along the left edge, over the frame (the BigButton hover accent).</summary>
    public Box AccentLeft(Color c, int w = 4) { _accentLeft = (w, c); return this; }
    public Box Pad(float all) => Pad(all, all, all, all);
    public Box Pad(float h, float v) => Pad(h, v, h, v);
    public Box Pad(float l, float t, float r, float b)
    {
        ContentMarginLeft = l; ContentMarginTop = t; ContentMarginRight = r; ContentMarginBottom = b;
        return this;
    }


    // ---------------- drawing ----------------
    public override Rect2 _GetDrawRect(Rect2 rect) => new(rect.Position, rect.Size + new Vector2(_shadow.X, _shadow.Y));

    public override void _Draw(Rid ci, Rect2 rect)
    {
        rect = new Rect2(rect.Position.Round(), rect.Size.Round());
        float x0 = rect.Position.X, y0 = rect.Position.Y, x1 = rect.End.X, y1 = rect.End.Y;
        if (_shadow != Vector2I.Zero && _shadowColor.A > 0)
        {
            // the L below and right of the box only: a translucent card must not darken over its own shadow
            Rect(ci, x0 + _shadow.X, y1, rect.Size.X, _shadow.Y, _shadowColor);
            Rect(ci, x1, y0 + _shadow.Y, _shadow.X, rect.Size.Y - _shadow.Y, _shadowColor);
        }
        if (_fill.A > 0) Rect(ci, x0, y0, rect.Size.X, rect.Size.Y, _fill);

        var inner = new Rect2(x0 + _bl, y0 + _bt, rect.Size.X - _bl - _br, rect.Size.Y - _bt - _bb);
        if (_dither.Band > 0 && inner.Size.X > 0)
        {
            _ditherTex ??= PixelTex.DitherBand(_dither.Top, _dither.Bottom, _dither.Band, _dither.Px);
            float h = Math.Min(_dither.Band, inner.Size.Y);
            RenderingServer.CanvasItemAddTextureRectRegion(ci, new Rect2(inner.Position, new Vector2(inner.Size.X, h)), _ditherTex.GetRid(),
                new Rect2(0, 0, inner.Size.X, h));
        }
        if (_grain && inner.Size.X > 0)
            RenderingServer.CanvasItemAddTextureRectRegion(ci, inner, PixelTex.Grain.GetRid(), new Rect2(Vector2.Zero, inner.Size));

        foreach (var (y, h, c, fromBottom) in _rules)
            Rect(ci, inner.Position.X, fromBottom ? y1 - _bb - y - h : y0 + _bt + y, inner.Size.X, h, c);

        if (_border.A > 0)
        {
            if (_dash > 0) Dashes(ci, rect);
            else
            {
                if (_bt > 0) Rect(ci, x0, y0, rect.Size.X, _bt, _border);
                if (_bb > 0) Rect(ci, x0, y1 - _bb, rect.Size.X, _bb, _border);
                if (_bl > 0) Rect(ci, x0, y0 + _bt, _bl, rect.Size.Y - _bt - _bb, _border);
                if (_br > 0) Rect(ci, x1 - _br, y0 + _bt, _br, rect.Size.Y - _bt - _bb, _border);
            }
        }
        if (_accentLeft.W > 0 && _accentLeft.Color.A > 0) Rect(ci, x0, y0, _accentLeft.W, rect.Size.Y, _accentLeft.Color);
    }

    static void Rect(Rid ci, float x, float y, float w, float h, Color c)
    {
        if (w > 0 && h > 0) RenderingServer.CanvasItemAddRect(ci, new Rect2(x, y, w, h), c);
    }

    /// <summary>Square dashes along each framed side; corners are always solid so the frame reads as closed.</summary>
    void Dashes(Rid ci, Rect2 rect)
    {
        float x0 = rect.Position.X, y0 = rect.Position.Y, x1 = rect.End.X, y1 = rect.End.Y;
        int step = _dash * 2;
        for (float x = x0; x < x1; x += step)
        {
            float w = Math.Min(_dash, x1 - x);
            if (_bt > 0) Rect(ci, x, y0, w, _bt, _border);
            if (_bb > 0) Rect(ci, x, y1 - _bb, w, _bb, _border);
        }
        for (float y = y0; y < y1; y += step)
        {
            float h = Math.Min(_dash, y1 - y);
            if (_bl > 0) Rect(ci, x0, y, _bl, h, _border);
            if (_br > 0) Rect(ci, x1 - _br, y, _br, h, _border);
        }
        Rect(ci, x1 - _dash, y1 - Math.Max(_bb, 1), _dash, Math.Max(_bb, 1), _border);
        Rect(ci, x1 - Math.Max(_br, 1), y1 - _dash, Math.Max(_br, 1), _dash, _border);
    }
}

/// <summary>Generated pixel textures shared by the HUD (created once, cached): dither bands and film grain.</summary>
public static class PixelTex
{
    static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    static readonly Dictionary<(Color, Color, int, int), Texture2D> Bands = new();
    static Texture2D _grain;

    /// <summary>Bayer threshold 0..1 of a virtual pixel.</summary>
    public static float Threshold(int x, int y) => (Bayer[(x & 3) + (y & 3) * 4] + .5f) / 16f;

    /// <summary>Top→bottom gradient in 4 tones, 4×4 Bayer dithered in <paramref name="px"/>-sized pixels; tiles horizontally.</summary>
    public static Texture2D DitherBand(Color top, Color bottom, int height, int px, int steps = 4)
    {
        var key = (top, bottom, height, px);
        if (Bands.TryGetValue(key, out var tex)) return tex;
        int w = 4 * px, vh = Math.Max(1, height / px);
        var img = Image.CreateEmpty(w, height, false, Image.Format.Rgba8);
        for (int y = 0; y < height; y++)
        {
            int vy = y / px;
            float t = vh <= 1 ? 0 : (float)vy / (vh - 1);
            for (int x = 0; x < w; x++)
            {
                float level = Mathf.Clamp(Mathf.Floor(t * steps + Threshold(x / px, vy)) / steps, 0, 1);
                img.SetPixel(x, y, top.Lerp(bottom, level));
            }
        }
        return Bands[key] = ImageTexture.CreateFromImage(img);
    }

    static SkinTexture _kind = SkinTexture.Specks;
    static bool _light;

    /// <summary>Switch the film over the cards (an era skin's texture); the tile is rebuilt on next use.</summary>
    public static void SetTexture(SkinTexture kind, bool light)
    {
        if (kind == _kind && light == _light && _grain != null) return;
        _kind = kind; _light = light; _grain = null;
    }

    static uint Hash(int x, int y, uint salt)
    {
        uint h = 2166136261 ^ salt;
        h = (h ^ (uint)(x * 73856093 ^ y * 19349663)) * 16777619;
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return h;
    }

    /// <summary>64×64 tile laid over every card: the skin's surface (specks, stone crumbs, papyrus fibres, parchment
    /// stains, brushed metal, scanlines). Light skins darken, dark skins mostly lighten.</summary>
    public static Texture2D Grain
    {
        get
        {
            if (_grain != null) return _grain;
            var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
            var lit = _light ? new Color(1, 1, 1, .18f) : new Color(1, 1, 1, .028f);
            var dark = _light ? new Color(.25f, .14f, .04f, .07f) : new Color(0, 0, 0, .09f);
            switch (_kind)
            {
                case SkinTexture.Stone:
                    for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        uint r = Hash(x, y, 11) % 100;
                        if (r < 9) img.FillRect(new Rect2I(x * 4, y * 4, 2 + (int)(r % 3), 2 + (int)(r % 2) * 2), new Color(1, 1, 1, .035f));
                        else if (r < 24) img.FillRect(new Rect2I(x * 4 + 1, y * 4 + 1, 2 + (int)(r % 2) * 2, 2), new Color(0, 0, 0, .11f));
                    }
                    break;
                case SkinTexture.Fibre:
                    for (int y = 0; y < 64; y += 2)
                    {
                        int x = (int)(Hash(0, y, 21) % 64), len = 6 + (int)(Hash(1, y, 21) % 22);
                        img.FillRect(new Rect2I(x, y, Mathf.Min(len, 64 - x), 1), Hash(2, y, 21) % 3 == 0 ? lit : dark);
                    }
                    for (int x = 0; x < 64; x += 16) img.FillRect(new Rect2I(x + (int)(Hash(x, 3, 22) % 8), 0, 1, 64), new Color(dark, dark.A * .6f));
                    break;
                case SkinTexture.Parchment:
                    for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        uint r = Hash(x, y, 31) % 100;
                        if (r < 10) img.FillRect(new Rect2I(x * 2, y * 2, 2, 2), dark);
                        else if (r < 14) img.FillRect(new Rect2I(x * 2, y * 2, 2, 2), lit);
                    }
                    for (int k = 0; k < 3; k++)   // soft stains
                    {
                        int cx = (int)(Hash(k, 7, 32) % 64), cy = (int)(Hash(k, 8, 32) % 64), rad = 6 + (int)(Hash(k, 9, 32) % 6);
                        for (int y = -rad; y <= rad; y += 2)
                        for (int x = -rad; x <= rad; x += 2)
                            if (x * x + y * y <= rad * rad && Hash(x, y, (uint)k) % 3 == 0)
                                img.FillRect(new Rect2I((cx + x) & 63 & ~1, (cy + y) & 63 & ~1, 2, 2), new Color(dark, dark.A * .8f));
                    }
                    break;
                case SkinTexture.Brushed:
                    for (int y = 0; y < 64; y++)
                    {
                        uint r = Hash(0, y, 41) % 100;
                        if (r < 30) img.FillRect(new Rect2I(0, y, 64, 1), new Color(1, 1, 1, .012f + r % 4 * .004f));
                        else if (r < 55) img.FillRect(new Rect2I(0, y, 64, 1), new Color(0, 0, 0, .05f));
                    }
                    break;
                case SkinTexture.Scanlines:
                    for (int y = 0; y < 64; y += 3) img.FillRect(new Rect2I(0, y, 64, 1), new Color(0, 0, 0, .16f));
                    for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        if (Hash(x, y, 51) % 100 < 3) img.FillRect(new Rect2I(x * 2, y * 2, 2, 2), new Color(.5f, 1, 1, .03f));
                    break;
                default:
                    for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        uint r = Hash(x, y, 1) % 100;
                        var c = r < 7 ? lit : r < 16 ? dark : Colors.Transparent;
                        if (c.A > 0) img.FillRect(new Rect2I(x * 2, y * 2, 2, 2), c);
                    }
                    break;
            }
            return _grain = ImageTexture.CreateFromImage(img);
        }
    }
}
