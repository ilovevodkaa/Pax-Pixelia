using System;
using Godot;
using PaxPixelia.Core.Flags;

namespace PaxPixelia.UI.Front;

/// <summary>
/// The «ФЛАГ» tab of «Народ» (MAIN_MENU.md §3.3): a live 18×12 canvas, 15 divisions and 13 charges as thumbnails,
/// tincture rows (3 field slots + the charge; the ◆ swatch is the nation colour), charge position, the rule-of-tincture
/// chip and the shareable flag code. Hovering or focusing a thumbnail tries it on the canvas; click / Enter accepts;
/// leaving restores the accepted flag.
/// </summary>
public partial class FlagEditor : HBoxContainer
{
    FlagSpec _spec, _before;   // _before = the flag before the last accepted change («Вернуть» of the chip)
    (byte R, byte G, byte B) _nation;
    readonly int _canvasScale, _thumbScale;
    readonly TextureRect _canvas;
    readonly GridContainer _divisions, _charges;
    readonly TinctureRow[] _rows = new TinctureRow[4];
    readonly HBoxContainer _positions;
    readonly Label _code, _chipText;
    readonly Control _chip;

    /// <summary>Raised with the new accepted flag.</summary>
    public event Action<FlagSpec> Changed;

    public FlagSpec Spec => _spec;

    public FlagEditor(bool compact)
    {
        _canvasScale = compact ? 12 : 17;
        _thumbScale = compact ? 2 : 3;
        AddThemeConstantOverride("separation", compact ? 16 : 20);

        // ---- left: canvas, position, code, chip ----
        _canvas = new TextureRect
        {
            CustomMinimumSize = new Vector2(20, 14) * _canvasScale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = TextureFilterEnum.Nearest,
        };
        var canvasFrame = new PanelContainer();
        canvasFrame.AddThemeStyleboxOverride("panel", PixelKit.Padded(PixelKit.Framed(PixelKit.Ink, PixelKit.PanelBorder), 10, 10));
        canvasFrame.AddChild(_canvas);
        canvasFrame.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

        // position = 3 thumbnails of the current flag with the charge in the centre / at the hoist / in the canton
        _positions = SetupUi.Row(6, PixelKit.Label(compact ? "Место" : "Место фигуры", 16, PixelKit.TextDim));
        for (int p = 0; p < 3; p++)
        {
            byte k = (byte)p;
            _positions.AddChild(new FlagThumb(this, s => s with { Pos = k }, new Vector2(20, 14) * 2, FlagPatterns.PosName[p]));
        }

        _code = PixelKit.Label("", 18, PixelKit.AccentLight);
        _code.AddThemeFontOverride("font", PixelKit.Spaced(2));
        var copy = SetupUi.IconButton(PxIcon.Copy, "Скопировать код флага — им можно поделиться с друзьями", 36);
        copy.Pressed += () => { DisplayServer.ClipboardSet(FlagCode.Encode(_spec)); PixelKit.Sfx?.Invoke("click", 1.2f); PixelKit.Bump(_code, 1.1f); };
        var paste = PixelKit.Button("Вставить", "GhostButton");
        paste.CustomMinimumSize = new Vector2(0, 36);
        paste.TooltipText = "Вставить код флага из буфера";
        paste.Pressed += () => Paste(paste);
        var codeCaption = PixelKit.Label(compact ? "Код" : "Код флага", 16, PixelKit.TextDim);
        codeCaption.TooltipText = "Код флага: им можно поделиться с друзьями";
        codeCaption.MouseFilter = MouseFilterEnum.Pass;
        var codeRow = SetupUi.Row(10, codeCaption, _code, SetupUi.Spacer(), copy, paste);

        _chipText = PixelKit.Paragraph("", 13, PixelKit.Warn);
        _chipText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var accept = PixelKit.Button("Принять", "GhostButton");
        accept.CustomMinimumSize = new Vector2(0, 34);
        accept.Pressed += AcceptRule;
        var revert = PixelKit.Button("Вернуть", "GhostButton");
        revert.CustomMinimumSize = new Vector2(0, 34);
        revert.Pressed += () => Accept(_before);
        _chip = SetupUi.Card(SetupUi.Row(8, _chipText, accept, revert), 10, 6, new Color("1d1d1a"));
        _chip.Visible = false;
        var left = SetupUi.Column(10, canvasFrame, _positions, codeRow, _chip);
        left.CustomMinimumSize = new Vector2(20 * _canvasScale + 20, 0);
        AddChild(left);

        // ---- right: divisions, charges, tinctures ----
        _divisions = new GridContainer { Columns = 5 };
        _charges = new GridContainer { Columns = compact ? 13 : 7 };
        foreach (var g in new[] { _divisions, _charges })
        {
            g.AddThemeConstantOverride("h_separation", 6);
            g.AddThemeConstantOverride("v_separation", 6);
        }
        for (int d = 0; d < FlagPatterns.DivisionCount; d++)
        {
            byte k = (byte)d;
            _divisions.AddChild(new FlagThumb(this, s => s with { Division = k }, new Vector2(20, 14) * _thumbScale, FlagPatterns.DivisionName[d]));
        }
        for (int c = 0; c <= FlagPatterns.ChargeCount; c++)
        {
            byte k = (byte)c;
            _charges.AddChild(new ChargeThumb(this, k, compact ? 28 : 36));
        }
        var rows = SetupUi.Column(4);
        string[] names = { "Поле 1", "Поле 2", "Поле 3", "Фигура" };
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            _rows[i] = new TinctureRow(names[i], compact ? 22 : 24, t => Accept(With(_spec, slot, t)));
            rows.AddChild(_rows[i]);
        }
        var right = SetupUi.Column(compact ? 5 : 8,
            SetupUi.Section("Деление поля"), _divisions,
            SetupUi.Section("Фигура"), _charges,
            SetupUi.Section("Цвета"), rows);
        AddChild(right);
    }

    public void SetFlag(FlagSpec spec, (byte R, byte G, byte B) nation)
    {
        _spec = _before = spec.Sanitized();
        _nation = nation;
        Refresh();
    }

    /// <summary>The nation colour changed: every tincture ◆ and thumbnail repaints.</summary>
    public void SetNation((byte R, byte G, byte B) nation) { _nation = nation; Refresh(); }

    static FlagSpec With(FlagSpec s, int slot, byte t) => slot switch
    {
        0 => s with { T1 = t }, 1 => s with { T2 = t }, 2 => s with { T3 = t }, _ => s with { ChargeTinct = t },
    };

    internal (byte R, byte G, byte B) Nation => _nation;

    /// <summary>Try-on: the canvas shows <paramref name="s"/> until <see cref="EndTry"/>.</summary>
    internal void TryOn(FlagSpec s) => _canvas.Texture = FlagTextures.Get(s, _nation);
    internal void EndTry() => _canvas.Texture = FlagTextures.Get(_spec, _nation);

    internal void Accept(FlagSpec s)
    {
        s = s.Sanitized();
        if (s == _spec) { EndTry(); return; }
        _before = _spec;
        _spec = s;
        PixelKit.Sfx?.Invoke("tick", 1f);
        PixelKit.Bump(_canvas, 1.03f, .14f);
        Refresh();
        Changed?.Invoke(_spec);
    }

    void AcceptRule()
    {
        var effective = FlagRender.EffectiveChargeTincture(_spec, _nation);
        Accept(_spec with { ChargeTinct = effective });
    }

    void Paste(Control from)
    {
        if (FlagCode.TryDecode(DisplayServer.ClipboardGet(), out var s)) { Accept(s); PixelKit.Sfx?.Invoke("confirm", 1f); return; }
        PixelKit.Sfx?.Invoke("error", 1f);
        PixelKit.Shake(from, 6);
    }

    void Refresh()
    {
        EndTry();
        foreach (var n in _divisions.GetChildren()) ((FlagThumb)n).Refresh(_spec);
        foreach (var n in _charges.GetChildren()) ((ChargeThumb)n).Refresh(_spec);
        int used = FlagPatterns.SlotsUsed(_spec.Division);
        _rows[0].Set(_spec.T1, _nation, true);
        _rows[1].Set(_spec.T2, _nation, used >= 2);
        _rows[2].Set(_spec.T3, _nation, used >= 3);
        _rows[3].Set(_spec.ChargeTinct, _nation, _spec.Charge > 0);
        foreach (var n in _positions.GetChildren()) if (n is FlagThumb t) { t.Refresh(_spec); t.SetEnabled(_spec.Charge > 0); }
        _code.Text = FlagCode.Encode(_spec);
        bool recolour = FlagRender.ChargeRecoloured(_spec, _nation);
        _chip.Visible = recolour;
        if (recolour)
        {
            var t = FlagRender.EffectiveChargeTincture(_spec, _nation);
            _chipText.Text = $"Фигура сливается с полем — на флаге она станет {(t == FlagPatterns.Argent ? "серебряной" : "чёрной")}.";
        }
    }

    // ================================================================ parts

    /// <summary>A division thumbnail: the current flag with this division; hover / focus tries it on.</summary>
    sealed partial class FlagThumb : Button
    {
        readonly FlagEditor _ed;
        readonly Func<FlagSpec, FlagSpec> _apply;
        FlagSpec _shown;
        bool _selected;

        public FlagThumb(FlagEditor ed, Func<FlagSpec, FlagSpec> apply, Vector2 size, string tip)
        {
            _ed = ed; _apply = apply;
            CustomMinimumSize = size + new Vector2(6, 6);
            TooltipText = tip;
            FocusMode = FocusModeEnum.All;
            Flat = true;
            TextureFilter = TextureFilterEnum.Nearest;
            var empty = new StyleBoxEmpty();
            foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" }) AddThemeStyleboxOverride(st, empty);
            MouseEntered += () => { if (!Disabled) GrabFocus(); };
            FocusEntered += () => { if (!Disabled) _ed.TryOn(_apply(_ed.Spec)); QueueRedraw(); };
            FocusExited += () => { _ed.EndTry(); QueueRedraw(); };
            Pressed += () => _ed.Accept(_apply(_ed.Spec));
        }

        public void Refresh(FlagSpec current)
        {
            _shown = _apply(current);
            _selected = _shown == current;
            QueueRedraw();
        }

        public void SetEnabled(bool on)
        {
            Disabled = !on;
            FocusMode = on ? FocusModeEnum.All : FocusModeEnum.None;
            Modulate = on ? Colors.White : new Color(1, 1, 1, .3f);
        }

        public override void _Draw()
        {
            var r = new Rect2(3, 3, Size.X - 6, Size.Y - 6);
            DrawRect(new Rect2(r.Position + new Vector2(2, 2), r.Size), PixelKit.Shadow);
            DrawTextureRect(FlagTextures.Get(_shown, _ed.Nation), r, false);
            if (_selected || HasFocus()) DrawRect(new Rect2(0, 0, Size.X, Size.Y), _selected ? PixelKit.AccentLight : PixelKit.Secondary, false, 2);
        }
    }

    /// <summary>A charge thumbnail: the charge's 7×7 mask ×4 on the field colour (or «нет»).</summary>
    sealed partial class ChargeThumb : Button
    {
        readonly FlagEditor _ed;
        readonly byte _charge;
        FlagSpec _current;

        public ChargeThumb(FlagEditor ed, byte charge, int size)
        {
            _ed = ed; _charge = charge;
            CustomMinimumSize = new Vector2(size, size);
            TooltipText = FlagPatterns.ChargeName[charge];
            FocusMode = FocusModeEnum.All;
            Flat = true;
            var empty = new StyleBoxEmpty();
            foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" }) AddThemeStyleboxOverride(st, empty);
            MouseEntered += () => GrabFocus();
            FocusEntered += () => { _ed.TryOn(Apply(_ed.Spec)); QueueRedraw(); };
            FocusExited += () => { _ed.EndTry(); QueueRedraw(); };
            Pressed += () => _ed.Accept(Apply(_ed.Spec));
        }

        FlagSpec Apply(FlagSpec s) => s with { Charge = _charge };

        public void Refresh(FlagSpec current) { _current = current; QueueRedraw(); }

        public override void _Draw()
        {
            var n = _ed.Nation;
            var field = FlagPatterns.Rgb(_current.T1, n);
            var box = new Rect2(3, 3, Size.X - 6, Size.Y - 6);
            DrawRect(new Rect2(box.Position + new Vector2(2, 2), box.Size), PixelKit.Shadow);
            DrawRect(box, Color.Color8(field.R, field.G, field.B));
            if (_charge == 0)
            {
                // «нет»: a diagonal slash in Ink
                for (int i = 0; i < (int)box.Size.X - 8; i += 2) DrawRect(new Rect2(box.Position.X + 4 + i, box.Position.Y + box.Size.Y - 6 - i * (box.Size.Y - 8) / (box.Size.X - 8), 2, 2), PixelKit.Ink);
            }
            else
            {
                var probe = _current with { Charge = _charge, Pos = 0, Division = 0 };
                var c = FlagPatterns.Rgb(FlagRender.EffectiveChargeTincture(probe, n), n);
                var mask = FlagPatterns.Mask(_charge);
                int px = Math.Max(2, (int)(box.Size.X - 8) / 7);
                var o = box.Position + (box.Size - new Vector2(mask[0].Length, mask.Length) * px) / 2;
                o = o.Floor();
                for (int y = 0; y < mask.Length; y++)
                    for (int x = 0; x < mask[y].Length; x++)
                        if (mask[y][x] == '#') DrawRect(new Rect2(o.X + x * px, o.Y + y * px, px, px), Color.Color8(c.R, c.G, c.B));
            }
            bool selected = _current.Charge == _charge;
            if (selected || HasFocus()) DrawRect(new Rect2(0, 0, Size.X, Size.Y), selected ? PixelKit.AccentLight : PixelKit.Secondary, false, 2);
        }
    }
}

/// <summary>«Поле 1  ■■■■■■■■■◆»: 9 heraldic tinctures + the nation colour (◆), the chosen one framed.</summary>
public partial class TinctureRow : HBoxContainer
{
    readonly Label _caption;
    readonly Swatch[] _sw = new Swatch[10];
    bool _enabled = true;

    public TinctureRow(string caption, int size, Action<byte> pick)
    {
        AddThemeConstantOverride("separation", 3);
        _caption = PixelKit.Label(caption, 16, PixelKit.Text);
        _caption.CustomMinimumSize = new Vector2(76, 0);
        AddChild(_caption);
        for (int i = 0; i < 10; i++)
        {
            byte t = (byte)i;
            var s = new Swatch(size, FlagPatterns.TinctureName[i]);
            s.Pressed += () => { if (_enabled) pick(t); };
            _sw[i] = s;
            AddChild(s);
        }
    }

    public void Set(byte chosen, (byte R, byte G, byte B) nation, bool enabled)
    {
        _enabled = enabled;
        _caption.AddThemeColorOverride("font_color", enabled ? PixelKit.Text : PixelKit.TextMuted);
        for (int i = 0; i < 10; i++)
        {
            var c = FlagPatterns.Rgb((byte)i, nation);
            _sw[i].Set(Color.Color8(c.R, c.G, c.B), i == chosen, i == FlagPatterns.NationTincture, enabled);
        }
    }
}

/// <summary>A colour square button (tinctures, nation colours): framed when chosen, dimmed when not applicable.</summary>
public partial class Swatch : Button
{
    Color _color;
    bool _chosen, _diamond, _enabled = true;

    public Swatch(int size, string tip)
    {
        CustomMinimumSize = new Vector2(size, size);
        TooltipText = tip;
        FocusMode = FocusModeEnum.All;
        Flat = true;
        var empty = new StyleBoxEmpty();
        foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" }) AddThemeStyleboxOverride(st, empty);
        MouseEntered += () => { if (_enabled) GrabFocus(); };
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
    }

    public void Set(Color c, bool chosen, bool diamond, bool enabled)
    {
        _color = c; _chosen = chosen; _diamond = diamond; _enabled = enabled;
        FocusMode = enabled ? FocusModeEnum.All : FocusModeEnum.None;
        MouseDefaultCursorShape = enabled ? CursorShape.PointingHand : CursorShape.Arrow;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var box = new Rect2(3, 3, Size.X - 6, Size.Y - 6);
        var c = _enabled ? _color : _color.Lerp(PixelKit.Panel, .7f);
        DrawRect(box, PixelKit.Ink);
        DrawRect(box.Grow(-1), c);
        if (_diamond)
        {
            var m = box.GetCenter().Floor();
            var ink = c.Luminance > .55f ? PixelKit.Ink : PixelKit.AccentLight;
            DrawRect(new Rect2(m.X - 1, m.Y - 3, 2, 6), ink);
            DrawRect(new Rect2(m.X - 3, m.Y - 1, 6, 2), ink);
            DrawRect(new Rect2(m.X - 2, m.Y - 2, 4, 4), ink);
        }
        if (_chosen && _enabled) DrawRect(new Rect2(0, 0, Size.X, Size.Y), PixelKit.AccentLight, false, 2);
        else if (HasFocus()) DrawRect(new Rect2(0, 0, Size.X, Size.Y), PixelKit.Secondary, false, 2);
    }
}
