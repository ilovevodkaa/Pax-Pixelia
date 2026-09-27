using Godot;

namespace PaxPixelia.Map;

/// <summary>A world-space node that draws one mesh with its own material (e.g. river casing / river water).</summary>
internal partial class MeshLayer : Node2D
{
    Mesh _mesh;
    public Mesh Mesh { get => _mesh; set { _mesh = value; QueueRedraw(); } }
    public override void _Draw() { if (_mesh != null) DrawMesh(_mesh, null); }
}
