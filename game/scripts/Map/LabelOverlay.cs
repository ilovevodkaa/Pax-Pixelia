using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Big map text redrawn with the view (few items): nation names (≤ ×4, placed by <see cref="LabelPlan"/>) and sparse
/// sea names (×1–×3; the ×½ atlas shows nation names only).
/// </summary>
internal partial class LabelOverlay : MapOverlay
{
    readonly List<Rect2> _boxes = new();
    readonly SeaLabels _seas = new();
    readonly UiBlockers _ui = new();

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Draw()
    {
        if (Map == null || !Map.HasWorld || !Game.I.IsReady) return;
        var v = Map.View;
        _boxes.Clear();
        if (v.Level <= 4) Map.Labels.Nations.Draw(this, v, Map.Labels.Get(v.Level), _boxes);
        if (v.Level is >= 1 and <= 3)
        {
            _ui.Refresh(GetTree().Root);
            _seas.Draw(this, v, _boxes, _ui);
        }
    }
}
