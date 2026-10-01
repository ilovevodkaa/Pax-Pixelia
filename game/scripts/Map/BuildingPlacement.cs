using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Where a province's building icons stand (ART_BIBLE §10.2): in list order, each takes the best-scoring free plot
/// (<see cref="BuildingPlots"/>) whose icon box is clear of the city sprite, its name (or the province name) and the
/// icons placed before it, all in level px of the label tier. Shared by the retained building layer and the
/// construction sites, so a scaffold stands exactly where its building will: a job is placed after the standing
/// buildings, which is where the finished building joins the list.
/// </summary>
internal static class BuildingPlacement
{
    static readonly List<Rect2> Keep = new();   // main thread only (overlays draw there)

    /// <summary>Is the city sprite of p shown at this tier (capitals from ×2, towns the plan picks)?</summary>
    public static bool CityShown(MapMemory mem, LabelPlan.Tier plan, int level, int p, out bool cap)
    {
        cap = mem.CapitalOf(p) >= 0;
        return cap ? plan.Sprite[p] || level < 2 : plan.Sprite[p] || (level is 1 or 2 && mem.IsTown(p));
    }

    /// <summary>
    /// Plot centres (world px, x unwrapped near the province) of kinds[0..] in order, written to `at`; returns how many
    /// fit (a crowded little province shows what fits: placing stops at the first that does not).
    /// </summary>
    public static int Place(MapView map, int p, bool cap, bool city, LabelPlan.Tier plan, IReadOnlyList<Data.Bld> kinds, Span<Vector2> at)
    {
        var w = Game.I.World; var labels = map.Labels;
        int level = plan.Level, bs = Lod.BuildingScale(level);
        float z = plan.Z, half = 5 * bs + 1;                 // icon 10×10 sprite px
        Keep.Clear();
        if (city) Keep.Add(labels.SpriteRect(w, p, cap, level, z).Grow(2));
        if (plan.Name[p]) Keep.Add((city ? labels.NameRect(w, p, cap, level, z) : labels.ProvRect(w, p, z)).Grow(1));
        var plots = map.Plots.Of(p);
        Span<bool> used = stackalloc bool[plots.Length];
        int n = Math.Min(kinds.Count, at.Length);
        for (int k = 0; k < n; k++)
        {
            int best = -1; float bestScore = float.MinValue;
            for (int i = 0; i < plots.Length; i++)
            {
                if (used[i]) continue;
                var q = plots[i];
                float dx = q.X - w.PCX[p], dy = q.Y - w.PCY[p];
                float s = BuildingPlots.Score(kinds[k], q, MathF.Sqrt(dx * dx + dy * dy));
                if (s <= bestScore) continue;
                var box = new Rect2((q.X + .5f) * z - half, (q.Y + .5f) * z - half, half * 2, half * 2);
                bool clear = true;
                foreach (var r in Keep) if (box.Intersects(r)) { clear = false; break; }
                if (!clear) continue;
                best = i; bestScore = s;
            }
            if (best < 0) return k;
            used[best] = true;
            var pl = plots[best];
            Keep.Add(new Rect2((pl.X + .5f) * z - half, (pl.Y + .5f) * z - half, half * 2, half * 2));
            at[k] = new Vector2(pl.X + .5f, pl.Y + .5f);
        }
        return n;
    }
}
