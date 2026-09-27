using System;
using Godot;

namespace PaxPixelia.UI;

/// <summary>Framed pixel flag of the nation plate (18×12 pixels drawn at ×2, white passe-partout, hairline frame).</summary>
public partial class FlagView : Control
{
    ImageTexture _tex;
    static readonly StyleBoxFlat Shadow = new() { BgColor = Colors.Transparent, ShadowColor = Pal.Shade(.12f), ShadowSize = 5, ShadowOffset = new Vector2(0, 2) };

    public FlagView()
    {
        CustomMinimumSize = new Vector2(44, 32);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void SetNation(Color c)
    {
        var img = Image.CreateEmpty(18, 12, false, Image.Format.Rgba8);
        img.Fill(c);
        var gold = Pal.Hex(0xf0c850);
        img.FillRect(new Rect2I(8, 3, 2, 6), gold);
        img.FillRect(new Rect2I(6, 5, 6, 2), gold);
        img.FillRect(new Rect2I(7, 4, 4, 4), gold);
        img.FillRect(new Rect2I(0, 10, 18, 2), new Color(c.R * .6f, c.G * .6f, c.B * .6f));
        _tex = ImageTexture.CreateFromImage(img);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawStyleBox(Shadow, new Rect2(0, 0, 44, 32));
        DrawRect(new Rect2(0, 0, 44, 32), Pal.Ln2);
        DrawRect(new Rect2(1, 1, 42, 30), Colors.White);
        DrawRect(new Rect2(3, 3, 38, 26), new Color(0, 0, 0, .4f));
        if (_tex != null) DrawTextureRect(_tex, new Rect2(4, 4, 36, 24), false);
    }
}

/// <summary>#pips — five ascending speed bars; click a bar to set that speed.</summary>
public partial class SpeedPips : Control
{
    int _speed = 2;
    int _hover = -1;
    public event Action<int> SpeedPicked;

    public SpeedPips()
    {
        CustomMinimumSize = new Vector2(5 * 9 + 4 * 3, 12);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += () => { _hover = -1; QueueRedraw(); };
    }

    public void SetSpeed(int s) { if (s == _speed) return; _speed = s; QueueRedraw(); }

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

    static int PipAt(float x) => Math.Clamp((int)(x / 12), 0, 4);

    public override void _Draw()
    {
        for (int i = 0; i < 5; i++)
        {
            int h = 4 + 2 * i;
            var c = i < _speed ? Pal.Tx : _hover >= 0 ? Pal.PipHover : Pal.PipOff;
            DrawRect(new Rect2(i * 12, 12 - h, 9, h), c);
        }
    }
}

/// <summary>.pips5 — five-step meter (fertility).</summary>
public partial class Pips5 : Control
{
    readonly int _n;
    public Pips5(int n)
    {
        _n = Math.Clamp(n, 0, 5);
        CustomMinimumSize = new Vector2(5 * 13 + 4 * 3, 8);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        for (int i = 0; i < 5; i++)
        {
            var r = new Rect2(i * 16, 0, 13, 8);
            DrawRect(r, i < _n ? Pal.Tx2 : Pal.IbPress);
            if (i >= _n) DrawRect(r, new Color(0, 0, 0, .05f), false, 1);
        }
    }
}

/// <summary>.bar — segmented population-class bar with white separators and rounded ends.</summary>
public partial class ClassBar : Control
{
    readonly float[] _parts;
    readonly StyleBoxFlat[] _segments;
    static readonly StyleBoxFlat Outline = MakeOutline();

    public ClassBar(float[] parts, Color[] colors)
    {
        _parts = parts;
        _segments = new StyleBoxFlat[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var sb = new StyleBoxFlat { BgColor = colors[i], AntiAliasingSize = .5f };
            if (i == 0) { sb.CornerRadiusTopLeft = 3; sb.CornerRadiusBottomLeft = 3; }
            if (i == parts.Length - 1) { sb.CornerRadiusTopRight = 3; sb.CornerRadiusBottomRight = 3; }
            _segments[i] = sb;
        }
        CustomMinimumSize = new Vector2(0, 10);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    static StyleBoxFlat MakeOutline()
    {
        var o = new StyleBoxFlat { DrawCenter = false, BorderColor = new Color(0, 0, 0, .12f), AntiAliasingSize = .5f };
        o.SetBorderWidthAll(1);
        o.SetCornerRadiusAll(3);
        o.ExpandMarginLeft = o.ExpandMarginTop = o.ExpandMarginRight = o.ExpandMarginBottom = 1;
        return o;
    }

    public override void _Draw()
    {
        float total = 0; foreach (var p in _parts) total += p;
        float x = 0, w = Size.X;
        for (int i = 0; i < _parts.Length; i++)
        {
            float sw = i == _parts.Length - 1 ? w - x : Mathf.Round(_parts[i] / total * w);
            DrawStyleBox(_segments[i], new Rect2(x, 0, sw, Size.Y));
            if (i > 0) DrawRect(new Rect2(x, 0, 1, Size.Y), new Color(1, 1, 1, .9f));
            x += sw;
        }
        DrawStyleBox(Outline, new Rect2(Vector2.Zero, Size));
    }
}

/// <summary>.prog — thin graphite progress bar.</summary>
public partial class Progress : Control
{
    float _value;
    readonly StyleBoxFlat _track, _fill;
    public float Value { get => _value; set { _value = Mathf.Clamp(value, 0, 1); QueueRedraw(); } }

    public Progress(int height = 6)
    {
        CustomMinimumSize = new Vector2(0, height);
        MouseFilter = MouseFilterEnum.Ignore;
        _track = new StyleBoxFlat { BgColor = Pal.Track, AntiAliasingSize = .5f };
        _track.SetCornerRadiusAll(height / 2);
        _fill = new StyleBoxFlat { BgColor = Pal.G2, AntiAliasingSize = .5f };
        _fill.SetCornerRadiusAll(height / 2);
    }

    public override void _Draw()
    {
        DrawStyleBox(_track, new Rect2(Vector2.Zero, Size));
        float w = Mathf.Round(Size.X * _value);
        if (w >= Size.Y)
        {
            DrawStyleBox(_fill, new Rect2(0, 0, w, Size.Y));
            // subtle left-to-right sheen of the CSS gradient (#59616a → graphite)
            DrawRect(new Rect2(Size.Y / 2, 1, Mathf.Max(0, w * .35f - Size.Y / 2), Size.Y - 2), new Color(1, 1, 1, .08f));
        }
    }
}

/// <summary>Hairline that fills the remaining width of a heading row (h4::after) or a dashed divider (.kv).</summary>
public partial class HairLine : Control
{
    readonly Color _color;
    readonly bool _dashed;
    public HairLine(Color c, bool dashed = false)
    {
        _color = c; _dashed = dashed;
        CustomMinimumSize = new Vector2(12, 1);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (!_dashed) { DrawRect(new Rect2(0, 0, Size.X, 1), _color); return; }
        for (float x = 0; x < Size.X; x += 6) DrawRect(new Rect2(x, 0, Mathf.Min(3, Size.X - x), 1), _color);
    }
}

/// <summary>Small round dot separator (.nsub .dot).</summary>
public partial class Dot : Control
{
    public Dot() { CustomMinimumSize = new Vector2(3, 3); SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw() => DrawCircle(new Vector2(1.5f, 1.5f), 1.5f, Pal.Mu2, true, -1, true);
}
