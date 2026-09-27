using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// The HUD's one typeface: PixelifySansMrP without antialiasing (<see cref="PixelKit.LoadFont"/>). Its pixel grid is
/// about 11 px per em, so multiples of 11 (11, 22, 88) render cleanest; 13–14 keep 1 px strokes for dense body text.
/// </summary>
public static class UiFonts
{
    public const int Tiny = 11, Small = 13, Body = 14, Value = 16, Title = 22, Huge = 88;

    public static Font Regular => PixelKit.Body(400);
    public static Font Medium => PixelKit.Body(500);
    public static Font Semi => PixelKit.Body(600);
    public static Font Bold => PixelKit.Body(700);

    /// <summary>Tracked (letter-spaced) weight — kickers, headings, the nation name.</summary>
    public static Font Spaced(int px, int weight = 700) => PixelKit.Variant(weight, px);

    public static float Width(Font f, string text, int size) => f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
}
