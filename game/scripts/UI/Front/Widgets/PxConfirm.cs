using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// The shared modal dialog (MAIN_MENU.md §3.9): every dangerous action goes through it.
/// <c>await PxConfirm.Ask(parent, title, text, buttons)</c> returns the pressed button index or −1 (Esc / countdown
/// ran out). Focus starts on the safe button (<paramref name="focus"/>, default the last one); the dangerous one
/// (<paramref name="danger"/>, default the first; −1 none) is a GhostButton. With a countdown, «{0}» in the text is
/// replaced by the seconds left, and reaching zero answers −1 («Оставить этот режим? Вернём прежний через {0} с»).
/// </summary>
public partial class PxConfirm : Control
{
    public const float Width = 460;
    static int _open;
    /// <summary>A dialog is on screen (hosts skip their own Esc handling while true).</summary>
    public static bool IsOpen => _open > 0;

    readonly TaskCompletionSource<int> _result = new();
    readonly string _text;
    readonly List<Button> _buttons = new();
    readonly int _focus;
    Label _body;
    double _left;
    bool _done;

    PxConfirm(string title, string text, string[] buttons, int countdown, int focus, int danger)
    {
        _text = text;
        _left = countdown;
        _focus = focus < 0 || focus >= buttons.Length ? buttons.Length - 1 : focus;
        Theme = PixelTheme.Build();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var shade = new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f, .6f), MouseFilter = MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = FrontShell.MakePanel(24, 20);
        panel.CustomMinimumSize = new Vector2(Width, 0);
        center.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var head = PixelKit.Label(title.ToUpperInvariant(), 22, PixelKit.AccentLight);
        head.AddThemeFontOverride("font", PixelKit.Spaced(2));
        PixelKit.TextShadow(head);
        head.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(head);
        box.AddChild(new ColorRect { Color = PixelKit.Accent, CustomMinimumSize = new Vector2(40, 4), SizeFlagsHorizontal = SizeFlags.ShrinkBegin });
        _body = PixelKit.Paragraph(Format(), 16);
        _body.CustomMinimumSize = new Vector2(Width - 48, 0);
        box.AddChild(_body);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        box.AddChild(row);
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = PixelKit.Button(buttons[i], i == danger ? "GhostButton" : "");
            b.CustomMinimumSize = new Vector2(0, 42);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.Pressed += () => Close(index);
            row.AddChild(b);
            _buttons.Add(b);
        }
        Nav.Unify(row);
    }

    public static Task<int> Ask(Node parent, string title, string text, string[] buttons, int countdown = 0, int focus = -1, int danger = 0)
    {
        var dialog = new PxConfirm(title, text, buttons, countdown, focus, danger);
        parent.AddChild(dialog);
        return dialog._result.Task;
    }

    public override void _EnterTree() => _open++;
    public override void _ExitTree() { _open--; if (!_done) { _done = true; _result.TrySetResult(-1); } }

    public override void _Ready()
    {
        PixelKit.Sfx?.Invoke("open", 1f);
        TrapFocus();
        var panel = GetChild<Control>(1).GetChild<Control>(0);
        if (!FrontClock.Reduced) PixelKit.PopIn(panel, 0, .9f, .3f);
        _buttons[_focus].CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _Process(double delta)
    {
        if (_left <= 0 || _done) return;
        int before = (int)System.Math.Ceiling(_left);
        _left -= delta;
        if ((int)System.Math.Ceiling(_left) != before) _body.Text = Format();
        if (_left <= 0) Close(-1);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_done) return;
        if (e.IsActionPressed("ui_cancel")) Close(-1);
        if (e is InputEventKey or InputEventAction) GetViewport().SetInputAsHandled();   // the dialog is modal
    }

    string Format() => _text.Contains("{0}") ? string.Format(_text, (int)System.Math.Ceiling(System.Math.Max(0, _left))) : _text;

    /// <summary>Arrows and Tab cycle through the dialog's buttons only (it is modal).</summary>
    void TrapFocus()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            var b = _buttons[i];
            var prev = b.GetPathTo(_buttons[(i + _buttons.Count - 1) % _buttons.Count]);
            var next = b.GetPathTo(_buttons[(i + 1) % _buttons.Count]);
            b.FocusNeighborLeft = b.FocusPrevious = prev;
            b.FocusNeighborRight = b.FocusNext = next;
            b.FocusNeighborTop = b.FocusNeighborBottom = ".";
        }
    }

    void Close(int index)
    {
        if (_done) return;
        _done = true;
        PixelKit.Sfx?.Invoke(index < 0 ? "close" : "confirm", 1f);
        _result.TrySetResult(index);
        QueueFree();
    }
}
