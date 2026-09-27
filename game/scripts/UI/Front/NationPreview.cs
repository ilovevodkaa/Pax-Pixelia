using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Flags;
using PaxPixelia.Core.Nations;

namespace PaxPixelia.UI.Front;

/// <summary>
/// «КАК ВАС УВИДЯТ» (MAIN_MENU.md §3.3): the flag waving on its pole (4 wind frames at 6 fps), the name with the
/// title shimmer, the first-era camp sprite painted in the nation's ramp, the piece of the next world around the
/// future capital with the spaced name as on the map, and the chronicle's first line.
/// </summary>
public partial class NationPreview : PanelContainer
{
    readonly bool _compact;
    readonly TextureRect _pole, _city, _chunk, _chunkCity;
    readonly Label _name, _gov, _chronicle, _chunkName;
    readonly Control _chunkBox;
    readonly ShaderMaterial _shimmer;
    NationDesign _design;
    int _frame = -1;

    public NationPreview(bool compact, float width)
    {
        _compact = compact;
        CustomMinimumSize = new Vector2(width, 0);
        AddThemeStyleboxOverride("panel", PixelKit.Padded(PixelKit.Framed(PixelKit.Ink, PixelKit.PanelBorder), 16, 14));
        int poleScale = compact ? 4 : 6, cityScale = compact ? 4 : 6;
        _pole = Pixel(new Vector2(FlagRender.PoleW, FlagRender.PoleH) * poleScale);
        _city = Pixel(new Vector2(18, 18) * cityScale);
        var stage = SetupUi.Row(28, _pole, _city);
        stage.Alignment = BoxContainer.AlignmentMode.Center;
        foreach (var c in new Control[] { _pole, _city }) c.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        var ground = new ColorRect { Color = PixelKit.PanelBorder, CustomMinimumSize = new Vector2(0, 2) };

        _name = PixelKit.Label("", compact ? 33 : 44, PixelKit.AccentLight, HorizontalAlignment.Center);
        _name.AddThemeFontOverride("font", PixelKit.Spaced(compact ? 3 : 4));
        PixelKit.TextShadow(_name, 1.2f);
        _shimmer = Shimmer.Attach(_name, compact ? 33 : 44);
        _gov = PixelKit.Label("", 16, PixelKit.TextDim, HorizontalAlignment.Center);

        int cw = (int)width - 32, ch = compact ? 84 : 112;
        _chunk = Pixel(new Vector2(cw, ch));
        _chunkName = PixelKit.Label("", compact ? 16 : 22, PixelKit.AccentLight, HorizontalAlignment.Center);
        _chunkName.AddThemeFontOverride("font", PixelKit.Spaced(compact ? 4 : 6));
        _chunkName.AddThemeColorOverride("font_shadow_color", PixelKit.Ink);
        _chunkName.AddThemeConstantOverride("shadow_offset_x", 1);
        _chunkName.AddThemeConstantOverride("shadow_offset_y", 1);
        _chunkName.Position = new Vector2(0, ch / 2 - (compact ? 34 : 44));
        _chunkName.Size = new Vector2(cw, 24);
        _chunkCity = Pixel(new Vector2(36, 36));
        _chunkCity.Position = new Vector2(cw / 2 - 18, ch / 2 - 14);
        _chunk.AddChild(_chunkCity);
        _chunk.AddChild(_chunkName);
        _chunkBox = compact ? _chunk : SetupUi.Column(4, SetupUi.Section("У будущей столицы"), _chunk);

        _chronicle = PixelKit.Paragraph("", 16, PixelKit.TextDim);
        _chronicle.HorizontalAlignment = HorizontalAlignment.Center;

        AddChild(SetupUi.Column(compact ? 8 : 12,
            PixelKit.Kicker("КАК ВАС УВИДЯТ", PixelKit.Secondary, 11),
            SetupUi.Column(0, stage, ground),
            _name, _gov, _chunkBox, _chronicle));
    }

    static TextureRect Pixel(Vector2 size) => SetupUi.Pixel(size);

    public void Show(NationDesign d)
    {
        bool colourChanged = _design == null || _design.Rgb != d.Rgb;
        _design = d;
        _frame = -1;
        FitName(d.Name.ToUpper());
        _gov.Text = $"Вождество · {NationNames.Cultures[d.Culture % NationNames.Cultures.Length].Adjective} культура";
        _chronicle.Text = $"«Огонь горит. Род {Sim.Ru.Genitive(d.Name)} цел. Идём.»";
        _chunkName.Text = d.Name.ToUpper();
        if (colourChanged) _city.Texture = _chunkCity.Texture = CitySprite.Camp(d.Rgb);
        RefreshChunk();
    }

    /// <summary>Biggest step of the size ladder (44 → 33 → 22) at which the name fits the column.</summary>
    void FitName(string text)
    {
        _name.Text = text;
        float room = CustomMinimumSize.X - 40;
        foreach (int size in _compact ? new[] { 33, 22 } : new[] { 44, 33, 22 })
        {
            var font = _name.GetThemeFont("font");
            if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= room || size == 22)
            {
                Shimmer.SetSize(_name, _shimmer, size);
                break;
            }
        }
    }

    /// <summary>The land around the capital, when the preview world is cached (after «Новая игра» showed it).</summary>
    public void RefreshChunk()
    {
        var tex = WorldPreview.CapitalChunk((int)_chunk.CustomMinimumSize.X, (int)_chunk.CustomMinimumSize.Y);
        _chunkBox.Visible = tex != null;
        if (tex == null) return;
        _chunk.Texture = tex;
        _chunk.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_design == null) return;
        int frame = FrontClock.Reduced ? 0 : (int)(FrontClock.T * 6) % 4;   // wind: 4 frames at 6 fps
        if (frame == _frame) return;
        _frame = frame;
        _pole.Texture = FlagTextures.Pole(_design.Flag, _design.Rgb, frame);
    }
}

/// <summary>
/// The pixel title shimmer on a label (shaders/front/title_shimmer.gdshader, t from FrontClock): a stepped vertical
/// gradient over the glyph fill and a light diagonal passing in steps of one glyph pixel (snap = font size / 11).
/// </summary>
public static class Shimmer
{
    const string ShaderPath = "res://shaders/front/title_shimmer.gdshader";

    /// <summary>A material registered with FrontClock (unregister it when the label leaves the tree).</summary>
    public static ShaderMaterial Attach(Label l, int fontSize)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        l.Material = m;
        SetSize(l, m, fontSize);
        m.SetShaderParameter("period", FrontClock.Reduced ? 0f : 6f);
        l.Resized += () => Fit(l, m);
        l.TreeEntered += () => FrontClock.Register(m);
        l.TreeExiting += () => FrontClock.Unregister(m);
        return m;
    }

    public static void SetSize(Label l, ShaderMaterial m, int fontSize)
    {
        l.AddThemeFontSizeOverride("font_size", fontSize);
        m.SetShaderParameter("snap", Mathf.Max(2f, Mathf.Round(fontSize / 11f)));
        Fit(l, m);
    }

    /// <summary>The gradient spans the capital height of the font (as _update_title_gradient in Mr. President).</summary>
    static void Fit(Label l, ShaderMaterial m)
    {
        var font = l.GetThemeFont("font");
        int size = l.GetThemeFontSize("font_size");
        float ascent = font.GetAscent(size), height = font.GetHeight(size);
        float baseline = (l.Size.Y - height) * .5f + ascent, cap = ascent * .72f;
        m.SetShaderParameter("text_top", baseline - cap);
        m.SetShaderParameter("text_height", cap);
        m.SetShaderParameter("travel", Mathf.Max(300f, l.Size.X + l.Size.Y));
    }
}

/// <summary>City sprites of assets/front/city_eras_x1.png (11 eras × 16 px; row 0 = the mask): the magenta mask
/// (#FF80FF light, #FF00FF base, #800080 dark) is replaced by the nation's ramp.</summary>
public static class CitySprite
{
    const string Sheet = "res://assets/front/city_eras_x1.png";
    static Image _sheet;

    /// <summary>The first-era camp (the game starts in the Первобытная era) with a 1 px hard shadow, 18×18.</summary>
    public static ImageTexture Camp((byte R, byte G, byte B) rgb) => Era(0, rgb);

    public static ImageTexture Era(int era, (byte R, byte G, byte B) rgb)
    {
        _sheet ??= Load();
        if (_sheet == null) return null;
        var (light, main, dark) = NationRamp.From(rgb);
        var img = Image.CreateEmpty(18, 18, false, Image.Format.Rgba8);
        var shadow = new Color(0, 0, 0, .35f);
        for (int pass = 0; pass < 2; pass++)
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    var c = _sheet.GetPixel(Math.Clamp(era, 0, 10) * 16 + x, y);
                    if (c.A < .5f) continue;
                    if (pass == 0) { if (img.GetPixel(x + 2, y + 1).A == 0) img.SetPixel(x + 2, y + 1, shadow); continue; }
                    img.SetPixel(x + 1, y, Recolour(c, light, main, dark));
                }
        return ImageTexture.CreateFromImage(img);
    }

    static Color Recolour(Color c, (byte R, byte G, byte B) light, (byte R, byte G, byte B) main, (byte R, byte G, byte B) dark)
    {
        int r = c.R8, g = c.G8, b = c.B8;
        if (r == 0xFF && g == 0x80 && b == 0xFF) return Color.Color8(light.R, light.G, light.B);
        if (r == 0xFF && g == 0x00 && b == 0xFF) return Color.Color8(main.R, main.G, main.B);
        if (r == 0x80 && g == 0x00 && b == 0x80) return Color.Color8(dark.R, dark.G, dark.B);
        return c;
    }

    static Image Load()
    {
        var img = ResourceLoader.Exists(Sheet) ? GD.Load<Texture2D>(Sheet)?.GetImage() : null;
        img ??= Image.LoadFromFile(ProjectSettings.GlobalizePath(Sheet));
        if (img == null) { GD.PushWarning($"CitySprite: {Sheet} not found"); return null; }
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        return img;
    }
}
