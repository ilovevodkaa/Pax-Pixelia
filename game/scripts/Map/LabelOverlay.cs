using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>Big map text redrawn with the view (few items): nation names (≤ ×4) and sparse sea names (≤ ×3).</summary>
internal partial class LabelOverlay : MapOverlay
{
    readonly List<Rect2> _boxes = new();
    readonly NationLabels _nations = new();
    readonly SeaLabels _seas = new();
    readonly UiBlockers _ui = new();
    object _forWorld;

    public override void _Ready() => TextureFilter = TextureFilterEnum.Linear;

    public void Refresh()
    {
        if (!Game.I.IsReady) return;
        _forWorld = Game.I.World;
        _nations.Refresh();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Map == null || !Map.HasWorld || !Game.I.IsReady || _forWorld != Game.I.World) return;
        var v = Map.View;
        _boxes.Clear();
        if (v.Level <= 4) _nations.Draw(this, v, _boxes);
        if (v.Level <= 3)
        {
            _ui.Refresh(GetTree().Root);
            _seas.Draw(this, v, _boxes, _ui);
        }
    }
}
