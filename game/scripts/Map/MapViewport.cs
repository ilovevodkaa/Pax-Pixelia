using System;
using Godot;

namespace PaxPixelia.Map;

/// <summary>World → screen mapping for this frame (written by MapCamera, read by the map layers and overlays).</summary>
internal struct MapViewport
{
    public float Zoom;       // screen px per world px; animates between zoom levels
    public int Level;        // the integer level being shown / approached — sizes sprites and picks label tiers
    public Vector2 Origin;   // screen position of world (0, 0)
    public Vector2 Screen;   // viewport size, px
    public int W, H;         // world size

    public readonly float WZ => W * Zoom;
    public readonly float ScreenY(float wy) => wy * Zoom + Origin.Y;

    /// <summary>Leftmost screen x (≥ −margin) of world x; further copies follow every WZ px (horizontal wrap).</summary>
    public readonly float FirstX(float wx, float margin)
    {
        float s = wx * Zoom + Origin.X, wz = WZ;
        return s - MathF.Floor((s + margin) / wz) * wz;
    }

    public readonly Vector2 ToWorld(Vector2 screen) => (screen - Origin) / Zoom;
}
