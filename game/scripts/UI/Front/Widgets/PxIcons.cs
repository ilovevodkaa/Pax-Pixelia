using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>Sprites the pixel font lacks (MAIN_MENU.md §1.6). Order = cells of assets/front/icons.png (front_icons.py).</summary>
public enum PxIcon { Left, Right, Up, Down, Check, Dice, Copy, Lock, Diamond, Warn, Close, Dot }

/// <summary>16×16 white pixel icons, tinted through Modulate; scale only by whole factors.</summary>
public static class PxIcons
{
    public const string AtlasPath = "res://assets/front/icons.png";
    public const int Cell = 16;

    static Texture2D _atlas;
    static readonly Dictionary<PxIcon, AtlasTexture> _cache = new();

    public static Texture2D Get(PxIcon icon)
    {
        if (_cache.TryGetValue(icon, out var t)) return t;
        _atlas ??= FrontAssets.LoadTexture(AtlasPath);
        t = new AtlasTexture { Atlas = _atlas, Region = new Rect2((int)icon * Cell, 0, Cell, Cell) };
        _cache[icon] = t;
        return t;
    }

    public static TextureRect Make(PxIcon icon, int scale = 1, Color? color = null) => new()
    {
        Texture = Get(icon),
        CustomMinimumSize = Vector2.One * Cell * scale,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        Modulate = color ?? PixelKit.Text,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };
}
