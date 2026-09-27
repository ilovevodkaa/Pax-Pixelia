using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Base of the immediate-mode screen-space overlays (identity transform, above the world layer, below the HUD):
/// few items, redrawn when the view changes or while something animates.
/// </summary>
internal abstract partial class MapOverlay : Node2D
{
    public MapView Map;

    protected static bool FogOn => Game.I.State.FogEnabled;

    /// <summary>Draw atlas sprite id centred at a screen point with sprite-pixel size ps (whole screen px) and its shadow.</summary>
    protected void DrawSprite(int id, int nation, float cx, float cy, int ps) => MapAtlas.Draw(this, id, nation, cx, cy, ps);
}
