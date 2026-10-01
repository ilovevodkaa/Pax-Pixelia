using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Map;

/// <summary>
/// Ruins on the map (Sim/Archaeology.cs): a few pixels of broken columns and a fallen lintel beside the province's
/// anchor, with a dark rim — on land the player has seen (not under the clouds) and until the ruins are dug. From ×2 up.
/// </summary>
internal partial class RuinOverlay : MapOverlay
{
    // the picture in sprite pixels (x, y): a lintel over one whole and one broken column, rubble at the foot
    static readonly (int X, int Y)[] Stones =
    {
        (0, 0), (1, 0), (2, 0),
        (0, 1), (0, 2), (0, 3),
        (3, 2), (3, 3),
        (5, 3), (6, 3), (5, 2),
    };
    static readonly Color Stone = new(.80f, .76f, .66f), Shade = new(.58f, .54f, .47f), Rim = new(22 / 255f, 26 / 255f, 31 / 255f, .8f);
    int _key = int.MinValue;

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Process(double delta)
    {
        // redraw when the view, the fog or the dug ruins change: the view key is cheap
        if (!Game.I.IsReady) return;
        var v = Map.View;
        int key = HashCode.Combine(v.Origin, v.Zoom, Game.I.State.RuinsDug?.Length ?? 0, Game.I.Journal.Count, Game.I.State.Tick / 64);
        if (key != _key) { _key = key; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (Map == null || !Map.HasWorld || !Game.I.IsReady) return;
        var v = Map.View;
        if (v.Level < 2) return;
        var w = Game.I.World; var s = Game.I.State;
        int ps = Math.Max(1, Lod.UnitScale(v.Level));
        foreach (int p in Archaeology.Ruins(w))
        {
            if (Archaeology.Dug(w, s, p)) continue;
            if (FogOn && s.Fog != null && s.Fog[p] == 0) continue;
            float wx = w.PCX[p] + 3.5f, sy = v.ScreenY(w.PCY[p] + 2.5f);
            if (sy < -60 || sy > v.Screen.Y + 60) continue;
            for (float sx = v.FirstX(wx, 60); sx < v.Screen.X + 60; sx += v.WZ)
            {
                float x0 = MathF.Round(sx), y0 = MathF.Round(sy - 4 * ps);
                foreach (var (x, y) in Stones) DrawRect(new Rect2(x0 + x * ps - 1, y0 + y * ps - 1, ps + 2, ps + 2), Rim);
                foreach (var (x, y) in Stones) DrawRect(new Rect2(x0 + x * ps, y0 + y * ps, ps, ps), y == 3 || x == 5 ? Shade : Stone);
            }
        }
    }
}
