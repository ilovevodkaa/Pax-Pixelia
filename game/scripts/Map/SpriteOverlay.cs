using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>Pixel-art cities (capital with a flag in the nation colour from ×2, towns from ×3 unless a capital's sprite or
/// name needs their spot — see <see cref="LabelPlan"/>) and buildings around province centres (from ×5).</summary>
internal partial class SpriteOverlay : ChunkedOverlay
{
    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    protected override bool Wants(int p)
    {
        var s = Game.I.State;
        return s.CapitalOf[p] >= 0 || s.IsTown[p] || (s.Buildings[p] != null && s.Buildings[p].Count > 0);
    }

    protected internal override void DrawChunk(OverlayChunk c)
    {
        int level = c.RecLevel;
        if (level < 2) return;
        var w = Game.I.World; var s = Game.I.State;
        float z = c.RecZoom;
        var plan = Map.Labels.Get(level);
        int bps = PixelSprites.BuildingScale(level);
        // shadows first, then sprites: the whole chunk batches into two draws from one atlas
        for (int pass = 0; pass < 2; pass++)
            foreach (int p in Members[c.Index])
            {
                if (!KnownAt(p)) continue;
                var at = Local(c, w.PCX[p], w.PCY[p]);
                int owner = s.VisibleOwner(p) >= 0 ? s.VisibleOwner(p) : Math.Max((int)s.CapitalOf[p], 0);
                bool cap = s.CapitalOf[p] >= 0, city = plan.Sprite[p];
                int ps = PixelSprites.CityScale(level, cap);
                var list = s.Buildings[p];
                if (level >= 5 && list != null && list.Count > 0)
                {
                    // two columns of buildings: beside the city sprite (flag above and name below stay clear),
                    // or tight round the province centre just under its name
                    float cx = at.X, cy = at.Y, gap;
                    int bh = 7 * bps;
                    if (city)
                    {
                        var cs = PixelSprites.Size(cap ? Spr.Capital : Spr.Town) * ps;
                        gap = cs.X / 2f + bh * .5f + ps + 2; cx += z / 2; cy += z / 2 + cs.Y * .12f;
                    }
                    else { gap = bh * .5f + bps; cy += bh * .5f; }
                    int n = list.Count, right = (n + 1) / 2, left = n / 2;
                    for (int k = 0; k < n; k++)
                    {
                        int col = k & 1, row = k >> 1, rows = col == 0 ? right : left;
                        float y = cy + (row - (rows - 1) * .5f) * (bh + bps);
                        Put(c, pass, PixelSprites.ForBuilding(list[k]), owner, col == 0 ? cx + gap : cx - gap, y, bps);
                    }
                }
                if (city) Put(c, pass, cap ? Spr.Capital : Spr.Town, owner, at.X + z / 2, at.Y + z / 2, ps);
            }
    }

    static void Put(CanvasItem ci, int pass, Spr s, int nation, float x, float y, int ps)
    {
        if (pass == 0) PixelSprites.DrawShadow(ci, s, x, y, ps);
        else PixelSprites.DrawSprite(ci, s, nation, x, y, ps);
    }
}
