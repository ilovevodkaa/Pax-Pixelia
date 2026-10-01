using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// The end of a «Блиц недели»: the score by its parts, where the result file went (friends replay it to check it) and
/// two ways on — the menu (pause menu: main menu, a new game) or stay and look at the map, the clock standing.
/// Opens on <see cref="Game.BlitzEnded"/> and again for a loaded blitz that had ended.
/// </summary>
public partial class BlitzCard : PanelContainer
{
    const int Width = 440;
    readonly Label _kicker, _total, _file;
    readonly VBoxContainer _rows;

    public BlitzCard()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", new Box().Fill(Pal.A(Pal.Popup, .98f)).Border(Pal.Ln3).Shadow(4).Pad(2));
        AnchorLeft = AnchorRight = .5f;
        OffsetTop = TopBar.Height + 60;
        GrowHorizontal = GrowDirection.Both;

        var icon = Ui.Icon("trophy", 2, Pal.OnAc, shadow: false);
        var block = Ui.Panel(new Box().Fill(Pal.Ac).Pad(9), icon);
        block.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        _kicker = Ui.Text("БЛИЦ ОКОНЧЕН", "Kick");
        var title = Ui.Text("Время вышло", "PanelTitle");
        _rows = Ui.VBox(4);
        _total = Ui.Text("", "PanelTitle");
        _file = Ui.Text("", "SmallMu");   // two fixed lines: a wrapped label would leave the card its unwrapped height
        var menu = Ui.Button("В меню", "flag", "Pri", () => { Visible = false; PauseMenu.Open(); }, 1, 34);
        var stay = Ui.Button("Остаться на карте", null, null, () => Visible = false, 1, 34);
        var body = Ui.VBox(8, _kicker, title, Ui.Gap(0, 2), _rows, new HairLine(Pal.Ln2), Ui.HBox(8, Ui.Text("Итого", "Strong").Grow(), _total),
            _file, Ui.Gap(0, 4), Ui.HBox(8, menu, stay));
        AddChild(Ui.HBox(0, Ui.Margin(block, 12, 14, 0, 0), Ui.Margin(body, 16, 14, 18, 16)));
    }

    public override void _Ready()
    {
        Game.I.BlitzEnded += Show;
        Game.I.WorldReady += Sync;
        Sync();   // the HUD was rebuilt (a new era skin) after the end
    }

    /// <summary>A new or loaded game: open only for a blitz of this very game that has ended (a CLI fast-forward ends it
    /// inside WorldReady, before this handler runs).</summary>
    void Sync()
    {
        if (Game.I.BlitzResultCurrent) Show(); else Visible = false;
    }

    public override void _ExitTree()
    {
        if (Game.I == null) return;
        Game.I.BlitzEnded -= Show;
        Game.I.WorldReady -= Sync;
    }

    new void Show()
    {
        var r = Game.I.BlitzResult;
        if (r == null) return;
        _kicker.Text = $"{(Game.I.IsReplay ? $"ПОВТОР · {r.NationName}" : "БЛИЦ ОКОНЧЕН")} · {Blitz.WeekTitle(r.Week)}".ToUpper();
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }   // gone now: a second Show in one frame keeps the size
        var sc = r.Score;
        Row("Люди", $"{Fmt.Int(sc.People * 1000L)} чел.", sc.People);
        Row("Земли", "провинции × 10", sc.Lands);
        Row("Эпохи", "эпоха × 150", sc.Eras);
        Row("Знания", "технологии × 10", sc.Knowledge);
        Row("Города", "города × 30", sc.Cities);
        Row("Встречи", "державы × 15", sc.Contacts);
        _total.Text = Fmt.Int(sc.Total);
        _file.Text = Game.I.IsReplay
            ? Game.I.ReplayMatches ? "Повтор сошёлся с файлом до последнего такта:\nочки честные." : "Повтор разошёлся с файлом:\nдругая версия игры или файл правили."
            : Game.I.BlitzResultPath != null
            ? "Результат сохранён в папку блица. Пришлите файл друзьям:\nигра повторит вашу партию и проверит очки."
            : "Результат не удалось записать.";
        Visible = true;
        Size = Vector2.Zero;
        PixelKit.Sfx?.Invoke("open", 1f);
    }

    void Row(string name, string how, int points)
    {
        _rows.AddChild(Ui.HBox(8, Ui.Text(name, "Strong"), Ui.Text(how, "SmallMu").Grow(), Ui.Text(Fmt.Int(points), "Semi")));
    }
}
