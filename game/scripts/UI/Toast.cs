using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// Slip at the top centre with an inverted icon block (crosshair while picking a scout target, a faint-red block for
/// refusals). Drops in 6px in whole-pixel steps and fades; hides after its duration. Clicks pass through to the map,
/// so a pick toast never swallows the click it asks for.
/// </summary>
public partial class Toast : PanelContainer
{
    const int MaxWidth = 580, Top = TopBar.Height + 14;
    readonly Label _text;
    readonly PanelContainer _block;
    readonly TextureRect _icon;
    readonly Box _graphite = new Box().Fill(Pal.Ac);
    readonly Box _red = new Box().Fill(Pal.BadFill).Border(Pal.Bad, 0, 0, 2, 0);
    double _left, _age;
    public ToastKind Kind { get; private set; }

    public Toast()
    {
        Visible = false;
        // Pass + accept only the wheel: clicks and motion fall through to the map (a pick toast must not hide the
        // provinces under it), while a wheel notch over the slip does not zoom the map
        MouseFilter = MouseFilterEnum.Pass;
        AddThemeStyleboxOverride("panel", new Box().Fill(Pal.A(Pal.Popup, .98f)).Border(Pal.Ln3).Shadow(4).Pad(2));
        _icon = Ui.Icon("info-circle", 2, Pal.OnAc, shadow: false);
        _icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _block = Ui.Panel(_graphite, _icon).MinSize(44, 40);
        _text = Ui.Text("", "Strong");
        AddChild(Ui.HBox(0, _block, Ui.Margin(_text, 14, 10, 18, 10)));
    }

    public void Display(string text, float seconds, ToastKind kind)
    {
        Kind = kind;
        _text.Text = text;
        float natural = UiFonts.Width(UiFonts.Medium, text, UiFonts.Body);
        int maxText = MaxWidth - 44 - 14 - 18 - 4;
        if (natural > maxText)
        {
            _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _text.CustomMinimumSize = new Vector2(maxText, 0);
            _text.Size = _text.CustomMinimumSize;
        }
        else
        {
            _text.AutowrapMode = TextServer.AutowrapMode.Off;
            _text.CustomMinimumSize = Vector2.Zero;
        }
        _icon.Texture = Icons.Get(kind switch { ToastKind.Pick => "crosshair", ToastKind.Error => "alert-triangle", _ => "info-circle" }, 2, false);
        _icon.SelfModulate = kind == ToastKind.Error ? Pal.BadText : Pal.OnAc;
        _block.AddThemeStyleboxOverride("panel", kind == ToastKind.Error ? _red : _graphite);
        _left = seconds;
        if (!Visible) _age = 0;
        Visible = true;
        Size = Vector2.Zero;
    }

    public void HideNow() => Visible = false;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight })
            AcceptEvent();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _age += delta;
        _left -= delta;
        if (_left <= 0) { Visible = false; return; }
        var s = GetCombinedMinimumSize();
        Size = s;
        float t = Mathf.Clamp((float)_age / .22f, 0, 1), e = 1 - (1 - t) * (1 - t) * (1 - t);
        float vw = GetParentAreaSize().X;
        Position = new Vector2(Mathf.Round((vw - s.X) / 2), Top - 2 * Mathf.Round(3 * (1 - e)));
        Modulate = new Color(1, 1, 1, e);
    }
}
