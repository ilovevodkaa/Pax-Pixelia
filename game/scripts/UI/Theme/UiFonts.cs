using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// The two OFL families of the design: Alegreya SC (titles, date, nation name) and Fira Sans Condensed (everything else).
/// Numbers always use tabular figures (Alegreya additionally lining — its default figures are old-style).
/// Tracked variants (CSS letter-spacing) are cached FontVariations.
/// </summary>
public static class UiFonts
{
    public static Font Fu400, Fu500, Fu600;     // Fira Sans Condensed, tabular numerals
    public static Font Fd500, Fd700, Fd800;     // Alegreya SC, lining + tabular numerals

    static FontFile _fu400, _fu500, _fu600, _fd500, _fd700, _fd800;
    static readonly Dictionary<(FontFile, int), FontVariation> Tracked = new();
    static bool _ready;

    public static void Init()
    {
        if (_ready) return;
        _ready = true;
        _fu400 = Load("FiraSansCondensed-Regular"); _fu500 = Load("FiraSansCondensed-Medium"); _fu600 = Load("FiraSansCondensed-SemiBold");
        _fd500 = Load("AlegreyaSC-Medium"); _fd700 = Load("AlegreyaSC-Bold"); _fd800 = Load("AlegreyaSC-ExtraBold");
        Fu400 = Variant(_fu400, 0); Fu500 = Variant(_fu500, 0); Fu600 = Variant(_fu600, 0);
        Fd500 = Variant(_fd500, 0); Fd700 = Variant(_fd700, 0); Fd800 = Variant(_fd800, 0);
    }

    /// <summary>The same face with extra per-glyph spacing (CSS letter-spacing in px).</summary>
    public static Font Track(Font f, int px)
    {
        if (px == 0) return f;
        var file = f == Fu400 ? _fu400 : f == Fu500 ? _fu500 : f == Fu600 ? _fu600 : f == Fd500 ? _fd500 : f == Fd700 ? _fd700 : _fd800;
        return Variant(file, px);
    }

    static FontVariation Variant(FontFile file, int spacing)
    {
        if (Tracked.TryGetValue((file, spacing), out var v)) return v;
        var ts = TextServerManager.GetPrimaryInterface();
        var features = new Godot.Collections.Dictionary { [ts.NameToTag("tnum")] = 1 };
        if (file == _fd500 || file == _fd700 || file == _fd800) features[ts.NameToTag("lnum")] = 1;
        v = new FontVariation { BaseFont = file, OpentypeFeatures = features, SpacingGlyph = spacing };
        Tracked[(file, spacing)] = v;
        return v;
    }

    static FontFile Load(string name)
    {
        var f = GD.Load<FontFile>($"res://assets/fonts/{name}.ttf");
        f.Antialiasing = TextServer.FontAntialiasing.Gray;
        f.Hinting = TextServer.Hinting.Light;
        f.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        f.GenerateMipmaps = false;
        return f;
    }
}
