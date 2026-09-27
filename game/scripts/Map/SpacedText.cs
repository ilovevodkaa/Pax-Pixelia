using System;
using Godot;

namespace PaxPixelia.Map;

/// <summary>
/// A label drawn glyph by glyph: wide letter spacing (HOI4-style nation names, sea names) along a tilted, gently
/// arched baseline. Pixel style: every glyph stays upright and snaps to whole pixels, so a tilted name climbs in
/// small steps like old strategy-game maps instead of blurring. Glyph strings/codes are split once, so drawing
/// allocates nothing.
/// </summary>
internal sealed class SpacedText
{
    public readonly string[] Chars;
    readonly long[] _codes;
    readonly float[] _adv;       // advances at the last measured size
    int _advSize = -1;
    Font _advFont;

    public SpacedText(string text)
    {
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        var list = new System.Collections.Generic.List<string>();
        while (e.MoveNext()) list.Add((string)e.Current);
        Chars = list.ToArray();
        _codes = new long[Chars.Length];
        for (int i = 0; i < Chars.Length; i++) _codes[i] = char.ConvertToUtf32(Chars[i], 0);
        _adv = new float[Chars.Length];
    }

    void Measure(Font f, int size)
    {
        if (size == _advSize && f == _advFont) return;
        for (int i = 0; i < _codes.Length; i++) _adv[i] = f.GetCharSize(_codes[i], size).X;
        _advSize = size; _advFont = f;
    }

    public float Width(Font f, int size, float spacing)
    {
        Measure(f, size);
        float w = 0;
        for (int i = 0; i < _adv.Length; i++) w += _adv[i];
        return w + spacing * Math.Max(0, _adv.Length - 1);
    }

    /// <summary>
    /// Draw centred on (cx, cy) along a baseline rotated by angle (radians) and bent into an arc of curvature bend
    /// (1/px; positive droops the ends). The effect (shadow / outline, <paramref name="depth"/> px) is drawn for all
    /// glyphs first so it never covers a neighbour's fill.
    /// </summary>
    public void Draw(CanvasItem ci, Font f, int size, float spacing, float cx, float cy, float angle, float bend,
                     Color fill, TextFx fx, int depth)
    {
        float tw = Width(f, size, spacing);
        float baseline = PixelText.CentreBaseline(f, size);
        float ca = MathF.Cos(angle), sa = MathF.Sin(angle);
        for (int pass = 0; pass < 2; pass++)
        {
            float x = -tw * .5f;
            for (int i = 0; i < Chars.Length; i++)
            {
                float mid = x + _adv[i] * .5f;                 // arc position of the glyph centre
                float sag = bend * mid * mid * .5f;
                float gx = cx + mid * ca - sag * sa, gy = cy + mid * sa + sag * ca;
                var o = new Vector2(MathF.Round(gx - _adv[i] * .5f), MathF.Round(gy + baseline));
                if (pass == 1) ci.DrawChar(f, o, Chars[i], size, fill);
                else
                {
                    if (fx == TextFx.Outline)
                    {
                        ci.DrawChar(f, o + new Vector2(-1, 0), Chars[i], size, PixelText.Ink);
                        ci.DrawChar(f, o + new Vector2(0, -1), Chars[i], size, PixelText.Ink);
                        ci.DrawChar(f, o + new Vector2(1, 0), Chars[i], size, PixelText.Ink);
                        ci.DrawChar(f, o + new Vector2(0, 1), Chars[i], size, PixelText.Ink);
                    }
                    for (int d = 1; d <= depth; d++) ci.DrawChar(f, o + new Vector2(d, d), Chars[i], size, PixelText.Ink);
                }
                x += _adv[i] + spacing;
            }
        }
    }
}
