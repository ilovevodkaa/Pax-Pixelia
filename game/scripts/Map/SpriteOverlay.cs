using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Cities and buildings, drawn in their owner's era and colours (ART_BIBLE §3.4, §10): ×½–×1 a capital rhombus and
/// town dots, ×2 capitals, ×3+ every city the <see cref="LabelPlan"/> shows (capitals wear a 1 px rim of their nation
/// colour; their waving flag is drawn by <see cref="LifeOverlay"/>), and from ×5 the building icons on their plots
/// (<see cref="BuildingPlots"/>). Stale provinces show what was last seen (<see cref="MapMemory"/>).
/// </summary>
internal partial class SpriteOverlay : ChunkedOverlay
{
    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    protected override bool Wants(int p)
    {
        var mem = Map.Memory;
        return mem.IsCity(p) || mem.Buildings(p).Count > 0;
    }

    protected internal override void DrawChunk(OverlayChunk c)
    {
        int level = c.RecLevel;
        var w = Game.I.World; var mem = Map.Memory;
        float z = c.RecZoom;
        var plan = Map.Labels.Get(level);
        var origin = new Vector2(c.X0 * z, c.Y0 * z);          // level px of the chunk corner
        // shadows first, then sprites: the whole chunk batches into a couple of draws from one atlas
        for (int pass = 0; pass < 2; pass++)
            foreach (int p in Members[c.Index])
            {
                if (!KnownAt(p)) continue;
                bool city = BuildingPlacement.CityShown(mem, plan, level, p, out bool cap);
                int nation = mem.Colours(p), era = mem.Era(p);
                float cx = (w.PCX[p] + .5f) * z - origin.X, cy = (w.PCY[p] + .5f) * z - origin.Y;
                if (level >= 5) DrawBuildings(c, pass, p, nation, cap, city, plan, origin);
                if (!city) continue;
                int id = Lod.CitySprite(level, cap, era), ps = Lod.CityScale(level, cap);
                if (pass == 0) MapAtlas.DrawShadow(c, id, cx, cy, ps);
                else
                {
                    if (cap && level >= 2) MapAtlas.DrawRim(c, id, cx, cy, ps, MapPalette.RimColor(nation));
                    MapAtlas.DrawSprite(c, id, nation, cx, cy, ps);
                }
            }
    }

    /// <summary>Building icons on the province's plots, best-suited plot first, clear of the city, its name and the
    /// province name (all in level px).</summary>
    void DrawBuildings(OverlayChunk c, int pass, int p, int nation, bool cap, bool city, LabelPlan.Tier plan, Vector2 origin)
    {
        var list = Map.Memory.Buildings(p);
        if (list.Count == 0) return;
        int bs = Lod.BuildingScale(plan.Level);
        float z = plan.Z;
        Span<Vector2> at = stackalloc Vector2[Math.Min(list.Count, 32)];
        int placed = BuildingPlacement.Place(Map, p, cap, city, plan, list, at);
        for (int k = 0; k < placed; k++)
        {
            int id = MapAtlas.Building((int)list[k]);
            float x = at[k].X * z - origin.X, y = at[k].Y * z - origin.Y;
            if (pass == 0) MapAtlas.DrawShadow(c, id, x, y, bs);
            else MapAtlas.DrawSprite(c, id, nation, x, y, bs);
        }
    }
}
