using System;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// The nation's pixel flag, 18×12 art pixels drawn at ×2 inside a hard 2px ink frame with a hard shadow.
/// </summary>
public partial class FlagView : Control
{
    const int W = 18, H = 12, Px = 2;
    ImageTexture _tex;

    public FlagView()
    {
        CustomMinimumSize = new Vector2(W * Px + 4 + 2, H * Px + 4 + 2);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>The nation's real flag (FlagRender, the same 18×12 art as the menu, the chapter card and the map).</summary>
    public void SetFlag(Core.Flags.FlagSpec spec, (byte R, byte G, byte B) nation)
    {
        var px = Core.Flags.FlagRender.Render(spec, nation, W, H);
        _tex = ImageTexture.CreateFromImage(Image.CreateFromData(W, H, false, Image.Format.Rgba8, px));
        QueueRedraw();
    }

    public override void _Draw()
    {
        float w = W * Px + 4, h = H * Px + 4;
        DrawRect(new Rect2(2, h, w, 2), Pal.Shadow);
        DrawRect(new Rect2(w, 2, 2, h - 2), Pal.Shadow);
        DrawRect(new Rect2(0, 0, w, h), Pal.Ink);
        if (_tex != null) DrawTextureRect(_tex, new Rect2(2, 2, W * Px, H * Px), false);
    }
}

/// <summary>Five ascending speed bars; click a bar to set that speed (hover previews it).</summary>
public partial class SpeedPips : Control
{
    const int Bar = 6, Gap = 2, Tall = 12;
    int _speed = 2;
    int _hover = -1;
    public event Action<int> SpeedPicked;

    public SpeedPips()
    {
        CustomMinimumSize = new Vector2(5 * Bar + 4 * Gap, Tall);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += () => { _hover = -1; QueueRedraw(); };
    }

    public void SetSpeed(int s) { if (s == _speed) return; _speed = s; QueueRedraw(); }

    /// <summary>«Умное время» runs the clock faster: the pips above the player's speed glow in the accent.</summary>
    public bool Hurry { get => _hurry; set { if (value == _hurry) return; _hurry = value; QueueRedraw(); } }
    bool _hurry;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            int h = PipAt(mm.Position.X);
            if (h != _hover) { _hover = h; QueueRedraw(); }
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            SpeedPicked?.Invoke(PipAt(mb.Position.X) + 1);
            AcceptEvent();
        }
    }

    static int PipAt(float x) => Math.Clamp((int)(x / (Bar + Gap)), 0, 4);

    public override void _Draw()
    {
        float y0 = Size.Y - Tall;
        for (int i = 0; i < 5; i++)
        {
            int h = 4 + 2 * i;
            var c = i < _speed ? Pal.Hi : _hurry ? Pal.Ac : i <= _hover ? Pal.Ln3 : Pal.Ln2;
            DrawRect(new Rect2(i * (Bar + Gap), y0 + Tall - h, Bar, h), c);
        }
    }
}

/// <summary>Five-step meter (fertility): square pixel cells.</summary>
public partial class Pips5 : Control
{
    readonly int _n;
    public Pips5(int n)
    {
        _n = Math.Clamp(n, 0, 5);
        CustomMinimumSize = new Vector2(5 * 10 + 4 * 2, 8);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        for (int i = 0; i < 5; i++)
        {
            var r = new Rect2(i * 12, 0, 10, 8);
            if (i < _n) { DrawRect(r, Pal.Ac); DrawRect(new Rect2(r.Position, new Vector2(10, 2)), Pal.Hi); }
            else { DrawRect(r, Pal.Ln); DrawRect(r.Grow(-2), Pal.Well); }
        }
    }
}

/// <summary>Segmented population-class bar: square segments split by 2px ink, framed, lit top edge.</summary>
public partial class ClassBar : Control
{
    readonly float[] _parts;
    readonly Color[] _colors;

    public ClassBar(float[] parts, Color[] colors)
    {
        _parts = parts;
        _colors = colors;
        CustomMinimumSize = new Vector2(0, 14);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float total = 0; foreach (var p in _parts) total += p;
        DrawRect(new Rect2(Vector2.Zero, Size), Pal.Ink);
        float x = 2, w = Size.X - 4, h = Size.Y - 4;
        for (int i = 0; i < _parts.Length; i++)
        {
            float sw = i == _parts.Length - 1 ? 2 + w - x : Mathf.Round(_parts[i] / total * w);
            float gap = i < _parts.Length - 1 ? 2 : 0;
            DrawRect(new Rect2(x, 2, sw - gap, h), _colors[i]);
            DrawRect(new Rect2(x, 2, sw - gap, 2), _colors[i].Lightened(.22f));
            x += sw;
        }
    }
}

/// <summary>Segmented pixel progress bar: 6px cells with 2px gaps in a sunken frame; cells light up one by one.</summary>
public partial class Progress : Control
{
    const int Cell = 6, Gap = 2;
    float _value;
    public float Value { get => _value; set { value = Mathf.Clamp(value, 0, 1); if (value == _value) return; _value = value; QueueRedraw(); } }

    public Progress(int height = 12)
    {
        CustomMinimumSize = new Vector2(0, height);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Pal.Ln);
        DrawRect(new Rect2(2, 2, Size.X - 4, Size.Y - 4), Pal.Well);
        int cells = (int)((Size.X - 6 + Gap) / (Cell + Gap));
        int lit = Mathf.RoundToInt(cells * _value);
        for (int i = 0; i < cells; i++)
        {
            var r = new Rect2(4 + i * (Cell + Gap), 4, Cell, Size.Y - 8);
            DrawRect(r, i < lit ? Pal.Ac : Pal.Surface);
        }
    }
}

public enum LineStyle { Solid, Dotted, Dashed }

/// <summary>2px line that fills the rest of a row: solid separator, dotted heading rule, dashed divider.</summary>
public partial class HairLine : Control
{
    readonly Color _color;
    readonly LineStyle _style;
    public HairLine(Color c, LineStyle style = LineStyle.Solid)
    {
        _color = c; _style = style;
        CustomMinimumSize = new Vector2(12, 2);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (_style == LineStyle.Solid) { DrawRect(new Rect2(0, 0, Size.X, 2), _color); return; }
        int step = _style == LineStyle.Dashed ? 8 : 4, len = _style == LineStyle.Dashed ? 4 : 2;
        for (float x = 0; x < Size.X; x += step) DrawRect(new Rect2(x, 0, Mathf.Min(len, Size.X - x), 2), _color);
    }
}
