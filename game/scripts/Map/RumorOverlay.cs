using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Rumors on the map (Sim/Rumors.cs): a thin column of smoke rising from the clouds where the talk points — the
/// province stays under the fog, only the smoke tells that somebody lives there. Puffs are whole sprite pixels that
/// rise, drift and thin out (a 2.4 s loop, offset per rumor) above a breathing ember; light puffs with a dark rim read on
/// the dark clouds. Shown only while that province is unexplored and the fog is on.
/// </summary>
internal partial class RumorOverlay : MapOverlay
{
    const float LoopMs = 2400;
    static readonly Color Rim = new(22 / 255f, 26 / 255f, 31 / 255f, .55f);
    static readonly Color Puff = new(.93f, .92f, .88f), Ember = new(1f, .62f, .25f), Glow = new(1f, .55f, .2f, .28f);

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Process(double delta)
    {
        if (Game.I.IsReady && FogOn && Game.I.HeardRumors.Count > 0) QueueRedraw();
    }

    public override void _Draw()
    {
        if (Map == null || !Map.HasWorld || !Game.I.IsReady || !FogOn) return;
        var g = Game.I; var s = g.State; var w = g.World;
        var rumors = g.HeardRumors;
        if (rumors.Count == 0 || s.Fog == null) return;
        var v = Map.View;
        int ps = Math.Max(2, Lod.UnitScale(Math.Max(v.Level, 2)) + 1);
        double t = Time.GetTicksMsec();
        foreach (var r in rumors)
        {
            int p = r.Province;
            if (s.Fog[p] != 0) continue;
            float wx = w.PCX[p] + .5f, sy = v.ScreenY(w.PCY[p] + .5f);
            if (sy < -200 || sy > v.Screen.Y + 200) continue;
            float phase0 = (r.Nation * 0.37f) % 1;
            for (float sx = v.FirstX(wx, 200); sx < v.Screen.X + 200; sx += v.WZ)
            {
                // the ember at the foot of the column, breathing, and its glow on the clouds
                float breath = .75f + .25f * MathF.Sin((float)(t / 300 + r.Nation));
                DrawRect(new Rect2(MathF.Round(sx - ps * 2), MathF.Round(sy - ps), ps * 4, ps * 3), Glow with { A = Glow.A * breath });
                Square(sx, sy, ps, Ember with { A = breath }, 2, true);
                for (int i = 0; i < 5; i++)
                {
                    float ph = (float)((t / LoopMs + phase0 + i / 5f) % 1);
                    float px = sx + MathF.Sin(ph * 5f + i) * ps * 2f + ph * ps * 4;   // a slow wind blows it east
                    float py = sy - ps * 3 - ph * ps * 20;
                    int size = 2 + (int)(ph * 4f);   // puffs swell as they rise
                    Square(px, py, ps, Puff with { A = .95f * (1 - ph * ph) }, size, true);
                }
            }
        }
    }

    /// <summary>A puff of size×size sprite pixels centred at (cx, cy), snapped to the pixel grid, with a rim.</summary>
    void Square(float cx, float cy, int ps, Color c, int size, bool rim)
    {
        float half = size * ps / 2f;
        var rect = new Rect2(MathF.Round(cx - half), MathF.Round(cy - half), size * ps, size * ps);
        if (rim || c.A > .3f) DrawRect(rect.Grow(1), Rim with { A = Rim.A * c.A });
        DrawRect(rect, c);
    }
}
