using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// City names (×3+: capitals a size above towns) and province names (×4+, dimmer, so city names lead) in the pixel
/// font with a hard 1 px outline, at fixed pixel sizes. Which names appear at a level — none overlapping another name,
/// a city or a nation name — is decided by <see cref="LabelPlan"/>. Retained per chunk; outlines are drawn before
/// fills so each chunk batches.
/// </summary>
internal partial class NameOverlay : ChunkedOverlay
{
    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    protected override bool Wants(int p) => Game.I.World.PLand[p] == 1 || Map.Memory.IsCity(p);

    protected internal override void DrawChunk(OverlayChunk c)
    {
        int level = c.RecLevel;
        if (level < 3) return;
        var w = Game.I.World; var mem = Map.Memory;
        var labels = Map.Labels;
        var plan = labels.Get(level);
        float z = c.RecZoom;
        var f = MapFonts.Pixel;
        for (int pass = 0; pass < 2; pass++)
            foreach (int p in Members[c.Index])
            {
                if (!plan.Name[p]) continue;
                var at = Local(c, w.PCX[p], w.PCY[p]);
                bool cap = mem.CapitalOf(p) >= 0;
                if (cap || mem.IsTown(p))
                {
                    float y = at.Y + z / 2 + labels.NameOffset(p, cap, level);
                    int size = cap ? LabelPlan.CapSize : LabelPlan.TownSize;
                    float tw = labels.CityNameWidth(p, cap);
                    var pos = new Vector2(at.X + z / 2 - tw / 2, y + PixelText.CentreBaseline(f, size));
                    PixelText.Draw(c, pass, f, pos, w.PName[p], size, cap ? MapPalette.CapitalText : MapPalette.TownText, TextFx.Outline);
                }
                else
                {
                    float tw = labels.ProvNameWidth(p);
                    var pos = new Vector2(at.X - tw / 2, at.Y - z * 3 + PixelText.CentreBaseline(f, LabelPlan.ProvSize));
                    PixelText.Draw(c, pass, f, pos, w.PName[p], LabelPlan.ProvSize, MapPalette.ProvinceText, TextFx.Shadow);
                }
            }
    }
}
