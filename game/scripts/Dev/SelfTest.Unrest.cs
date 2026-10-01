using System.Linq;
using System.Threading.Tasks;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>Unrest through the real UI: a rising province shows «Мятеж» and the time to secession in its panel, and
/// «Раздать хлеб» calms it through a command.</summary>
public partial class SelfTest
{
    async Task UnrestFlow()
    {
        var s = G.State;
        int p = First(q => s.Owner[q] == GameState.LocalPlayer && s.CapitalOf[q] < 0);
        if (p < 0) { Fail("unrest", "no own province"); return; }
        G.SetPaused(true);
        s.Mood[p] = 10; s.Unrest[p] = 20;
        G.Issue(Cmd.CheatGold(G.Viewer, 1000));
        G.Select(p);
        await Frames(4);
        Check("unrest: the panel shows the rising and its countdown", FindText(Hud.Panel, "Мятеж") && FindText(Hud.Panel, "до отделения"));
        var bread = AllButtons(Hud.Panel).FirstOrDefault(b => Caption(b).StartsWith("Раздать хлеб"));
        Check("unrest: «Раздать хлеб» is offered", bread != null && !bread.Disabled, bread == null ? "no button" : Caption(bread));
        await Shot("unrest");
        int journal = G.Journal.Count;
        bread?.EmitSignal(Godot.BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("unrest: bread calms it through a command", s.Mood[p] >= 10 + Unrest.ReliefMood && G.Journal.Count > journal, $"mood {s.Mood[p]}");
        s.Mood[p] = 60; s.Unrest[p] = 0;
        G.Select(-1);
        await Frames(2);
    }
}
