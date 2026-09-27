using System.Collections.Generic;
using Godot;

namespace PaxPixelia.Core.Flags;

/// <summary>
/// Godot side of <see cref="FlagRender"/>: cached ImageTextures of flags (plain, outlined, on a pole). Draw them with
/// nearest filtering at an integer scale (TextureRect.TextureFilter = Nearest, StretchMode = Scale).
/// </summary>
public static class FlagTextures
{
    const int MaxCached = 256;   // the editor previews ~40 variants per change; keep the cache bounded
    static readonly Dictionary<(FlagSpec, int, int), ImageTexture> _cache = new();

    static int Key((byte R, byte G, byte B) c) => c.R << 16 | c.G << 8 | c.B;

    /// <summary>The flag (18×12) with a 1px dark outline → 20×14 texture, optionally pre-scaled ×<paramref name="scale"/>
    /// with nearest sampling (for places that cannot set a texture filter).</summary>
    public static ImageTexture Get(FlagSpec spec, (byte R, byte G, byte B) nation, int scale = 1)
    {
        scale = System.Math.Clamp(scale, 1, 16);
        var key = (spec, Key(nation), 10 + scale);
        if (_cache.TryGetValue(key, out var t)) return t;
        const int w = FlagPatterns.W + 2, h = FlagPatterns.H + 2;
        var flat = FlagRender.Render(spec, nation);
        var framed = new byte[w * h * 4];
        for (int y = 0; y < FlagPatterns.H; y++)
            System.Array.Copy(flat, y * FlagPatterns.W * 4, framed, ((y + 1) * w + 1) * 4, FlagPatterns.W * 4);
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, FlagRender.Outlined(framed, w, h));
        if (scale > 1) img.Resize(w * scale, h * scale, Image.Interpolation.Nearest);
        return Store(key, img);
    }

    /// <summary>Wind frame 0..3 of the flag on its pole (<see cref="FlagRender.PoleW"/>×<see cref="FlagRender.PoleH"/>).</summary>
    public static ImageTexture Pole(FlagSpec spec, (byte R, byte G, byte B) nation, int frame)
    {
        var key = (spec, Key(nation), frame & 3);
        if (_cache.TryGetValue(key, out var t)) return t;
        return Store(key, Image.CreateFromData(FlagRender.PoleW, FlagRender.PoleH, false, Image.Format.Rgba8, FlagRender.OnPole(spec, nation, frame & 3)));
    }

    static ImageTexture Store((FlagSpec, int, int) key, Image img)
    {
        if (_cache.Count >= MaxCached) _cache.Clear();
        var tex = ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }
}
