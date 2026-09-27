using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// A setting row (MAIN_MENU.md §2.3): «Подпись ........ ‹ значение › ». The row itself takes focus; ←/→ change the
/// value with auto-repeat (0.35 s, then every 0.08 s) and stop at the ends with a low tick; clicking ‹ › does the
/// same. Focus looks like hover (SurfaceHover fill + 2 px AccentLight frame 4 px out). <see cref="SetLocked"/>
/// shows a lock and mutes the arrows; the reason goes to <see cref="Hint"/>.
/// </summary>
public partial class PxOptionRow : HBoxContainer
{
    const float RepeatDelay = .35f, RepeatEvery = .08f;

    public event Action<int> Changed;
    /// <summary>Help text for the hint line under the options column (the lock reason while locked).</summary>
    public string Hint { get => LockReason ?? _hint; set => _hint = value; }
    public string LockReason { get; private set; }
    public bool Locked => LockReason != null;
    public int Index { get; private set; }
    public IReadOnlyList<string> Values { get; private set; }

    readonly Label _label, _value, _left, _right;
    readonly TextureRect _lock;
    string _hint;
    bool _hover;
    int _held;          // −1 / +1 while ←/→ is held
    double _repeatIn;

    public PxOptionRow(string label, IReadOnlyList<string> values, int index = 0)
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, 40);
        AddThemeConstantOverride("separation", 10);

        AddChild(new Control { CustomMinimumSize = new Vector2(2, 0), MouseFilter = MouseFilterEnum.Ignore });
        _label = PixelKit.Label(label, 18, PixelKit.Text);
        _label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _label.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_label);
        _lock = PxIcons.Make(PxIcon.Lock, 1, PixelKit.TextDim);
        _lock.Visible = false;
        AddChild(_lock);
        _left = Arrow("‹", -1);
        AddChild(_left);
        _value = PixelKit.Label("", 18, PixelKit.AccentLight, HorizontalAlignment.Center);
        _value.CustomMinimumSize = new Vector2(220, 0);
        _value.MouseFilter = MouseFilterEnum.Ignore;
        _value.ClipText = true;
        AddChild(_value);
        _right = Arrow("›", 1);
        AddChild(_right);
        AddChild(new Control { CustomMinimumSize = new Vector2(2, 0), MouseFilter = MouseFilterEnum.Ignore });

        MouseEntered += () => { _hover = true; if (FocusMode != FocusModeEnum.None) GrabFocus(); QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
        FocusEntered += Refresh;
        FocusExited += () => { _held = 0; Refresh(); };
        SetValues(values, index);
    }

    public void SetValues(IReadOnlyList<string> values, int index)
    {
        Values = values;
        Index = Math.Clamp(index, 0, Math.Max(0, values.Count - 1));
        Refresh();
    }

    /// <summary>Set the value without raising <see cref="Changed"/> (e.g. restoring saved settings).</summary>
    public void SetIndex(int index)
    {
        Index = Math.Clamp(index, 0, Math.Max(0, Values.Count - 1));
        Refresh();
    }

    /// <summary>Lock the row with a reason shown in the hint line; null unlocks.</summary>
    public void SetLocked(string reason)
    {
        LockReason = reason;
        Refresh();
    }

    public void Step(int dir)
    {
        if (Locked) { PixelKit.Sfx?.Invoke("error", 1f); PixelKit.Shake(_value, 4); return; }
        int next = Index + dir;
        if (next < 0 || next >= Values.Count) { PixelKit.Sfx?.Invoke("tick", .8f); return; }
        Index = next;
        Refresh();
        PixelKit.Bump(_value, 1.06f, .14f);
        PixelKit.Sfx?.Invoke("tick", 1f);
        Changed?.Invoke(Index);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventKey { Echo: true }) { AcceptEvent(); return; }   // auto-repeat is ours, not the OS's
        int dir = e.IsActionPressed("ui_left") ? -1 : e.IsActionPressed("ui_right") ? 1 : 0;
        if (dir != 0)
        {
            Step(dir);
            _held = dir;
            _repeatIn = RepeatDelay;
            AcceptEvent();
        }
        else if (e.IsActionReleased("ui_left") || e.IsActionReleased("ui_right")) _held = 0;
    }

    public override void _Process(double delta)
    {
        if (_held == 0) return;
        if (!HasFocus() || !Input.IsActionPressed(_held < 0 ? "ui_left" : "ui_right")) { _held = 0; return; }
        _repeatIn -= delta;
        if (_repeatIn > 0) return;
        _repeatIn += RepeatEvery;
        Step(_held);
    }

    public override void _Draw()
    {
        if (!HasFocus() && !_hover) return;
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, PixelKit.SurfaceHover);
        if (HasFocus()) DrawRect(r.Grow(4), PixelKit.AccentLight, false, 2);
    }

    void Refresh()
    {
        _value.Text = Values.Count > 0 ? Values[Index] : "";
        bool active = HasFocus() || _hover;
        _lock.Visible = Locked;
        _label.AddThemeColorOverride("font_color", Locked ? PixelKit.TextDim : active ? PixelKit.AccentLight : PixelKit.Text);
        _value.AddThemeColorOverride("font_color", Locked ? PixelKit.TextDim : PixelKit.AccentLight);
        var on = active ? PixelKit.AccentLight : PixelKit.TextDim;
        _left.AddThemeColorOverride("font_color", Locked || Index == 0 ? PixelKit.TextMuted : on);
        _right.AddThemeColorOverride("font_color", Locked || Index >= Values.Count - 1 ? PixelKit.TextMuted : on);
        QueueRedraw();
    }

    Label Arrow(string glyph, int dir)
    {
        var a = PixelKit.Label(glyph, 22, PixelKit.TextDim, HorizontalAlignment.Center);
        a.CustomMinimumSize = new Vector2(22, 0);
        a.MouseFilter = MouseFilterEnum.Stop;
        a.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                if (FocusMode != FocusModeEnum.None) GrabFocus();
                Step(dir);
                a.AcceptEvent();
            }
        };
        return a;
    }
}
