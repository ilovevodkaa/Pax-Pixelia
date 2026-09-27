using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// Pixel icons of the HUD, drawn as 12×12 ASCII art (<see cref="PixelIconArt"/>) in two tones — '#' full, '+' mid —
/// auto-centred, with a baked hard shadow one art-pixel right and down. Scale 1 → 13 px, scale 2 → 26 px (nearest).
/// Textures are white/gray so they take the tint of SelfModulate or the Button icon colours; the black shadow
/// stays black under any tint. Unknown names fall back to "info-circle".
/// </summary>
public static class Icons
{
    public const int Grid = 12;
    static readonly Dictionary<(string, int, bool), Texture2D> Cache = new();

    public static int Size(int scale, bool shadow = true) => (Grid + (shadow ? 1 : 0)) * scale;

    public static Texture2D Get(string name, int scale = 1, bool shadow = true)
    {
        if (string.IsNullOrEmpty(name)) name = "info-circle";
        if (name.StartsWith("ti-")) name = name[3..];
        if (!PixelIconArt.Art.ContainsKey(name)) name = "info-circle";
        var key = (name, scale, shadow);
        if (Cache.TryGetValue(key, out var tex)) return tex;
        var img = Render(PixelIconArt.Art[name], shadow);
        if (scale > 1) img.Resize(img.GetWidth() * scale, img.GetHeight() * scale, Image.Interpolation.Nearest);
        return Cache[key] = ImageTexture.CreateFromImage(img);
    }

    public static bool Has(string name) => PixelIconArt.Art.ContainsKey(name);

    static Image Render(string art, bool shadow)
    {
        var rows = art.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int minX = Grid, minY = Grid, maxX = -1, maxY = -1;
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] != '.') { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        int ox = (Grid - (maxX - minX + 1)) / 2 - minX, oy = (Grid - (maxY - minY + 1)) / 2 - minY;
        int size = Grid + (shadow ? 1 : 0);
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var ink = new Color(0, 0, 0, .8f);
        var full = Colors.White;
        // on an inverted (light) button the icon is tinted dark and has no shadow: the mid tone becomes translucent ink
        var mid = shadow ? new Color(.5f, .5f, .52f) : new Color(1, 1, 1, .4f);
        if (shadow)
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] != '.') img.SetPixel(x + ox + 1, y + oy + 1, ink);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char c = rows[y][x];
                if (c == '#') img.SetPixel(x + ox, y + oy, full);
                else if (c == '+') img.SetPixel(x + ox, y + oy, mid);
            }
        return img;
    }
}
