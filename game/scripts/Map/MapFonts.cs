using Godot;

namespace PaxPixelia.Map;

/// <summary>Fonts used on the map canvas (design_final: Alegreya SC for nations/capitals/seas, Fira Sans Condensed for towns/provinces).</summary>
internal static class MapFonts
{
    public static readonly Font Display500 = Load("AlegreyaSC-Medium.ttf");
    public static readonly Font Display700 = Load("AlegreyaSC-Bold.ttf");
    public static readonly Font Display800 = Load("AlegreyaSC-ExtraBold.ttf");
    public static readonly Font Ui500 = Load("FiraSansCondensed-Medium.ttf");
    public static readonly Font Ui600 = Load("FiraSansCondensed-SemiBold.ttf");

    static Font Load(string file) => GD.Load<Font>("res://assets/fonts/" + file);
}
