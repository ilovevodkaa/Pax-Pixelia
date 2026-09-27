using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// Components of the province panel, one per CSS class of the design (.chip .tag .own .grid2/.stat h4 .bl .bl.empty
/// .menu .kv .leg .acts .stale). Everything returned is mouse-transparent except interactive parts (Pass).
/// </summary>
public static class Kit
{
    public static Control Chip(string name, Color c)
    {
        var chip = Ui.Panel(St.Chip(), Ui.HBox(7, new Swatch(c, 11), Ui.Text(name, "Chip")));
        chip.CustomMinimumSize = new Vector2(0, 24);
        return chip;
    }

    public static Control Tag(string text)
    {
        var tag = Ui.Panel(St.Tag(), Ui.Cap(text));
        tag.CustomMinimumSize = new Vector2(0, 22);
        tag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return tag;
    }

    /// <summary>.own — owner chip followed by tags.</summary>
    public static Control Own(params Control[] items)
    {
        var f = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        f.AddThemeConstantOverride("h_separation", 6);
        f.AddThemeConstantOverride("v_separation", 6);
        foreach (var i in items) if (i != null) f.AddChild(i);
        return f;
    }

    /// <summary>.grid2 — ruled two-column ledger of stats.</summary>
    public static Control Grid(params (string label, Control value)[] cells)
    {
        var g = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        g.AddThemeConstantOverride("h_separation", 1);
        g.AddThemeConstantOverride("v_separation", 1);
        int n = cells.Length;
        for (int i = 0; i < n; i++)
        {
            int tl = i == 0 ? 3 : 0, tr = i == 1 ? 3 : 0, bl = i == n - 2 ? 3 : 0, br = i == n - 1 ? 3 : 0;
            var value = cells[i].value;
            value.CustomMinimumSize = new Vector2(0, 20);
            var cell = Ui.Panel(St.Cell(tl, tr, br, bl), Ui.VBox(3, Ui.Cap(cells[i].label), value));
            cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            g.AddChild(cell);
        }
        return Ui.Panel(St.GridFrame(), g);
    }

    public static Label Value(string text, Color? color = null)
    {
        var l = Ui.Text(text, "Val");
        if (color is { } c) l.Colored(c);
        return l;
    }

    /// <summary>Big value followed by a small muted unit («+12,7 в год»).</summary>
    public static Control ValueUnit(Label value, string unit)
    {
        var u = Ui.Text(unit, "SmallMu");
        u.VerticalAlignment = VerticalAlignment.Bottom;
        return Ui.HBox(4, value, Ui.Margin(u, 0, 0, 0, 1));
    }

    public static Control Fertility(float f)
    {
        int n = Mathf.RoundToInt(f * 5);
        return Ui.HBox(7, new Pips5(n), Ui.Text($"{n}/5", "SmallMu"));
    }

    public static Control Faith(int religion)
    {
        if (religion < 0) return Value("—", Pal.Mu);
        return Ui.HBox(7, new Swatch(Pal.Religion(religion)), Value(Core.Data.Religions[religion].Name));
    }

    /// <summary>h4 — small-caps heading with a hairline running to a right-aligned aside.</summary>
    public static Control H4(string title, string aside, out Label asideLabel)
    {
        asideLabel = aside != null ? Ui.Text(aside, "Aside") : null;
        return Ui.HBox(10, Ui.Text(title, "H4"), new HairLine(Pal.Ln2), asideLabel);
    }
    public static Control H4(string title, string aside = null) => H4(title, aside, out _);

    public static Label Para(string text, bool muted = true, int size = 13)
    {
        var l = Ui.Text(text, muted ? "Mu" : null, wrap: true).Spacing(4);
        l.VerticalAlignment = VerticalAlignment.Top;
        if (size != 13) l.Sized(size);
        return l;
    }

    /// <summary>.bl — white row: icon tile · text · right meta or inline button.</summary>
    public static PanelContainer Row(string icon, string text, string meta = null, Button action = null, bool mutedText = false, Label textLabel = null)
    {
        var label = textLabel ?? Ui.Text(text, mutedText ? "Mu" : "Strong");
        label.ClipText = true;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var h = Ui.HBox(10, Tile(icon, false), label);
        if (meta != null) h.AddChild(Ui.Text(meta, "SmallMu"));
        if (action != null) { Ui.SetHeight(action, 26); action.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; h.AddChild(action); }
        var row = Ui.Panel(St.Row(), h, Control.MouseFilterEnum.Pass);
        row.CustomMinimumSize = new Vector2(0, 36);
        return row;
    }

    public static PanelContainer Tile(string icon, bool dashed, Color? tint = null)
    {
        var t = Ui.Panel(St.Tile(dashed), Ui.Icon(icon, 15, tint ?? (dashed ? Pal.Mu : Pal.Tx2)));
        t.CustomMinimumSize = new Vector2(26, 26);
        t.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return t;
    }

    /// <summary>.bl.empty — dashed free building slot; darkens on hover.</summary>
    public static Control Slot(string text, Action onClick)
    {
        var icon = Ui.Icon("hammer", 15, Pal.Mu);
        var tileNormal = St.Tile(true);
        var tileHover = St.Tile(true).Border(Pal.Ac).Dashed();
        var tile = Ui.Panel(tileNormal, icon).MinSize(26, 26).Center();
        var label = Ui.Text(text, "Mu");
        var slot = new ClickPanel(St.Slot(), St.SlotHover(), Ui.HBox(10, tile, label));
        slot.CustomMinimumSize = new Vector2(0, 36);
        slot.HoverChanged += on =>
        {
            icon.SelfModulate = on ? Pal.Tx : Pal.Mu;
            label.Colored(on ? Pal.Tx : Pal.Mu);
            tile.AddThemeStyleboxOverride("panel", on ? tileHover : tileNormal);
        };
        slot.Clicked += Ui.Deferred(onClick);
        return slot;
    }

    /// <summary>.menu — sunken well with a two-column grid of small left-aligned buttons.</summary>
    public static Control Menu(IEnumerable<Button> buttons)
    {
        var g = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        g.AddThemeConstantOverride("h_separation", 4);
        g.AddThemeConstantOverride("v_separation", 4);
        foreach (var b in buttons)
        {
            b.ThemeTypeVariation = "Menu";
            if (b is TextButton tb) { tb.Align = HorizontalAlignment.Left; tb.Elastic = true; } else b.Alignment = HorizontalAlignment.Left;
            Ui.SetHeight(b, 28);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.ClipText = true;
            g.AddChild(b);
        }
        return Ui.Panel(St.Well(), g);
    }

    /// <summary>.kv — dashed-top line of «key value» pairs.</summary>
    public static Control Kv(params (string key, string value, Color? swatch)[] items)
    {
        var h = Ui.HBox(16);
        foreach (var (k, v, sw) in items)
            h.AddChild(Ui.HBox(5, Ui.Text(k, "Mu"), sw is { } c ? new Swatch(c) : null, Ui.Text(v, "Strong")));
        return Ui.VBox(0, new HairLine(Pal.Ln2, dashed: true), Ui.Gap(0, 8), h);
    }

    /// <summary>.leg — two-column legend of the class bar.</summary>
    public static Control Legend(IReadOnlyList<string> names, IReadOnlyList<Color> colors, IReadOnlyList<int> pcts)
    {
        var g = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        g.AddThemeConstantOverride("h_separation", 16);
        g.AddThemeConstantOverride("v_separation", 3);
        for (int i = 0; i < names.Count; i++)
        {
            var row = Ui.HBox(0, new Swatch(colors[i]), Ui.Gap(7, 0), Ui.Text(names[i]), Ui.Expand(), Ui.Text(pcts[i] + "%", "Mu"));
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            g.AddChild(row);
        }
        return g;
    }

    /// <summary>.acts — row of equal buttons.</summary>
    public static Control Acts(params Button[] buttons)
    {
        var h = Ui.HBox(6);
        foreach (var b in buttons) { b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; h.AddChild(b); }
        return h;
    }

    /// <summary>.stale — «Сведения устарели» chip for explored provinces that are not visible right now.</summary>
    public static Control Stale()
    {
        var chip = Ui.Panel(St.Stale(), Ui.HBox(6, Ui.Icon("history", 14, Pal.Mu), Ui.Cap("Сведения устарели")));
        chip.CustomMinimumSize = new Vector2(0, 24);
        chip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        chip.Tip("Сведения устарели", "Провинция разведана, но сейчас её никто не видит.", "Население и владелец — на момент последнего визита");
        chip.MouseFilter = Control.MouseFilterEnum.Pass;
        return chip;
    }
}

/// <summary>PanelContainer that behaves like a button: hover style swap, pointer cursor, click.</summary>
public partial class ClickPanel : PanelContainer
{
    readonly StyleBox _normal, _hover;
    public event Action Clicked;
    public event Action<bool> HoverChanged;

    public ClickPanel() : this(St.Empty(), St.Empty(), null) { }
    public ClickPanel(StyleBox normal, StyleBox hover, Control child)
    {
        _normal = normal; _hover = hover;
        MouseFilter = MouseFilterEnum.Pass;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeStyleboxOverride("panel", normal);
        if (child != null) AddChild(child);
        MouseEntered += () => { AddThemeStyleboxOverride("panel", _hover); HoverChanged?.Invoke(true); };
        MouseExited += () => { AddThemeStyleboxOverride("panel", _normal); HoverChanged?.Invoke(false); };
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb && GetRect().HasPoint(mb.Position + Position))
        {
            AcceptEvent();
            Clicked?.Invoke();
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) AcceptEvent();
    }
}

/// <summary>
/// Appends controls to a VBox emulating CSS block flow: vertical margins collapse (the gap between two elements is
/// the larger of the previous bottom and the next top margin).
/// </summary>
public sealed class Flow
{
    readonly VBoxContainer _box;
    int _pending;

    public Flow(VBoxContainer box, int initialGap = 0) { _box = box; _pending = initialGap; }

    public T Add<T>(T c, int top = 0, int bottom = 0) where T : Control
    {
        int gap = Math.Max(_pending, top);
        if (gap > 0) _box.AddChild(Ui.Gap(0, gap));
        _box.AddChild(c);
        _pending = bottom;
        return c;
    }
}
