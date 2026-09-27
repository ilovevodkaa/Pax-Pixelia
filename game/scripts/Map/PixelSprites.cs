using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

internal enum Spr { Capital, Town, Farm, Lumber, Quarry, Fishery, Pasture, Shrine, Market, Granary, Scout0, Scout1, Flag, Count }

/// <summary>
/// Pixel-art map sprites authored as text (restyled from docs/mockups/js/render.js SPR: same subjects, now with a
/// dark outline and two-tone shading so they read on any tint). Baked once into one atlas — a column per nation
/// colour, a sprite row and a white-silhouette row per sprite — so a whole overlay chunk is a single batch.
/// Drawn at whole-pixel scales with nearest filtering; the silhouette makes the drop shadow.
/// </summary>
internal static class PixelSprites
{
    // palette: k outline · F/f flag · R/r roof dark/lit (nation) · C cloak (nation) · W/w wall · D dark opening · P wood
    //          Y/y wheat · T/t foliage · L logs/hull · G/g stone · S/s gold · O barn · H skin · E/e flame
    static readonly string[][] Rows =
    {
        new[] { // Capital: walled keep with the nation flag
            "......kk.....",
            "......kFFk...",
            "......kFFFk..",
            "......kfk....",
            "......k......",
            ".....krk.....",
            ".kk.krRRk.kk.",
            "krRkkWWWkkrRk",
            "kWwkkWDwkkWwk",
            "kWwWWWWWWWWwk",
            "kWwWWkDkWWWwk",
            "kkkkkkkkkkkkk" },
        new[] { // Town
            "....kk....",
            "...krRk...",
            "..krRRRk..",
            ".krRRRRRk.",
            "kkkkkkkkkk",
            ".kWDWWkwk.",
            ".kWWWWDwk.",
            ".kkkkkkkk." },
        new[] { "kkkkkkk", "kYyYyYk", "kyYyYyk", "kYyYyYk", "kkkkkkk" },                           // Farm
        new[] { "..kk.kk..", ".kTTkTTk.", "kTtTkTtTk", ".kTTkTTk.", "kkPkkkPkk", "kLLLLLLLk", "kkkkkkkkk" }, // Lumber
        new[] { "...kk...", "..kGgk..", ".kGGGgkk", "kGgGGGgk", "kkkkkkkk" },                        // Quarry
        new[] { "...k...", "..kWk..", ".kWWk..", "kkkkkkk", "kLLLLLk", ".kkkkk." },                   // Fishery
        new[] { ".kkkk..", "kWWWWkk", "kWWWWDk", ".kkkkk.", ".kDkDk." },                              // Pasture
        new[] { "..kkk..", ".kSsSk.", "kSSsSSk", ".kSsSk.", "kkWWWkk", "kWWWWWk", "kkkkkkk" },         // Shrine
        new[] { "kkkkkkk", "kRWRWRk", "kkkkkkk", "kWkkkWk", "kWkOkWk", "kkkkkkk" },                   // Market
        new[] { "...k...", "..kLk..", ".kLLLk.", "kLLLLLk", "kOOOOOk", "kODDDOk", "kkkkkkk" },         // Granary (barn)
        new[] { "..kk.kk", ".kHHkEk", ".kHHkek", "kCCCCPk", "kCCCCPk", ".kCCkPk", ".kCCkk.", ".kLkLk.", ".kk.kk." }, // Scout, frame 0
        new[] { "..kk.k.", ".kHHkek", ".kHHkEk", "kCCCCPk", "kCCCCPk", ".kCCkPk", ".kCCkk.", "kLkkLk.", "kk..kk." }, // Scout, frame 1
        new[] { "kkkkk.", "kWFFFk", "kWFFFk", "kWFkk.", "kWk...", "kWk...", "kkk..." },               // target flag
    };

    public static Spr ForBuilding(Data.Bld b) => b switch
    {
        Data.Bld.Farm => Spr.Farm, Data.Bld.Lumber => Spr.Lumber, Data.Bld.Quarry => Spr.Quarry, Data.Bld.Fishery => Spr.Fishery,
        Data.Bld.Pasture => Spr.Pasture, Data.Bld.Shrine => Spr.Shrine, Data.Bld.Market => Spr.Market, _ => Spr.Granary,
    };

    public static Vector2I Size(Spr s) { var r = Rows[(int)s]; return new Vector2I(r[0].Length, r.Length); }

    /// <summary>Screen px per sprite pixel for towns/capitals/scouts at a zoom level.</summary>
    public static int CityScale(int level) => Math.Max(1, (int)MathF.Floor(level * .5f + .5f));
    /// <summary>Screen px per sprite pixel for buildings (shown from ×5).</summary>
    public static int BuildingScale(int level) => Math.Max(1, (int)MathF.Floor(level * .4f + .5f));

    const int CellW = 14, CellH = 13;    // largest sprite + 1 px padding
    static Texture2D _atlas;
    static int _cols;

    public static Texture2D Atlas => _atlas ??= BakeAtlas();

    static Texture2D BakeAtlas()
    {
        _cols = Data.Nations.Length;
        var img = Image.CreateEmpty(_cols * CellW, (int)Spr.Count * 2 * CellH, false, Image.Format.Rgba8);
        for (int s = 0; s < (int)Spr.Count; s++)
        {
            var rows = Rows[s];
            for (int n = 0; n < _cols; n++)
                for (int y = 0; y < rows.Length; y++)
                    for (int x = 0; x < rows[y].Length; x++)
                    {
                        char ch = rows[y][x];
                        if (ch == '.') continue;
                        img.SetPixel(n * CellW + x, s * 2 * CellH + y, Colour(ch, n));
                        if (n == 0) img.SetPixel(x, (s * 2 + 1) * CellH + y, Colors.White);
                    }
        }
        return ImageTexture.CreateFromImage(img);
    }

    static Rect2 Region(Spr s, int n, bool shadow)
    {
        _ = Atlas;
        var r = Rows[(int)s];
        int col = shadow ? 0 : Math.Clamp(n, 0, _cols - 1);
        return new Rect2(col * CellW, ((int)s * 2 + (shadow ? 1 : 0)) * CellH, r[0].Length, r.Length);
    }

    static readonly Color ShadowTint = new(0, 0, 0, .35f);

    /// <summary>Draw sprite s (nation n's colours) centred at a point, scaled by whole pixels, with a drop shadow.</summary>
    public static void Draw(CanvasItem ci, Spr s, int n, float cx, float cy, int ps)
    {
        DrawShadow(ci, s, cx, cy, ps);
        DrawSprite(ci, s, n, cx, cy, ps);
    }

    /// <summary>Only the drop shadow (a layer draws all shadows first, then all sprites, to batch).</summary>
    public static void DrawShadow(CanvasItem ci, Spr s, float cx, float cy, int ps)
    {
        var size = Size(s);
        float w = size.X * ps, h = size.Y * ps;
        ci.DrawTextureRectRegion(Atlas, new Rect2(MathF.Round(cx - w / 2) + ps, MathF.Round(cy - h / 2) + ps, w, h), Region(s, 0, true), ShadowTint);
    }

    public static void DrawSprite(CanvasItem ci, Spr s, int n, float cx, float cy, int ps)
    {
        var size = Size(s);
        float w = size.X * ps, h = size.Y * ps;
        ci.DrawTextureRectRegion(Atlas, new Rect2(MathF.Round(cx - w / 2), MathF.Round(cy - h / 2), w, h), Region(s, n, false));
    }

    static Color Colour(char ch, int n)
    {
        var nat = MapPalette.Nation(n);
        return ch switch
        {
            'k' => Hex(0x241c17),
            'F' => nat.Scaled(1.15f, 20).ToColor(),
            'f' => nat.ToColor(),
            'R' => nat.Scaled(.72f).ToColor(),
            'r' => nat.Scaled(.95f, 14).ToColor(),
            'C' => nat.ToColor(),
            'W' => Hex(0xeee3cb),
            'w' => Hex(0xc9b99a),
            'D' => Hex(0x3a2e24),
            'P' => Hex(0x7a5634),
            'Y' => Hex(0xe3c85e),
            'y' => Hex(0xb8973e),
            'T' => Hex(0x2d5a33),
            't' => Hex(0x467a46),
            'L' => Hex(0x966c42),
            'G' => Hex(0x9a968e),
            'g' => Hex(0x68645e),
            'S' => Hex(0xf2cf6a),
            's' => Hex(0xc8a046),
            'O' => Hex(0xb88a52),
            'H' => Hex(0xe2b38a),
            'E' => Hex(0xffe27a),
            'e' => Hex(0xf0863a),
            _ => new Color(1, 0, 1),
        };
    }

    static Color Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
}
