using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// Outline icons from assets/ui/icons (24-unit Tabler-style SVGs with a white stroke), rasterised at the exact pixel size
/// they are shown at so they stay crisp; tint them with modulate / the Button icon colours.
/// The stroke is thinned slightly for larger sizes to keep an optical ~1.4px line everywhere.
/// </summary>
public static class Icons
{
    const string Dir = "res://assets/ui/icons/";
    static readonly Dictionary<(string, int), Texture2D> Cache = new();

    /// <summary>Accepts both "coins" and the mockup's "ti-coins" keys; unknown names fall back to info-circle.</summary>
    public static Texture2D Get(string name, int size)
    {
        if (string.IsNullOrEmpty(name)) name = "info-circle";
        if (name.StartsWith("ti-")) name = name[3..];
        if (Cache.TryGetValue((name, size), out var tex)) return tex;

        var path = Dir + name + ".svg";
        if (!FileAccess.FileExists(path)) return name == "info-circle" ? null : Cache[(name, size)] = Get("info-circle", size);
        string svg = FileAccess.GetFileAsString(path);
        float stroke = size <= 16 ? 2f : size <= 19 ? 1.85f : 1.7f;
        svg = svg.Replace("stroke-width=\"2\"", $"stroke-width=\"{stroke.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"");
        var img = new Image();
        img.LoadSvgFromString(svg, size / 24f);
        tex = ImageTexture.CreateFromImage(img);
        Cache[(name, size)] = tex;
        return tex;
    }

    /// <summary>A whole SVG from assets/ui rasterised at <paramref name="scale"/> (used for the loading compass rose).</summary>
    public static Texture2D Art(string file, float scale)
    {
        var key = (file, (int)(scale * 100));
        if (Cache.TryGetValue(key, out var tex)) return tex;
        var img = new Image();
        img.LoadSvgFromString(FileAccess.GetFileAsString("res://assets/ui/" + file), scale);
        img.GenerateMipmaps();
        return Cache[key] = ImageTexture.CreateFromImage(img);
    }
}
