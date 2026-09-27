using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// City names (×3+: capitals in Alegreya SC, towns in Fira Sans Condensed) and province names (×4+, small provinces
/// from ×5) at fixed pixel sizes. A town name that would collide with a more important city (capital first, then
/// lower id) is left out at that zoom. Retained per chunk; halos are drawn before fills so each chunk batches.
/// </summary>
internal partial class NameOverlay : ChunkedOverlay
{
    const int CapSize = 14, TownSize = 12, ProvSize = 11;
    float[] _wCap = Array.Empty<float>(), _wTown = Array.Empty<float>(), _wProv = Array.Empty<float>();
    object _forWorld;
    readonly List<int> _cities = new();

    public override void _Ready() => TextureFilter = TextureFilterEnum.Linear;

    protected override bool Wants(int p) => Game.I.World.PLand[p] == 1 || Game.I.State.CapitalOf[p] >= 0 || Game.I.State.IsTown[p];

    /// <summary>Also keeps the list of cities used for label collisions.</summary>
    public override void Refresh(IReadOnlyList<int> changed = null)
    {
        if (Game.I.IsReady)
        {
            var w = Game.I.World; var s = Game.I.State;
            if (_forWorld != w)
            {
                _forWorld = w;
                _wCap = NewWidths(w.P); _wTown = NewWidths(w.P); _wProv = NewWidths(w.P);
            }
            _cities.Clear();
            for (int p = 0; p < w.P; p++) if (s.CapitalOf[p] >= 0 || s.IsTown[p]) _cities.Add(p);
        }
        base.Refresh(changed);
    }

    protected internal override void DrawChunk(OverlayChunk c)
    {
        int level = c.RecLevel;
        if (level < 3 || _forWorld != Game.I.World) return;
        var w = Game.I.World; var s = Game.I.State;
        float z = c.RecZoom;
        int ps = PixelSprites.CityScale(level);
        for (int pass = 0; pass < 2; pass++)
            foreach (int p in Members[c.Index])
            {
                if (!KnownAt(p)) continue;
                var at = Local(c, w.PCX[p], w.PCY[p]);
                bool cap = s.CapitalOf[p] >= 0;
                if (cap || s.IsTown[p])
                {
                    if (!cap && Blocked(p, z, ps)) continue;
                    var font = cap ? MapFonts.Display700 : MapFonts.Ui500;
                    int size = cap ? CapSize : TownSize;
                    Label(c, pass, font, size, w.PName[p], Width(cap ? _wCap : _wTown, p, font, size),
                        at.X + z / 2, at.Y + z / 2 + NameOffset(cap, ps), cap ? MapPalette.CapitalText : MapPalette.TownText, cap ? 4 : 3);
                }
                else if (level >= 4 && (level > 4 || w.PSize[p] >= 160))
                    Label(c, pass, MapFonts.Ui500, ProvSize, w.PName[p], Width(_wProv, p, MapFonts.Ui500, ProvSize), at.X, at.Y - z * 3, MapPalette.ProvinceText, 3);
            }
    }

    static float NameOffset(bool cap, int ps) => PixelSprites.Size(cap ? Spr.Capital : Spr.Town).Y * ps / 2f + 9;

    /// <summary>Screen rect (at zoom z, world origin) of city p's name label.</summary>
    Rect2 NameRect(int p, float z, int ps)
    {
        var w = Game.I.World;
        bool cap = Game.I.State.CapitalOf[p] >= 0;
        float tw = Width(cap ? _wCap : _wTown, p, cap ? MapFonts.Display700 : MapFonts.Ui500, cap ? CapSize : TownSize);
        float h = cap ? CapSize : TownSize;
        return new Rect2(w.PCX[p] * z + z / 2 - tw / 2 - 2, w.PCY[p] * z + z / 2 + NameOffset(cap, ps) - h / 2 - 1, tw + 4, h + 2);
    }

    static Rect2 SpriteRect(int p, float z, int ps)
    {
        var w = Game.I.World;
        var size = (Vector2)PixelSprites.Size(Game.I.State.CapitalOf[p] >= 0 ? Spr.Capital : Spr.Town) * ps;
        return new Rect2(new Vector2(w.PCX[p] * z + z / 2, w.PCY[p] * z + z / 2) - size / 2, size);
    }

    /// <summary>Town p's name would overlap the name or sprite of a more important visible city.</summary>
    bool Blocked(int p, float z, int ps)
    {
        var s = Game.I.State; var w = Game.I.World;
        var mine = NameRect(p, z, ps);
        float reach = 400;
        foreach (int q in _cities)
        {
            if (q == p || !KnownAt(q)) continue;
            float dx = MathF.Abs(w.PCX[q] - w.PCX[p]) * z, dy = MathF.Abs(w.PCY[q] - w.PCY[p]) * z;
            if (dx > reach || dy > reach) continue;
            if (SpriteRect(q, z, ps).Intersects(mine)) return true;
            bool qFirst = s.CapitalOf[q] >= 0 || q < p;
            if (qFirst && NameRect(q, z, ps).Intersects(mine)) return true;
        }
        return false;
    }

    static float[] NewWidths(int n) { var a = new float[n]; Array.Fill(a, -1f); return a; }

    static float Width(float[] cache, int p, Font f, int size)
    {
        if (cache[p] < 0) cache[p] = f.GetStringSize(Game.I.World.PName[p], HorizontalAlignment.Left, -1, size).X;
        return cache[p];
    }

    static void Label(CanvasItem ci, int pass, Font f, int size, string text, float width, float cx, float cy, Color fill, int halo)
    {
        var pos = new Vector2(MathF.Round(cx - width / 2), MathF.Round(cy + (f.GetAscent(size) - f.GetDescent(size)) / 2));
        if (pass == 0) ci.DrawStringOutline(f, pos, text, HorizontalAlignment.Left, -1, size, halo, MapPalette.Halo);
        else ci.DrawString(f, pos, text, HorizontalAlignment.Left, -1, size, fill);
    }
}
