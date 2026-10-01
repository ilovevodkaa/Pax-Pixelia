using System.Linq;
using System.Threading.Tasks;
using PaxPixelia.Sim;
using PaxPixelia.UI;

namespace PaxPixelia.Dev;

/// <summary>The era skin: a new era group dresses the HUD anew (Костёр → Глина), and the chronicle and the
/// selection survive the rebuild; a new world in the first era brings the campfire back.</summary>
public partial class SelfTest
{
    async Task SkinFlow()
    {
        Check("skin: the first era wears «Костёр»", EraSkin.Current == EraSkin.Campfire, EraSkin.Current.Name);
        var oldHud = Hud;
        int sel = G.Selected;
        int notes = Hud.Notes.Snapshot().Count;
        G.Issue(Cmd.CheatEra(G.Viewer, 1));
        await Frames(4);
        Check("skin: Древний мир wears «Глина»", EraSkin.Current == EraSkin.Clay, EraSkin.Current.Name);
        Check("skin: the HUD was rebuilt in the new skin", Hud != oldHud && Hud.IsInsideTree() && (!Godot.GodotObject.IsInstanceValid(oldHud) || !oldHud.IsInsideTree()));
        Check("skin: the selection survived", G.Selected == sel && Hud.Panel.Visible, $"{sel} → {G.Selected}");
        Check("skin: the chronicle survived", Hud.Notes.Snapshot().Count >= System.Math.Min(notes, 1), $"{notes} → {Hud.Notes.Snapshot().Count}");
        await Shot("skin_clay");
    }
}
