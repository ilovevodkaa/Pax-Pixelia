using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// A row of tab toggles (MAIN_MENU.md §2.3): GhostButtons; the active one gets an 8 px AccentLight bar on the left
/// and AccentLight text. Q / E (physical keys — «Й / У» on ЙЦУКЕН) switch tabs unless a text field has focus.
/// </summary>
public partial class PxTabs : HBoxContainer
{
    public event Action<int> TabChanged;
    public int Current { get; private set; } = -1;
    public IReadOnlyList<Button> Tabs => _tabs;

    readonly List<Button> _tabs = new();
    static StyleBoxFlat _active;

    public PxTabs(params string[] titles)
    {
        AddThemeConstantOverride("separation", 8);
        foreach (var title in titles)
        {
            int index = _tabs.Count;
            var b = PixelKit.Button(title.ToUpperInvariant(), "GhostButton");
            b.CustomMinimumSize = new Vector2(0, 38);
            b.Pressed += () => Select(index);
            AddChild(b);
            _tabs.Add(b);
        }
        var keys = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        keys.AddThemeConstantOverride("separation", 4);
        AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        keys.AddChild(new KeyHint("Q"));
        keys.AddChild(PixelKit.Label("/", 13, PixelKit.TextMuted));
        keys.AddChild(new KeyHint("E"));
        keys.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddChild(keys);
        if (_tabs.Count > 0) Select(0, false);
    }

    public void Select(int index, bool notify = true)
    {
        index = Math.Clamp(index, 0, _tabs.Count - 1);
        if (index == Current) return;
        Current = index;
        for (int i = 0; i < _tabs.Count; i++) Style(_tabs[i], i == index);
        if (!notify) return;
        PixelKit.Sfx?.Invoke("tab", 1f);
        TabChanged?.Invoke(index);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || !IsVisibleInTree() || PxConfirm.IsOpen) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        int dir = k.PhysicalKeycode == Key.Q ? -1 : k.PhysicalKeycode == Key.E ? 1 : 0;
        if (dir == 0) return;
        Select((Current + dir + _tabs.Count) % _tabs.Count);
        GetViewport().SetInputAsHandled();
    }

    static void Style(Button b, bool active)
    {
        if (active)
        {
            _active ??= ActiveBox();
            foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed" }) b.AddThemeStyleboxOverride(st, _active);
            foreach (var c in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color" }) b.AddThemeColorOverride(c, PixelKit.AccentLight);
        }
        else
        {
            foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed" }) b.RemoveThemeStyleboxOverride(st);
            foreach (var c in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color" }) b.RemoveThemeColorOverride(c);
        }
    }

    static StyleBoxFlat ActiveBox()
    {
        var s = PixelKit.Box(PixelKit.Surface, PixelKit.PanelBorder);
        s.BorderWidthLeft = 8;
        s.BorderColor = PixelKit.AccentLight;
        s.ShadowColor = PixelKit.Shadow; s.ShadowSize = 1; s.ShadowOffset = new Vector2(2, 2);
        return PixelKit.Padded(s, 20, 8);
    }
}
