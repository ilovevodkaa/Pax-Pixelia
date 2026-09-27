using System;
using Godot;

namespace PaxPixelia.Map;

/// <summary>
/// Sprite sizes per zoom level (ART_BIBLE §3.4, tuned on the contact sheet): icons grow slower than the terrain, as
/// in HOI4. ×½–×1: capital rhombus, town dot; ×2: capitals only; ×3+: every shown city; buildings from ×5.
/// Scales are screen px per sprite px (whole numbers only).
/// </summary>
internal static class Lod
{
    public static int CapitalScale(int level) => level switch { <= 1 => 1, 2 => 1, 3 => 2, 4 => 2, 5 => 3, 6 => 3, _ => 4 };
    public static int TownScale(int level) => level switch { <= 3 => 1, 4 => 2, 5 => 2, 6 => 3, _ => 3 };
    public static int CityScale(int level, bool capital) => capital ? CapitalScale(level) : TownScale(level);
    public static int BuildingScale(int level) => level >= 8 ? 3 : 2;
    public static int UnitScale(int level) => Math.Max(1, (int)MathF.Round(MapViewport.ZoomOf(level) / 2, MidpointRounding.AwayFromZero));
    /// <summary>The capital's waving flag is a step smaller than the city (a banner, not a tower).</summary>
    public static int FlagScale(int level) => Math.Max(1, CapitalScale(level) - (level >= 5 ? 1 : 0));

    /// <summary>Sprite drawn for a city at a level (the rhombus / dot below ×2 and for towns at ×2).</summary>
    public static int CitySprite(int level, bool capital, int era) =>
        level <= 1 ? (capital ? MapAtlas.Rhombus : MapAtlas.Dot) : !capital && level == 2 ? MapAtlas.Dot : MapAtlas.City(era);

    /// <summary>Screen rect of a city sprite centred on (cx, cy) (level px or screen px alike).</summary>
    public static Rect2 CitySpriteRect(float cx, float cy, int level, bool capital, int era) =>
        MapAtlas.Dest(CitySprite(level, capital, era), cx, cy, CityScale(level, capital));

    /// <summary>Top-left of the capital's flag: its pole stands on the sprite's topmost pixel.</summary>
    public static Vector2 FlagOrigin(Rect2 city, int level, int era)
    {
        int ps = CityScale(level, true), fs = FlagScale(level);
        var top = MapAtlas.Top(MapAtlas.City(era));
        float poleX = city.Position.X + (top.X + .5f) * ps;
        return new Vector2(MathF.Round(poleX - 1.5f * fs), city.Position.Y + top.Y * ps - 13 * fs);
    }

    /// <summary>Box of a city (sprite, plus the capital's flag from ×2) centred on (cx, cy).</summary>
    public static Rect2 CityBox(float cx, float cy, int level, bool capital, int era)
    {
        var r = CitySpriteRect(cx, cy, level, capital, era);
        if (!capital || level < 2) return r;
        int fs = FlagScale(level);
        return r.Merge(new Rect2(FlagOrigin(r, level, era), (Vector2)MapAtlas.Size(MapAtlas.CapitalFlag(0)) * fs));
    }
}
