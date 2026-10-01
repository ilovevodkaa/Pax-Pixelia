using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// The event choice (HOI4-style popup) of the deck of fates: icon block, title, text and the options offered to the
/// player; the first is the safe one, taken on its own when the deadline runs out. Opens and closes on
/// <see cref="Game.ChoiceChanged"/>; the answer goes through <see cref="Game.ChooseOption"/> (a command).
/// </summary>
public partial class EventWindow : PanelContainer
{
    const int Width = 460;
    readonly TextureRect _icon;
    readonly Label _kicker, _title, _text, _timer;
    readonly VBoxContainer _options;
    int _event = -1;
    double _clock;

    public EventWindow()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", new Box().Fill(Pal.A(Pal.Popup, .98f)).Border(Pal.Ln3).Shadow(4).Pad(2));
        AnchorLeft = AnchorRight = .5f;
        OffsetTop = TopBar.Height + 70;
        GrowHorizontal = GrowDirection.Both;

        _icon = Ui.Icon("feather", 2, Pal.OnAc, shadow: false);
        _icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _icon.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var block = Ui.Panel(new Box().Fill(Pal.Ac).Pad(9), _icon);
        block.SizeFlagsVertical = SizeFlags.ShrinkBegin;

        _kicker = Ui.Text("СОБЫТИЕ", "Kick");
        _title = Ui.Text("", "PanelTitle");
        _text = Ui.Text("", null, wrap: true);
        _text.CustomMinimumSize = new Vector2(Width - 44 - 36, 0);
        _options = Ui.VBox(6);
        _timer = Ui.Text("", "SmallMu");
        var body = Ui.VBox(8, _kicker, _title, _text, Ui.Gap(0, 4), _options, _timer);
        AddChild(Ui.HBox(0, Ui.Margin(block, 12, 14, 0, 0), Ui.Margin(body, 16, 14, 18, 16)));
    }

    public override void _Ready()
    {
        Game.I.ChoiceChanged += Refresh;
        Game.I.WorldReady += Refresh;
    }

    public override void _ExitTree()
    {
        if (Game.I == null) return;
        Game.I.ChoiceChanged -= Refresh;
        Game.I.WorldReady -= Refresh;
    }

    public void Refresh()
    {
        var info = Game.I.PendingChoice();
        if (info == null) { Visible = false; _event = -1; return; }
        _clock = 0;
        UpdateTimer(info.SecondsLeft);
        if (info.Event == _event && Visible) return;
        _event = info.Event;
        _icon.Texture = Icons.Get(Icons.Has(info.Icon) ? info.Icon : "feather", 2, false);
        _kicker.Text = $"СОБЫТИЕ · {Game.I.DateText}".ToUpper();
        _title.Text = info.Title;
        _text.Text = info.Text;
        foreach (var c in _options.GetChildren()) c.QueueFree();
        for (int i = 0; i < info.Options.Length; i++)
        {
            var (k, text) = info.Options[i];
            var b = Ui.Button(text, skin: i == 0 ? "Pri" : null, onPress: () => Game.I.ChooseOption(k), height: 34);
            b.Alignment = HorizontalAlignment.Left;
            b.ClipText = true;
            b.CustomMinimumSize = new Vector2(Width - 44 - 36, 34);
            _options.AddChild(b);
        }
        Visible = true;
        Size = Vector2.Zero;
        PixelKit.Sfx?.Invoke("open", 1f);
    }

    void UpdateTimer(float seconds) =>
        _timer.Text = Game.I.State.Paused ? "Время стоит: решение ждёт вас" : $"Без ответа через {Mathf.CeilToInt(seconds)} с будет выбран первый вариант";

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _clock += delta;
        if (_clock < .25) return;
        _clock = 0;
        float left = Game.I.ChoiceSecondsLeft();
        if (left >= 0) UpdateTimer(left);
        else Refresh();
    }
}
