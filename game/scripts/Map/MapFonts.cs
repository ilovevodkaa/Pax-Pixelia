using System;
using Godot;

namespace PaxPixelia.Map;

/// <summary>
/// Map text in the UI's pixel font (PixelifySansMrP, antialiasing / hinting / subpixel positioning off) at whole
/// pixel positions, with hard dark shadows instead of soft halos — the monochrome pixel style of the interface.
/// One design pixel of the font is ~0.09 em, so 11 px draws it 1:1 and 22 / 33 px at exactly ×2 / ×3.
/// </summary>
internal static class MapFonts
{
    const string Path = "res://assets/fonts/PixelifySansMrP.ttf";
    public static readonly Font Pixel = Load();

    /// <summary>Sizes the map uses (glyph caches are warmed up for them at WorldReady).</summary>
    public static readonly int[] Sizes = { 11, 13, 16, 22, 33 };

    static Font Load()
    {
        var file = ResourceLoader.Exists(Path) ? GD.Load<FontFile>(Path) : null;
        if (file == null)
        {
            // not imported yet (added outside the editor): read the .ttf directly, as the UI kit does
            file = new FontFile();
            if (file.LoadDynamicFont(ProjectSettings.GlobalizePath(Path)) != Error.Ok) file = null;
        }
        if (file == null) { GD.PushWarning($"map: font {Path} not found"); return ThemeDB.FallbackFont; }
        file.Antialiasing = TextServer.FontAntialiasing.None;
        file.Hinting = TextServer.Hinting.None;
        file.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        return file;
    }

    /// <summary>Rasterise the glyphs map labels use at every map size now, so the first zoom to a new level does not
    /// hitch on glyph rendering (Cyrillic, Latin, digits and punctuation).</summary>
    public static void WarmUp()
    {
        if (Pixel is not FontFile f) return;
        foreach (int size in Sizes)
        {
            var sz = new Vector2I(size, 0);
            f.RenderRange(0, sz, 0x20, 0x7E);
            f.RenderRange(0, sz, 0x401, 0x451);
            f.RenderRange(0, sz, 0xAB, 0xBB);
            f.RenderRange(0, sz, 0x2014, 0x2014);
        }
    }
}

internal enum TextFx { Shadow, Outline }

/// <summary>Pixel-font drawing with hard effects: a dark copy one (or two) px down-right, or a 1 px outline plus that
/// shadow. Positions are whole pixels (the font is not antialiased).</summary>
internal static class PixelText
{
    public static readonly Color Ink = new(12 / 255f, 13 / 255f, 16 / 255f, .9f);

    /// <summary>Two passes: pass 0 draws the effect, pass 1 the fill (a chunk draws every effect first so fills
    /// are never covered by a neighbour's shadow).</summary>
    public static void Draw(CanvasItem ci, int pass, Font f, Vector2 pos, string text, int size, Color fill, TextFx fx, int depth = 1)
    {
        pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
        if (pass == 1) { ci.DrawString(f, pos, text, HorizontalAlignment.Left, -1, size, fill); return; }
        if (fx == TextFx.Outline)
        {
            ci.DrawString(f, pos + new Vector2(-1, 0), text, HorizontalAlignment.Left, -1, size, Ink);
            ci.DrawString(f, pos + new Vector2(0, -1), text, HorizontalAlignment.Left, -1, size, Ink);
            ci.DrawString(f, pos + new Vector2(1, 0), text, HorizontalAlignment.Left, -1, size, Ink);
        }
        for (int d = 1; d <= depth; d++)
            ci.DrawString(f, pos + new Vector2(d, d), text, HorizontalAlignment.Left, -1, size, Ink);
        if (fx == TextFx.Outline || depth > 1) ci.DrawString(f, pos + new Vector2(0, 1), text, HorizontalAlignment.Left, -1, size, Ink);
    }

    /// <summary>Baseline offset that vertically centres a line of text of this size on a point.</summary>
    public static float CentreBaseline(Font f, int size) => MathF.Round((f.GetAscent(size) - f.GetDescent(size)) / 2);
}
