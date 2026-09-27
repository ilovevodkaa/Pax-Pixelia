using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// City names (×3+: capitals in Alegreya SC, towns in Fira Sans Condensed 500) and province names (×4+, Fira 400 a
/// little translucent, so city names lead) at fixed pixel sizes. Which names appear at a level — none overlapping
/// another name, a city or a nation name — is decided by <see cref="LabelPlan"/>. Retained per chunk; halos are drawn
/// before fills so each chunk batches.
/// </summary>
internal partial class NameOverlay : ChunkedOverlay
{
    static readonly Color ProvinceText = new(MapPalette.ProvinceText, MapPalette.ProvinceText.A * .85f);

    public override void _Ready() => TextureFilter = TextureFilterEnum.Linear;

    protected override bool Wants(int p) => Game.I.World.PLand[p] == 1 || Game.I.State.CapitalOf[p] >= 0 || Game.I.State.IsTown[p];

    protected internal override void DrawChunk(OverlayChunk c)
    {
        int level = c.RecLevel;
        if (level < 3) return;
        var w = Game.I.World; var s = Game.I.State;
        var labels = Map.Labels;
        var plan = labels.Get(level);
        float z = c.RecZoom;
        for (int pass = 0; pass < 2; pass++)
            foreach (int p in Members[c.Index])
            {
                if (!plan.Name[p]) continue;
                var at = Local(c, w.PCX[p], w.PCY[p]);
                bool cap = s.CapitalOf[p] >= 0;
                if (cap || s.IsTown[p])
                {
                    float y = at.Y + z / 2 + LabelPlan.NameOffset(cap, PixelSprites.CityScale(level, cap));
                    if (cap) Label(c, pass, MapFonts.Display700, LabelPlan.CapSize, w.PName[p], labels.CityNameWidth(p, true), at.X + z / 2, y, MapPalette.CapitalText, 4);
                    else Label(c, pass, MapFonts.Ui500, LabelPlan.TownSize, w.PName[p], labels.CityNameWidth(p, false), at.X + z / 2, y, MapPalette.TownText, 3);
                }
                else Label(c, pass, MapFonts.Ui400, LabelPlan.ProvSize, w.PName[p], labels.ProvNameWidth(p), at.X, at.Y - z * 3, ProvinceText, 3);
            }
    }

    static void Label(CanvasItem ci, int pass, Font f, int size, string text, float width, float cx, float cy, Color fill, int halo)
    {
        var pos = new Vector2(MathF.Round(cx - width / 2), MathF.Round(cy + (f.GetAscent(size) - f.GetDescent(size)) / 2));
        if (pass == 0) ci.DrawStringOutline(f, pos, text, HorizontalAlignment.Left, -1, size, halo, MapPalette.Halo);
        else ci.DrawString(f, pos, text, HorizontalAlignment.Left, -1, size, fill);
    }
}
