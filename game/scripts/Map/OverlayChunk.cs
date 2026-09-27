using Godot;

namespace PaxPixelia.Map;

/// <summary>One 256×256 world-px cell of a <see cref="ChunkedOverlay"/>: its own canvas item, recorded on demand.</summary>
internal partial class OverlayChunk : Node2D
{
    public ChunkedOverlay Layer;
    public int Index;
    public float X0, Y0;       // world corner
    public bool Dirty = true;
    public float RecZoom = -1;  // zoom and level the current recording was made for
    public int RecLevel;
    public override void _Draw()
    {
        if (!Core.Game.I.IsReady) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Layer.DrawChunk(this);
        MapDebug.ChunkRecorded(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }
}
