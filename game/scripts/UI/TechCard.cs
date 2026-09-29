using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// Technologies card dropping from the atom button (the full «Партитура» screen of SCREENS §4 comes with the later
/// eras). One row per technology of the current and the next era: studied, being studied (with the real time left),
/// open (click to study) or later. The header counts what the era needs before the next one can begin.
/// </summary>
public partial class TechCard : Control
{
    const int CardWidth = 340;
    readonly PanelContainer _card;
    readonly VBoxContainer _rows = Ui.VBox(0);
    readonly ScrollContainer _scroll;
    readonly Label _count = Ui.Text("", "Aside");
    readonly Label _foot = Ui.Text("", "SmallMu", wrap: true);
    readonly Leaderboard.Notch _notch = new();

    public TechCard()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var title = Ui.Text("Технологии", "LeadTitle");
        title.Uppercase = true;
        var head = Ui.Panel(St.Header(36).Pad(14, 11, 14, 10), Ui.HBox(8, Ui.Icon("atom", 1, Pal.Ac), title, Ui.Expand(), _count));
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(Ui.Margin(_rows, 0, 4, 0, 4).Grow());
        _foot.CustomMinimumSize = new Vector2(CardWidth - 28, 0);
        var foot = Ui.Margin(_foot, 14, 6, 14, 10);
        _card = Ui.Panel(St.Card().Pad(2), Ui.VBox(0, head, _scroll, foot), MouseFilterEnum.Stop);
        _card.CustomMinimumSize = new Vector2(CardWidth, 0);
        AddChild(_card);
        AddChild(_notch);
    }

    public bool Toggle()
    {
        Visible = !Visible;
        if (Visible) Refresh();
        return Visible;
    }

    /// <summary>Drop under the atom button, the notch at its centre.</summary>
    public void Place(Rect2 button, Vector2 screen)
    {
        float lx = Mathf.Max(8, Mathf.Min(screen.X - CardWidth - 10, button.Position.X - 150));
        Position = new Vector2(Mathf.Round(lx), TopBar.Height + 12);
        _notch.Position = new Vector2(Mathf.Round(button.Position.X + button.Size.X / 2 - lx) - Leaderboard.Notch.W / 2, -Leaderboard.Notch.H + 2);
        float content = _rows.GetParent<Control>().GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(content, screen.Y - 200));
        _card.ResetSize();
    }

    public void Refresh()
    {
        if (!Visible || !Game.I.IsReady) return;
        var g = Game.I;
        Ui.Clear(_rows);
        var (known, needed, total) = g.EraKnowledge;
        _count.Text = needed > 0 ? $"{g.EraName} · {known} / {needed}" : g.EraName;
        var views = g.TechViews();
        int lastEra = -1;
        foreach (var v in views)
        {
            if (v.Def.Era != lastEra)
            {
                lastEra = v.Def.Era;
                _rows.AddChild(Ui.Margin(Ui.Cap(Eras.Name(lastEra)), 14, 8, 14, 4));
            }
            _rows.AddChild(new Row(v));
        }
        if (views.Count == 0) _rows.AddChild(Ui.Margin(Ui.Text("Технологии этой эпохи ещё не придуманы. Скоро.", "SmallMu", wrap: true), 14, 8, 14, 8));

        string foot = needed > 0 && known < needed
            ? $"Для эпохи «{g.NextEraName}» нужно изучить {needed} из {total}."
            : needed > 0 ? $"Знаний для эпохи «{g.NextEraName}» достаточно: она наступит, когда наберётся наука." : "";
        if (g.TechPool > 0) foot += (foot.Length > 0 ? " " : "") + $"В запасе {g.TechPool} очков науки: они перейдут в выбранную технологию.";
        else if (g.ResearchIdle) foot += (foot.Length > 0 ? " " : "") + "Выберите, чему учиться.";
        _foot.Text = foot;
        _foot.Visible = foot.Length > 0;
    }

    /// <summary>A row: icon · name · status, the effect underneath, a 2px progress line; open rows are buttons.</summary>
    sealed partial class Row : PanelContainer
    {
        readonly Game.TechView _v;
        readonly Box _normal, _hover;

        public Row() { }
        public Row(Game.TechView v)
        {
            _v = v;
            bool open = v.State == Game.TechState.Open, studying = v.State == Game.TechState.Studying, known = v.State == Game.TechState.Known;
            _normal = new Box().Border(Pal.Ln, 0, 0, 0, 2).Pad(14, 7, 14, 10);
            if (studying) _normal.Fill(Pal.Surface).AccentLeft(Pal.Ac, 4);
            _hover = new Box().Fill(Pal.Surface).Border(Pal.Ln, 0, 0, 0, 2).Pad(14, 7, 14, 10).AccentLeft(Pal.Hi, 4);
            AddThemeStyleboxOverride("panel", _normal);
            MouseFilter = open || studying ? MouseFilterEnum.Stop : MouseFilterEnum.Pass;
            if (open) MouseDefaultCursorShape = CursorShape.PointingHand;

            string status = known ? "изучено"
                : studying ? (v.SecondsLeft < 0 ? "изучается" : v.SecondsLeft < 60 ? $"≈ {v.SecondsLeft} с" : "≈ " + Fmt.Duration((v.SecondsLeft + 59) / 60))
                : open ? "изучить"
                : "позже";
            var icon = Ui.Icon(v.Def.Icon, 1, known ? Pal.Mu : studying || open ? Pal.Ac : Pal.Ln3);
            var name = Ui.Text(v.Def.Name, known || v.State == Game.TechState.Later ? "Mu" : "Semi");
            var st = Ui.Text(status, studying ? "Semi" : "SmallMu");
            var effect = Ui.Text(v.Def.Effect, "SmallMu", wrap: true);
            effect.CustomMinimumSize = new Vector2(CardWidth - 70, 0);
            AddChild(Ui.HBox(10, icon, Ui.VBox(2, Ui.HBox(8, name.Grow(), st), effect).Grow()));

            this.Tip(t =>
            {
                t.Title(v.Def.Name).Line(v.Def.Lore).Kv("Даёт", v.Def.Effect);
                if (!known) t.Kv("Цена", $"{v.Cost} очков науки").Kv("Вложено", $"{v.Points} ({v.Points * 100 / System.Math.Max(1, v.Cost)}%)");
                if (open) t.Mu(Game.I.Researching >= 0 ? "Нажмите, чтобы переключиться: вложенное не пропадёт" : "Нажмите, чтобы начать");
                else if (v.State == Game.TechState.Later) t.Mu($"Откроется в эпоху «{Eras.Name(v.Def.Era)}»");
            });
            if (open)
            {
                MouseEntered += () => AddThemeStyleboxOverride("panel", _hover);
                MouseExited += () => AddThemeStyleboxOverride("panel", _normal);
                GuiInput += e =>
                {
                    if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    {
                        Game.I.Research(_v.Id);
                        AcceptEvent();
                    }
                };
            }
        }

        public override void _Draw()
        {
            if (_v.State is not (Game.TechState.Studying or Game.TechState.Open) || _v.Points <= 0) return;
            float x0 = 40, x1 = Size.X - 14, y = Size.Y - 5;
            DrawRect(new Rect2(x0, y, x1 - x0, 2), Pal.Ln);
            DrawRect(new Rect2(x0, y, Mathf.Round((x1 - x0) * Mathf.Clamp(_v.Points / (float)_v.Cost, 0, 1)), 2), Pal.Hi);
        }
    }
}
