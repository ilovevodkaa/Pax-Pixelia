using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// #toast — white slip at the top centre with a graphite icon block (crosshair while picking a scout target,
/// red block for refusals). Drops in 6px and fades; hides after its duration.
/// </summary>
public partial class Toast : PanelContainer
{
    const int MaxWidth = 580, Top = TopBar.Height + 14;
    readonly Label _text;
    readonly PanelContainer _block;
    readonly TextureRect _icon;
    readonly Box _graphite = new Box().Fill(Pal.G1, Pal.G2).Radius(3, 0, 0, 3);
    readonly Box _red = new Box().Fill(Pal.Red1, Pal.Red2).Radius(3, 0, 0, 3);
    double _left, _age;
    public ToastKind Kind { get; private set; }

    public Toast()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;   // nothing to click: a pick toast must not hide the provinces under it
        AddThemeStyleboxOverride("panel", St.Card(St.R).Pad(1));
        _icon = Ui.Icon("info-circle", 18, Colors.White);
        _icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _block = Ui.Panel(_graphite, _icon).MinSize(42, 40);
        _text = Ui.Text("", "Strong");
        AddChild(Ui.HBox(0, _block, Ui.Margin(_text, 14, 10, 18, 10)));
    }

    public void Display(string text, float seconds, ToastKind kind)
    {
        Kind = kind;
        _text.Text = text;
        float natural = UiFonts.Fu500.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X;
        int maxText = MaxWidth - 42 - 14 - 18 - 2;
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
        _icon.Texture = Icons.Get(kind switch { ToastKind.Pick => "crosshair", ToastKind.Error => "alert-triangle", _ => "info-circle" }, 18);
        _block.AddThemeStyleboxOverride("panel", kind == ToastKind.Error ? _red : _graphite);
        _left = seconds;
        if (!Visible) _age = 0;
        Visible = true;
        Size = Vector2.Zero;
    }

    public void HideNow() => Visible = false;

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
        Position = new Vector2(Mathf.Round((vw - s.X) / 2), Mathf.Round(Top - 6 * (1 - e)));
        Modulate = new Color(1, 1, 1, e);
    }
}
