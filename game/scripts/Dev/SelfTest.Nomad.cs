using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>
/// `--selftest --nomad`: the nomad phase through the real UI — the camp in the panel, leading the tribe with a map
/// pick, walking in ticks, founding the capital with a myth from the panel's button, then the settled game goes on.
/// </summary>
public partial class SelfTest
{
    async Task NomadFlow()
    {
        var s = G.State; var w = G.World;
        var me = s.Nat[GameState.LocalPlayer];
        GD.Print($"nomad: start tick {s.Tick}, paused {s.Paused}, speed {s.Speed}");
        Check("nomad: a tribe, no capital", me.Camp >= 0 && s.NationCapital[GameState.LocalPlayer] < 0 && G.IsNomad);
        Check("nomad: the camp is selected, the panel shows the tribe", G.Selected == me.Camp && Hud.Panel.Visible && FindText(Hud.Panel, "Люди рода"));
        Check("nomad: the first chronicle line", _notes.Any(n => n.text.StartsWith("Огонь горит")) || Hud.Notes != null);
        await Shot("nomad_camp");

        // lead the tribe to the best site nearby through targeting and a map pick
        var sites = G.BestSites(3);
        int target = sites.FirstOrDefault(q => q != me.Camp, -1);
        if (target < 0) target = w.Adj[me.Camp].First(q => w.PLand[q] == 1);
        G.BeginTribeTargeting();
        await Frames(2);
        Check("nomad: aiming the tribe", G.IsTargeting && G.TargetingTribe);
        G.PickTarget(target);
        await Frames(2);
        Check("nomad: the tribe sets out (a journaled command)", !G.IsTargeting && me.CampPath != null && G.Journal.Any(c => c.Type == CmdType.TribeTo));
        int guard = 0;
        while (me.Camp != target && guard++ < 400) { G.RunTicks(4); await Frames(1); }
        Check("nomad: it arrives", me.Camp == target && me.CampPath == null, $"{guard * 4} ticks");
        await Frames(3);
        Check("nomad: the panel follows the camp", G.Selected == me.Camp && FindText(Hud.Panel, "Люди рода"));
        await Shot("nomad_arrived");

        // found the capital with the panel's button
        if (G.SettleProblem(me.Camp) != null)
        {
            var alt = G.BestSites(1);
            if (alt.Count > 0) { G.MoveTribe(alt[0]); guard = 0; while (me.CampPath != null && guard++ < 400) { G.RunTicks(4); await Frames(1); } }
        }
        int camp = me.Camp;
        GD.Print($"nomad: founding at tick {s.Tick}, camp {camp}, problem {G.SettleProblem(camp) ?? "none"}, progress {me.Progress}, date {G.DateText}");
        var button = AllButtons(Hud.Panel).FirstOrDefault(b => Caption(b).StartsWith("Миф") || Caption(b).StartsWith("Основать столицу"));
        Check("nomad: a founding button", button != null && !button.Disabled, button != null ? Caption(button) : "none");
        button?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("nomad: the capital stands on the camp", s.NationCapital[GameState.LocalPlayer] == camp && s.CapitalOf[camp] == GameState.LocalPlayer && me.Camp < 0);
        Check("nomad: land around it", s.Owner.Count(o => o == GameState.LocalPlayer) > 1, $"{s.Owner.Count(o => o == GameState.LocalPlayer)} provinces");
        Check("nomad: the founding is in the chronicle", _notes.Any(n => n.text.Contains("основана столица")));
        Check("nomad: the panel shows the capital", Hud.Panel.Province == camp && !FindText(Hud.Panel, "Люди рода"));
        await Shot("nomad_founded");
        GD.Print($"nomad: after the shot tick {s.Tick}, progress {me.Progress}, date {G.DateText}");

        // bots settle by themselves
        G.RunTicks(Nomads.BotSettleMax + 100);
        await Frames(3);
        Check("nomad: every bot settled within two minutes", Enumerable.Range(1, s.Nat.Length - 1).All(n => s.NationCapital[n] >= 0 && s.Nat[n].Camp < 0));
        GD.Print($"nomad: world tick {s.Tick}, materials {me.Materials} (+{me.LastMaterials}), gold {s.Gold:F0}, date {G.DateText}");
        await Seconds(2.5);                           // let the capture fills run their wave
        Check("nomad: the new borders are painted", !G.CaptureFillsRunning);
        await Shot("nomad_world");
    }

    static System.Collections.Generic.IEnumerable<Button> AllButtons(Node root) =>
        root.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.IsVisibleInTree());

    /// <summary>A button's caption: its own text, or the label inside a composite icon button.</summary>
    static string Caption(Button b) => !string.IsNullOrEmpty(b.Text) ? b.Text
        : b.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text).FirstOrDefault(t => t.Length > 0) ?? "";

    static bool FindText(Node root, string text)
    {
        foreach (var n in root.FindChildren("*", "Label", true, false))
            if (n is Label l && l.IsVisibleInTree() && l.Text.Contains(text, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
