using System.Threading.Tasks;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// Bayer-dither curtain for scene changes and quitting (MAIN_MENU.md §1.4): 8 steps × 40 ms driven by timers,
/// not a tween, so the stages stay crisp; 3 steps with «меньше анимации». dir: 0 even, 1 forward (left → right),
/// 2 back (right → left). Blocks the mouse while not fully open.
/// </summary>
public partial class DitherWipe : CanvasLayer
{
    const float StepSeconds = .04f;
    readonly ColorRect _rect;
    readonly ShaderMaterial _mat;
    float _progress;

    public DitherWipe(bool startClosed = false)
    {
        Layer = 100;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/front/dither_wipe.gdshader") };
        _rect = new ColorRect { Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore };
        _rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
        SetProgress(startClosed ? 1 : 0);
    }

    public bool IsClosed => _progress >= 1;

    /// <summary>Pattern cell in screen px; set it to the backdrop's vp size so the curtain matches the sky.</summary>
    public float Cell { set => _mat.SetShaderParameter("cell", value); }

    public Task Close(int dir = 0) => Run(dir, true);
    public Task Open(int dir = 0) => Run(dir, false);

    public void SetProgress(float p)
    {
        _progress = p;
        _mat.SetShaderParameter("progress", p);
        _rect.Visible = p > 0;
        _rect.MouseFilter = p > 0 ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
    }

    async Task Run(int dir, bool closing)
    {
        _mat.SetShaderParameter("dir", dir);
        int steps = FrontClock.Reduced ? 3 : 8;
        for (int k = 1; k <= steps; k++)
        {
            SetProgress(closing ? (float)k / steps : 1f - (float)k / steps);
            if (!IsInsideTree()) return;
            await ToSignal(GetTree().CreateTimer(StepSeconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
        }
    }
}
