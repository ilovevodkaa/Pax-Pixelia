using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;

namespace PaxPixelia.UI;

/// <summary>
/// The quiet «Сохранено» slip in the bottom-right corner (F-4): a floppy and one word, up for ~2.5 s after every save
/// (Game.Saved), fading in 0.2 s and out 0.6 s. Mouse-transparent; it never covers anything the player needs.
/// </summary>
public partial class SaveIndicator : PanelContainer
{
    const double Hold = 2.4, FadeIn = .2, FadeOut = .6;
    readonly Label _text;
    double _age = -1;

    public SaveIndicator()
    {
        Name = "SaveIndicator";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        AddThemeStyleboxOverride("panel", new Box().Fill(Pal.A(Pal.Popup, .94f)).Border(Pal.Ln2).Shadow(3).Pad(10, 6));
        _text = Ui.Text("Сохранено").Colored(Pal.Mu);
        AddChild(Ui.HBox(8, Ui.Icon("device-floppy", 1, Pal.Mu, shadow: false), _text));
    }

    public override void _Ready() => Game.I.Saved += OnSaved;
    public override void _ExitTree() { if (Game.I != null) Game.I.Saved -= OnSaved; }

    void OnSaved(SaveKind kind, string path) => Show(kind == SaveKind.Auto ? "Автосохранение" : "Сохранено");

    /// <summary>Show the slip with a text (tests call it too).</summary>
    public void Show(string text)
    {
        _text.Text = text;
        _age = 0;
        Visible = true;
        Size = Vector2.Zero;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _age += delta;
        if (_age > Hold + FadeOut) { Visible = false; return; }
        var s = GetCombinedMinimumSize();
        Size = s;
        var area = GetParentAreaSize();
        Position = new Vector2(Mathf.Round(area.X - s.X - 16), Mathf.Round(area.Y - s.Y - 16));
        float a = _age < FadeIn ? (float)(_age / FadeIn) : _age > Hold ? 1f - (float)((_age - Hold) / FadeOut) : 1f;
        Modulate = new Color(1, 1, 1, Mathf.Clamp(a, 0, 1));
    }
}
