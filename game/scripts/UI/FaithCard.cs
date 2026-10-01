using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// «Правитель и вера»: a card dropping from the sun button. The ruler — name, age, reign, traits with what they do — and
/// the faith: its name, the three dogma slots (adopted, open with the choices, or what opens them) and the provinces of
/// other faiths still to convert.
/// </summary>
public partial class FaithCard : Control
{
    const int CardWidth = 400;
    readonly PanelContainer _card;
    readonly VBoxContainer _body = Ui.VBox(0);
    readonly ScrollContainer _scroll;
    readonly Leaderboard.Notch _notch = new();
    long _shown = -1;

    public FaithCard()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var title = Ui.Text("Правитель и вера", "LeadTitle");
        title.Uppercase = true;
        var head = Ui.Panel(St.Header(36).Pad(14, 11, 14, 10), Ui.HBox(8, Ui.Icon("sun", 1, Pal.Ac), title, Ui.Expand()));
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(Ui.Margin(_body, 14, 8, 14, 12).Grow());
        _card = Ui.Panel(St.Card().Pad(2), Ui.VBox(0, head, _scroll), MouseFilterEnum.Stop);
        _card.CustomMinimumSize = new Vector2(CardWidth, 0);
        AddChild(_card);
        AddChild(_notch);
    }

    public bool Toggle()
    {
        Visible = !Visible;
        if (Visible) Refresh(true);
        return Visible;
    }

    public void Place(Rect2 button, Vector2 screen)
    {
        float lx = Mathf.Max(8, Mathf.Min(screen.X - CardWidth - 10, button.Position.X - 220));
        Position = new Vector2(Mathf.Round(lx), TopBar.Height + 12);
        _notch.Position = new Vector2(Mathf.Round(button.Position.X + button.Size.X / 2 - lx) - Leaderboard.Notch.W / 2, -Leaderboard.Notch.H + 2);
        if (_body.GetChildCount() > 0 && _body.GetChild(_body.GetChildCount() - 1) is Control last && last.Size.Y > 0)
            _scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(last.Position.Y + last.Size.Y + 8 + 12, screen.Y - 140 - 44));
        _card.ResetSize();
    }

    static long Signature()
    {
        var g = Game.I;
        var nat = g.State.Nat[g.Viewer];
        long h = 17;
        void Mix(long v) => h = h * 1_000_003 + v;
        Mix(nat.RulerSeed); Mix(nat.Rulers); Mix(nat.RulerTraits); Mix(g.RulerAge(g.Viewer)); Mix(nat.Dogmas); Mix(nat.Era);
        foreach (var slot in Faith.Slots) Mix(g.DogmaSlotOpen(slot) ? 1 : 0);
        Mix(g.Heathens());
        return h;
    }

    public void Refresh(bool force = false)
    {
        if (!Visible || !Game.I.IsReady) return;
        long sig = Signature();
        if (!force && sig == _shown) return;
        _shown = sig;
        var g = Game.I;
        var s = g.State;
        var nat = s.Nat[g.Viewer];
        Ui.Clear(_body);

        // ---- the ruler ----
        _body.AddChild(Ui.Margin(Ui.Cap("Правитель"), 0, 4, 0, 4));
        int age = g.RulerAge(g.Viewer), reign = g.RulerReign(g.Viewer);
        _body.AddChild(Ui.HBox(8, Ui.Icon("crown", 2, Pal.Ac).Center(),
            Ui.VBox(1, Ui.Text(g.RulerTitle(g.Viewer), "Semi"),
                Ui.Text($"{age} {Ru.Plural(age, "год", "года", "лет")} · правит {reign} {Ru.Plural(reign, "год", "года", "лет")}" + (age >= 55 ? " · годы берут своё" : ""), "SmallMu")).Grow()));
        for (int t = 0; t < Leader.Count; t++)
        {
            if (!g.RulerHas(t)) continue;
            var d = Leader.Traits[t];
            string group = d.Group switch { "upbringing" => "воспитание", "acquired" => "приобретено", _ => "нрав" };
            var row = Ui.Margin(Ui.VBox(1, Ui.HBox(8, Ui.Text(d.Name).Grow(), Ui.Text(group, "SmallMu")), Ui.Text(d.Effect, "SmallMu", true)), 0, 4, 0, 2);
            _body.AddChild(row);
        }
        _body.AddChild(Kit.Para("Правитель стареет: после 55 смерть всё ближе. Наследник приходит со своими чертами, а держава носит траур", true, UiFonts.Small));

        // ---- the faith ----
        _body.AddChild(Ui.Gap(0, 10));
        int rel = g.Nations[g.Viewer].Religion;
        _body.AddChild(Ui.Margin(Ui.Cap("Вера"), 0, 4, 0, 4));
        _body.AddChild(Ui.HBox(8, new Swatch(Pal.Religion(rel), 12).Center(), Ui.Text(Data.Religions[rel].Name, "Semi").Grow()));
        foreach (var slot in Faith.Slots) _body.AddChild(Slot(slot));
        int heathens = g.Heathens();
        if (heathens > 0)
            _body.AddChild(Kit.Para(Faith.Tolerant(nat)
                ? $"Иноверцев в державе: {heathens} {Ru.Plural(heathens, "провинция", "провинции", "провинций")}. Мы терпимы — они не ропщут"
                : $"Иноверцев в державе: {heathens} {Ru.Plural(heathens, "провинция", "провинции", "провинций")} — каждая ропщет (−10). Святилища и миссионеры обращают быстрее", true, UiFonts.Small));
    }

    Control Slot(string slot)
    {
        var g = Game.I;
        var box = Ui.VBox(3);
        box.AddChild(Ui.Margin(Ui.Text(Faith.SlotName(slot), "Semi"), 0, 6, 0, 0));
        int have = g.DogmaInSlot(slot);
        if (have >= 0)
        {
            var d = Faith.Dogmas[have];
            box.AddChild(Ui.Panel(new Box().Fill(Pal.Surface).Border(Pal.Ln).AccentLeft(Pal.Ac, 4).Pad(8, 5, 8, 6),
                Ui.VBox(1, Ui.Text(d.Name), Ui.Text(d.Effect, "SmallMu", true))));
            return box;
        }
        if (!g.DogmaSlotOpen(slot)) { box.AddChild(Ui.Text(Faith.SlotNeed(slot), "SmallMu", true)); return box; }
        box.AddChild(Ui.Text("Выберите догмат — выбор навсегда", "SmallMu").Colored(Pal.Warn));
        for (int k = 0; k < Faith.Count; k++)
        {
            var d = Faith.Dogmas[k];
            if (d.Slot != slot) continue;
            int dd = k;
            var btn = Ui.Button("Принять", null, "Pri", () => Game.I.AdoptDogma(dd), 1, 26);
            btn.CustomMinimumSize = new Vector2(88, 26);
            string why = g.DogmaProblem(k);
            Ui.Enable(btn, why == null);
            var text = Ui.VBox(1, Ui.Text(d.Name), Ui.Text(d.Effect, "SmallMu", true));
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var row = Ui.Panel(new Box().Border(Pal.Ln, 0, 0, 0, 2).Pad(0, 5, 0, 6), Ui.HBox(10, text, btn.Center()), MouseFilterEnum.Pass);
            var pole = Character.Pole(d.CharScale, d.CharRight ? 1 : -1);
            row.Tip(t => { t.Title(d.Name).Line(d.Effect).Kv("Характер", $"народ становится {pole.Adj}"); if (why != null) t.Line(why); });
            box.AddChild(row);
        }
        return box;
    }
}
