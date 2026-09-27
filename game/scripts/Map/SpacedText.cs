using System;
using Godot;

namespace PaxPixelia.Map;

/// <summary>
/// A label drawn glyph by glyph: wide letter spacing (HOI4-style nation names, sea names) and optional rotation
/// with a gentle arc. Glyph strings/codes are split once, so drawing allocates nothing.
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
    /// Draw centred on (cx, cy): baseline rotated by angle (radians), bent into an arc of curvature bend (1/px;
    /// positive bows the middle upwards). Halo (outline) is drawn for all glyphs first so it never covers a neighbour.
    /// </summary>
    public void Draw(CanvasItem ci, Font f, int size, float spacing, float cx, float cy, float angle, float bend, Color fill, int halo, Color haloColor)
    {
        float tw = Width(f, size, spacing);
        float baseline = (f.GetAscent(size) - f.GetDescent(size)) * .5f;
        bool plain = angle == 0 && bend == 0;
        float ca = MathF.Cos(angle), sa = MathF.Sin(angle);
        for (int pass = halo > 0 ? 0 : 1; pass < 2; pass++)
        {
            float x = -tw * .5f;
            for (int i = 0; i < Chars.Length; i++)
            {
                float mid = x + _adv[i] * .5f;            // arc position of the glyph centre
                // glyph centre on the arc: local (mid, sag) in the rotated frame, sag = bend·mid²/2 (ends droop for bend > 0)
                float sag = bend * mid * mid * .5f;
                float a = angle + MathF.Atan(bend * mid); // tangent angle there
                float gx = cx + mid * ca - sag * sa, gy = cy + mid * sa + sag * ca;
                // glyph origin = centre shifted back half an advance along the tangent and down to the baseline
                float ct = MathF.Cos(a), st = MathF.Sin(a);
                var origin = new Vector2(gx - _adv[i] * .5f * ct - baseline * st, gy - _adv[i] * .5f * st + baseline * ct);
                if (plain)
                {
                    var o = new Vector2(MathF.Round(origin.X), MathF.Round(origin.Y));
                    if (pass == 0) ci.DrawCharOutline(f, o, Chars[i], size, halo, haloColor);
                    else ci.DrawChar(f, o, Chars[i], size, fill);
                }
                else
                {
                    ci.DrawSetTransform(origin, a);
                    if (pass == 0) ci.DrawCharOutline(f, Vector2.Zero, Chars[i], size, halo, haloColor);
                    else ci.DrawChar(f, Vector2.Zero, Chars[i], size, fill);
                }
                x += _adv[i] + spacing;
            }
        }
        if (!plain) ci.DrawSetTransform(Vector2.Zero, 0);
    }
}
