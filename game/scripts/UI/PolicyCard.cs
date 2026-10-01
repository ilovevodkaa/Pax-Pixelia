using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// «Политика»: a card dropping from the bank button of the top bar, like the leaderboard. Three parts: the budget per
/// cycle line by line, the administration (provinces against the limit, the price of overextension) and the edicts —
/// each with its effect, its cost and a button to issue or repeal it.
/// </summary>
public partial class PolicyCard : Control
{
    const int CardWidth = 360;
    readonly PanelContainer _card;
    readonly VBoxContainer _body = Ui.VBox(0);
    readonly Leaderboard.Notch _notch = new();

    public PolicyCard()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var title = Ui.Text("Политика", "LeadTitle");
        title.Uppercase = true;
        var head = Ui.Panel(St.Header(36).Pad(14, 11, 14, 10), Ui.HBox(8, Ui.Icon("building-bank", 1, Pal.Ac), title, Ui.Expand()));
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _card = Ui.Panel(St.Card().Pad(2), Ui.VBox(0, head, Ui.Margin(_body, 14, 8, 14, 12)), MouseFilterEnum.Stop);
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

    /// <summary>Drop under the button, the notch at its centre.</summary>
    public void Place(Rect2 button, Vector2 screen)
    {
        float lx = Mathf.Max(8, Mathf.Min(screen.X - CardWidth - 10, button.Position.X - 160));
        Position = new Vector2(Mathf.Round(lx), TopBar.Height + 12);
        _notch.Position = new Vector2(Mathf.Round(button.Position.X + button.Size.X / 2 - lx) - Leaderboard.Notch.W / 2, -Leaderboard.Notch.H + 2);
        _card.ResetSize();
    }

    public void Refresh()
    {
        if (!Visible || !Game.I.IsReady) return;
        Ui.Clear(_body);
        var g = Game.I;
        var b = g.BudgetLines;
        var (prov, limit, over) = g.Admin;

        _body.AddChild(Caption("Бюджет за цикл"));
        _body.AddChild(Line("Налоги", Fmt.Signed(b.Taxes / 100.0, 1), Pal.Ok, "Со всех провинций: люди, рынки, столица, золотые жилы"));
        _body.AddChild(Line("Постройки", Fmt.Signed(-b.Buildings / 100.0, 1), Pal.Bad, $"Каждая постройка стоит {Rules.UpkeepPerBuilding / 100.0:0.00} золота за цикл"));
        _body.AddChild(Line("Управление", Fmt.Signed(-b.Admin / 100.0, 1), over > 0 ? Pal.Bad : Pal.Mu,
            $"{Policy.AdminPerProvince / 100.0:0.00} за каждую провинцию и ещё {Policy.AdminPerOver / 100.0:0.00} за каждую сверх предела"));
        _body.AddChild(Line("Указы", Fmt.Signed(-b.Edicts / 100.0, 1), b.Edicts > 0 ? Pal.Bad : Pal.Mu, "Доля налогов, которую забирают действующие указы"));
        if (b.Pacts > 0) _body.AddChild(Line("Договоры", Fmt.Signed(b.Pacts / 100.0, 1), Pal.Ok, $"Торговля с друзьями: +{Diplomacy.PactTaxPermille / 10}% налогов за каждый договор"));
        if (b.TributeIn > 0) _body.AddChild(Line("Дань нам", Fmt.Signed(b.TributeIn / 100.0, 1), Pal.Ok, $"Слабые соседи платят {Diplomacy.TributePct}% своих налогов"));
        if (b.TributeOut > 0) _body.AddChild(Line("Наша дань", Fmt.Signed(-b.TributeOut / 100.0, 1), Pal.Bad, $"{Diplomacy.TributePct}% налогов уходит сильному соседу"));
        _body.AddChild(Line("Итого", Fmt.Signed(b.Net / 100.0, 1), b.Net >= 0 ? Pal.Ok : Pal.Bad, null, true));

        _body.AddChild(Ui.Gap(0, 10));
        _body.AddChild(Caption("Управление"));
        var adminRow = Line("Провинции", $"{prov} из {limit}", over > 0 ? Pal.Bad : Pal.Hi, null);
        adminRow.Tip(t => t.Title("Предел управления")
            .Line("Сколько провинций держава удерживает без надрыва")
            .Kv("Основа", "16").Kv("Эпоха", $"+{4 * g.State.Nat[g.Viewer].Era}")
            .Kv("Города", $"+{4 * System.Math.Max(1, Policy.Size(g.State, g.Viewer).cities)}")
            .Kv("Знания", $"+{Techs.Sum(g.State.Nat[g.Viewer], TechFx.AdminLimit)}")
            .Mu("Предел растут эпохи, новые города и знания: Совет старейшин, Закон вождя, Письменность, Первые города"));
        _body.AddChild(adminRow);
        _body.AddChild(new ShareBar(limit > 0 ? (float)prov / limit : 0));
        _body.AddChild(Kit.Para(over > 0
            ? $"Перерасширение {over}%: довольство {Policy.OverMood(over)}, новые земли дороже на {over}%, каждая лишняя провинция стоит в пять раз больше"
            : prov >= limit - 2 ? "Держава у предела: дальше начнётся перерасширение" : "Держава управляется без надрыва", true, UiFonts.Small));

        _body.AddChild(Ui.Gap(0, 10));
        int slots = g.EdictSlots, active = g.EdictsActive;
        _body.AddChild(Caption($"Указы · {active} из {slots}"));
        for (int e = 0; e < Policy.Count; e++) _body.AddChild(EdictRow(e));
        _body.AddChild(Kit.Para("Новые места для указов дают Древний мир, Средневековье и Индустриальная эпоха", true, UiFonts.Small));
        _card.ResetSize();
    }

    static Control Caption(string text) => Ui.Margin(Ui.Cap(text), 0, 4, 0, 4);

    static Control Line(string name, string value, Color c, string tip, bool strong = false)
    {
        var row = Ui.Margin(Ui.HBox(8, Ui.Text(name, strong ? "Semi" : null).Grow(), Ui.Text(value, "Semi").Colored(c)), 0, 1, 0, 1);
        row.MouseFilter = MouseFilterEnum.Pass;
        if (tip != null) row.Tip(name, null, tip);
        return row;
    }

    Control EdictRow(int e)
    {
        var d = Policy.Edicts[e];
        bool on = Game.I.EdictOn(e);
        string line = d.CostPct > 0 ? $"{d.Effect} · −{d.CostPct}% налогов" : $"{d.Effect} · без золота";
        var text = Ui.VBox(1, Ui.Text(d.Name, on ? "Semi" : null), Ui.Text(line, "SmallMu", true));
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var btn = Ui.Button(on ? "Отменить" : "Издать", null, on ? "Ghost" : "Pri", () => Game.I.SetEdict(e, !on), 1, 28);
        btn.CustomMinimumSize = new Vector2(92, 28);
        string why = Game.I.EdictProblem(e, !on);
        Ui.Enable(btn, why == null);
        var box = new Box().Border(Pal.Ln, 0, 0, 0, 2).Pad(8, 7, 0, 8);
        if (on) box.Fill(Pal.Surface).AccentLeft(Pal.Ac, 4);
        var row = Ui.Panel(box, Ui.HBox(10, Ui.Icon(d.Icon, 1, on ? Pal.Ac : Pal.Mu).Center(), text, btn.Center()), MouseFilterEnum.Pass);
        row.Tip(t =>
        {
            var pole = Character.Pole(d.CharScale, d.CharRight ? 1 : -1);
            t.Title(d.Name).Line(d.Effect).Kv("Цена", d.CostPct > 0 ? $"{d.CostPct}% налогов каждый цикл" : $"довольство {d.Mood} во всех провинциях")
             .Kv("Характер", $"народ становится {pole.Adj}");
            if (why != null) t.Line(why);
            t.Mu(d.Lore);
        });
        return row;
    }

    /// <summary>Provinces against the limit: a 4px bar, light up to the limit, the warning colour beyond it.</summary>
    sealed partial class ShareBar : Control
    {
        readonly float _share;
        public ShareBar() { }
        public ShareBar(float share) { _share = share; CustomMinimumSize = new Vector2(0, 10); MouseFilter = MouseFilterEnum.Ignore; }

        public override void _Draw()
        {
            float w = Size.X, y = 3;
            DrawRect(new Rect2(0, y, w, 4), Pal.Ln);
            float within = Mathf.Min(1, _share), beyond = Mathf.Clamp(_share - 1, 0, 1);
            DrawRect(new Rect2(0, y, Mathf.Round(w * within), 4), Pal.Hi);
            if (beyond > 0) DrawRect(new Rect2(Mathf.Round(w * (1 - beyond)), y, Mathf.Round(w * beyond), 4), Pal.Bad);
        }
    }
}
