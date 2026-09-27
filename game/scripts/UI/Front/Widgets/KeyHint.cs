using System;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// A keycap for hint lines (MAIN_MENU.md §2.3): «Enter», «Esc», «Ctrl+Enter», arrows (drawn as pixel triangles,
/// the font has none) and letter keys given as physical keys («Q» shows «Й» on a ЙЦУКЕН layout).
/// Key names: Up, Down, Left, Right, a single Latin letter (physical), anything else is printed as is.
/// </summary>
public partial class KeyHint : PanelContainer
{
    public KeyHint(string key)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddThemeStyleboxOverride("panel", PixelKit.Padded(PixelKit.Box(PixelKit.Surface, PixelKit.PanelBorder), 6, 2));
        var dir = key switch { "Up" => 0, "Down" => 1, "Left" => 2, "Right" => 3, _ => -1 };
        if (dir >= 0) { AddChild(new KeyArrow(dir)); return; }
        var l = PixelKit.Label(Caption(key), 11, PixelKit.Text);
        l.AddThemeFontOverride("font", PixelKit.Spaced(1));
        l.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(l);
    }

    /// <summary>Letter keys follow the active layout; the rest is uppercased as a keycap caption.</summary>
    public static string Caption(string key)
    {
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
        {
            var label = DisplayServer.KeyboardGetLabelFromPhysical(Key.A + (key[0] - 'A'));
            if (label != Key.None && (long)label < 0x110000) return char.ConvertFromUtf32((int)label).ToUpperInvariant();
        }
        return key.ToUpperInvariant();
    }

    /// <summary>A centred hint row: keycap + «what it does» (13 px TextDim), items separated by a dim dot.</summary>
    public static HBoxContainer Bar((string key, string text)[] hints)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < hints.Length; i++)
        {
            if (i > 0) row.AddChild(PixelKit.Label("·", 13, PixelKit.TextMuted));
            foreach (var k in hints[i].key.Split(' ', StringSplitOptions.RemoveEmptyEntries)) row.AddChild(new KeyHint(k));
            row.AddChild(PixelKit.Label(hints[i].text, 13, PixelKit.TextDim));
        }
        return row;
    }
}

/// <summary>A 5×3 triangle drawn at ×2 (10×6 px).</summary>
internal partial class KeyArrow : Control
{
    readonly int _dir;
    public KeyArrow(int dir) { _dir = dir; CustomMinimumSize = new Vector2(10, 11); MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        var c = PixelKit.Text;
        for (int i = 0; i < 3; i++)
        {
            // line i counted from the tip spans 2i+1 units; a unit is 2×2 px
            int len = (2 * i + 1) * 2, from = (2 - i) * 2;
            var r = _dir switch
            {
                0 => new Rect2(from, 2 + i * 2, len, 2),
                1 => new Rect2(from, 2 + (2 - i) * 2, len, 2),
                2 => new Rect2(2 + i * 2, from, 2, len),
                _ => new Rect2(2 + (2 - i) * 2, from, 2, len),
            };
            DrawRect(r, c);
        }
    }
}
