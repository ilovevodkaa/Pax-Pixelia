using Godot;

namespace PaxPixelia.Map;

/// <summary>
/// One static rect three worlds wide (and tall), shaded by map.gdshader. The shader wraps x, so with the camera
/// centre kept in [0, W) every visible pixel is covered and nothing is redrawn while panning.
/// </summary>
internal partial class MapSurface : Node2D
{
    Vector2 _size;
    public Vector2 WorldSize { get => _size; set { _size = value; QueueRedraw(); } }

    public override void _Draw()
    {
        if (_size == Vector2.Zero) return;
        DrawRect(new Rect2(-_size, _size * 3), Colors.White);
    }
}
