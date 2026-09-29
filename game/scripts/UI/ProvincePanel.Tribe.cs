using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// The nomad phase in the province panel: the camp shows the tribe (people, supplies, legends, how good the place is,
/// founding the capital with a myth, the best sites nearby, scouts); any other explored land shows how good it would be
/// for a capital and leads the tribe there.
/// </summary>
public partial class ProvincePanel
{
    void TribeBody(Flow flow, int p)
    {
        var g = Game.I; var w = g.World;
        var nat = g.Me;

        var people = Kit.Value(Fmt.Int(nat.TribePop));
        var supplies = Kit.Value($"{nat.Supplies} / {Nomads.StartSupplies}");
        flow.Add(Kit.Grid(("Люди рода", people), ("Запасы", supplies)), 0);
        var status = flow.Add(Kit.Para("", true, UiFonts.Small), 8);
        void SyncTribe()
        {
            var n = Game.I.Me;
            if (n == null || n.Camp < 0) return;
            people.Text = Fmt.Int(n.TribePop);
            supplies.Text = $"{n.Supplies} / {Nomads.StartSupplies}";
            supplies.AddThemeColorOverride("font_color", n.Supplies == 0 ? Pal.Bad : n.Supplies < 25 ? Pal.Hi : Pal.Tx);
            int arrive = Game.I.TribeArrivalSeconds, elders = Game.I.ElderSeconds;
            string walk = arrive >= 0 ? $"Род в пути: ещё ≈{arrive} с. " : "";
            status.Text = walk + (n.Supplies == 0 ? "Запасы кончились: люди уходят. " : "")
                + (elders > 0 ? $"Через {elders / 60}:{elders % 60:00} старейшины сами разведут очаг." : "Старейшины ищут место для очага.");
        }
        SyncTribe();
        _live.Add(SyncTribe);

        // legends
        int count = Nomads.LegendCount(nat);
        flow.Add(Kit.H4("Предания", $"{count} / {Nomads.MaxLegends}"), 20, 10);
        if (count == 0)
            flow.Add(Kit.Para("Предания рождаются в пути: у реки, у моря, в горах, в степи, в лесу и у чужого костра."), 0);
        for (int l = 0; l < Nomads.Legends.Length; l++)
        {
            if (!Nomads.HasLegend(nat, l)) continue;
            var d = Nomads.Legends[l];
            var row = flow.Add(Kit.Row("book", d.Name, d.Where), 0, 5);
            row.Tip(t => t.Title(d.Name).Line(d.Line).Kv("Если станет мифом", d.Myth));
        }

        // the site
        SiteSection(flow, p, here: true);

        // leading the tribe
        bool walking = nat.CampPath != null;
        var go = Ui.Button("Вести род…", "walk", "Sm", () => Game.I.BeginTribeTargeting(), 1, 30);
        go.Tip("Вести род", "Выберите место на карте. Путь — 3 с на провинцию при скорости 3, в бедных землях тают запасы.");
        if (walking)
        {
            var stop = Ui.Button("Остановиться", "x", "Sm", () => Game.I.HaltTribe(), 1, 30);
            flow.Add(Kit.Acts(go, stop), 12);
        }
        else flow.Add(Kit.Acts(go), 12);

        // best sites nearby
        var best = g.BestSites(3);
        if (best.Count > 0)
        {
            flow.Add(Kit.H4("Лучшие места рядом"), 20, 10);
            foreach (int q in best)
            {
                int qq = q;
                bool isHere = q == p;
                var act = isHere ? null : Ui.Button("Туда", null, "Sm", () => Game.I.MoveTribe(qq), 1, 26);
                var row = flow.Add(Kit.Row("map-pin", w.PName[q], isHere ? $"{g.SiteOf(q).Total} · здесь" : $"{g.SiteOf(q).Total}", act), 0, 5);
                row.Tip(t => SiteTip(t, qq));
            }
        }

        flow.Add(BuildScouts(), 20);
        RefreshScouts();
    }

    /// <summary>How good p is for a capital, and (on the camp) the founding buttons with the myth choice.</summary>
    void SiteSection(Flow flow, int p, bool here)
    {
        var g = Game.I;
        var site = g.SiteOf(p);
        flow.Add(Kit.H4("Место для столицы", $"{site.Total} / 100"), 20, 10);
        var parts = flow.Add(Kit.Para(SiteLine(site), true, UiFonts.Small), 0);
        parts.MouseFilter = Control.MouseFilterEnum.Pass;
        parts.Tip(t => SiteTip(t, p));

        string why = g.SettleProblem(p);
        if (!here)
        {
            if (why != null && why != "Столица уже основана") flow.Add(Kit.Para(why, true, UiFonts.Small), 8);
            var lead = Ui.Button("Вести род сюда", "walk", "Pri", () => Game.I.MoveTribe(p), 1, 34);
            flow.Add(lead, 10);
            return;
        }
        if (why != null) { flow.Add(Kit.Para(why, true, UiFonts.Small), 10); return; }

        var nat = g.Me;
        int legends = Nomads.LegendCount(nat);
        if (legends == 0)
        {
            var found = Ui.Button("Основать столицу здесь", "home", "Pri", () => Game.I.Settle(-1), 1, 34);
            found.Tip("Основать столицу", "Род разведёт очаг здесь. Мифа у народа не будет: преданий в пути не собрано.");
            flow.Add(found, 12);
            return;
        }
        flow.Add(Kit.Para("Основать столицу здесь. Одно из преданий станет мифом народа:", true, UiFonts.Small), 12);
        bool first = true;
        for (int l = 0; l < Nomads.Legends.Length; l++)
        {
            if (!Nomads.HasLegend(nat, l)) continue;
            int ll = l;
            var d = Nomads.Legends[l];
            var b = Ui.Button($"Миф «{d.Name}»", "home", first ? "Pri" : "Sm", () => Game.I.Settle(ll), 1, first ? 34 : 30);
            b.Tip(t => t.Title($"Основать столицу · миф «{d.Name}»").Line(d.Line).Kv("Навсегда", d.Myth)
                .Kv("Запасы → материалы", $"+{nat.Supplies / 2}"));
            flow.Add(b, 6);
            first = false;
        }
    }

    static string SiteLine(Nomads.SiteParts s) =>
        $"плодородие {s.Fertility} · река {s.River} · море {s.Coast} · разнообразие {s.Variety} · камень {s.Stone} · простор {s.Room}";

    static void SiteTip(TipCard t, int p)
    {
        var s = Game.I.SiteOf(p);
        t.Title($"{Game.I.World.PName[p]} · {s.Total} из 100")
         .Kv("Плодородие (до 40)", s.Fertility.ToString())
         .Kv("Река (15)", s.River.ToString())
         .Kv("Море (10)", s.Coast.ToString())
         .Kv("Разные земли рядом (до 20)", s.Variety.ToString())
         .Kv("Камень рядом (10)", s.Stone.ToString())
         .Kv("Никого поблизости (5)", s.Room.ToString())
         .Mu("Плодородная земля растит людей, река и море — предания, камень и лес — материалы");
    }
}
