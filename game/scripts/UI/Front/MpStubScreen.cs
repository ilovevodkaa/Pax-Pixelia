using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>«Сетевая игра» until M2 (MAIN_MENU.md §3.6): says when it comes and what to do meanwhile.</summary>
public partial class MpStubScreen : FrontScreen
{
    Button _newGame;

    public override string Title => "Сетевая игра";
    public override Control DefaultFocus => _newGame;

    public override void Build()
    {
        var lead = PixelKit.Label("Появится в версии «Друзья 0.1» (M2).", 18, PixelKit.Text);
        lead.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(lead);
        var card = Card();
        card.AddChild(PixelKit.Kicker("УЖЕ СЕЙЧАС", PixelKit.Secondary, 11));
        card.AddChild(Bullet("сыграйте с ботами — до 15 соперников;"));
        card.AddChild(Bullet("соберите свой народ в «Мои народы» — он перейдёт в лобби."));
        AddGap(4);
        _newGame = MakeButton("НОВАЯ ИГРА", () => Shell.Replace(FrontShell.CreateScreen("newgame")), "PrimaryButton", 200);
        Footer(new Control[] { _newGame }, new Control[] { MakeButton("Назад", () => GoBack(), "GhostButton", 140) });
    }

    static Control Bullet(string text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var dot = PxIcons.Make(PxIcon.Dot, 1, PixelKit.Accent);
        dot.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(dot);
        var l = PixelKit.Paragraph(text, 16, PixelKit.Text);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(l);
        return row;
    }
}
