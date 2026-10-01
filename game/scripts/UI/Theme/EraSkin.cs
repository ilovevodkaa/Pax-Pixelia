using Godot;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>What the film over every card looks like: specks, stone crumbs, papyrus fibres, parchment stains,
/// brushed metal or screen scanlines.</summary>
public enum SkinTexture { Specks, Stone, Fibre, Parchment, Brushed, Scanlines }

/// <summary>
/// One look of the interface. The HUD changes its skin with the era group (AUDIO.md §1 п. 3: Костёр, Глина, Перо,
/// Латунь, Сигнал) — the road from the campfire to the stars shows in the interface itself. A skin is a palette of
/// roles (the same roles <see cref="Pal"/> and <see cref="PixelKit"/> expose) and a surface texture. Light skins
/// (papyrus, parchment) simply swap the ends: «Hi» is always the strongest ink against «Card», whatever its brightness.
/// </summary>
public sealed record EraSkin(
    string Id, string Name, SkinTexture Texture, bool Light,
    Color Ink, Color Well, Color Card, Color Band, Color Surface, Color SurfaceHover, Color Pressed,
    Color Ln, Color Ln2, Color Ln3, Color Hi, Color Tx, Color Ac, Color Sec, Color Mu, Color Mu2,
    Color Ok, Color Bad, Color Warn, Color Info, Color Shadow)
{
    static Color H(uint rgb) => Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>The menus' own look (the title screen keeps its graphite).</summary>
    public static readonly EraSkin Graphite = new("graphite", "Графит", SkinTexture.Specks, false,
        H(0x0b0b0c), H(0x111113), H(0x17171a), H(0x232327), H(0x202024), H(0x2c2c31), H(0x151517),
        H(0x2a2a2e), H(0x3a3a40), H(0x6b6b70), H(0xf0f0f2), H(0xd8d8da), H(0xc8c8cc), H(0xa3a3a8), H(0x8f8f94), H(0x5c5c62),
        H(0x8aa88a), H(0xb07070), H(0xbdbdb4), H(0x8a93a8), new Color(0, 0, 0, .7f));

    /// <summary>Г1 Костёр — Первобытная: warm dark stone, bone and ochre.</summary>
    public static readonly EraSkin Campfire = new("campfire", "Костёр", SkinTexture.Stone, false,
        H(0x120f0c), H(0x1b1814), H(0x26211c), H(0x3a3128), H(0x312a23), H(0x3d352c), H(0x1e1a16),
        H(0x3a3229), H(0x5c4f42), H(0x8a7a66), H(0xf4e7cf), H(0xdfd1b7), H(0xd2ad78), H(0xb89c78), H(0x9c8b74), H(0x6c5f50),
        H(0xa4b874), H(0xcf7148), H(0xe0aa48), H(0x93a9a8), new Color(0.04f, 0.02f, 0, .75f));

    /// <summary>Г2 Глина — Древний мир, Античность: sunlit papyrus, terracotta, soot ink.</summary>
    public static readonly EraSkin Clay = new("clay", "Глина", SkinTexture.Fibre, true,
        H(0x2a1a0c), H(0xd3bd86), H(0xe4d1a0), H(0xf0e2b8), H(0xdac591), H(0xebdcb0), H(0xc9b178),
        H(0xc2a774), H(0x9a7746), H(0x6e4f2c), H(0x2a1a0c), H(0x3b2914), H(0x9a3f1e), H(0x6b4e2e), H(0x7a6142), H(0x9d8763),
        H(0x4c7429), H(0xa3341c), H(0x9a6512), H(0x2f5a7e), new Color(0.24f, 0.14f, 0.05f, .45f));

    /// <summary>Г3 Перо — Средневековье, Возрождение: pale parchment, iron-gall ink, sealing wax.</summary>
    public static readonly EraSkin Quill = new("quill", "Перо", SkinTexture.Parchment, true,
        H(0x1c120a), H(0xdccaa0), H(0xece0c2), H(0xf6eedb), H(0xe3d4b0), H(0xf1e7cf), H(0xd2bf93),
        H(0xcdb98e), H(0x8c6c45), H(0x583f22), H(0x1c120a), H(0x2f2214), H(0x7c1f1f), H(0x584329), H(0x6c573a), H(0x9a8665),
        H(0x3c6a2c), H(0x8f1f1f), H(0x8e5c10), H(0x2c4c74), new Color(0.2f, 0.12f, 0.05f, .4f));

    /// <summary>Г4 Латунь — пар, индустриальная, атомная: blued steel, brass, lamp light.</summary>
    public static readonly EraSkin Brass = new("brass", "Латунь", SkinTexture.Brushed, false,
        H(0x0c0d0f), H(0x16181b), H(0x202327), H(0x30353b), H(0x2a2e33), H(0x353a40), H(0x1a1c1f),
        H(0x353a40), H(0x6d5a33), H(0xab8b4b), H(0xf3e5bf), H(0xdcd5c4), H(0xcfa555), H(0xb7a47a), H(0x938d80), H(0x64625d),
        H(0x92b67c), H(0xc76c5c), H(0xdcab44), H(0x82a6c6), new Color(0, 0, 0, .7f));

    /// <summary>Г5 Сигнал — информационная, космическая, будущее: dark glass, cyan light.</summary>
    public static readonly EraSkin Signal = new("signal", "Сигнал", SkinTexture.Scanlines, false,
        H(0x03080c), H(0x07121a), H(0x0b1820), H(0x123241), H(0x0f2430), H(0x153444), H(0x0a1820),
        H(0x163746), H(0x2a6b74), H(0x3fa3a0), H(0xe6fffb), H(0xbfeae4), H(0x4fd1c5), H(0x7cc7c0), H(0x6f9ea0), H(0x44686c),
        H(0x6ee7a0), H(0xff7a85), H(0xffd166), H(0x7ab8ff), new Color(0, 0.05f, 0.08f, .7f));

    /// <summary>The five era groups (AUDIO.md): Первобытная | Древний мир, Античность | Средневековье, Возрождение |
    /// пар, индустриальная, атомная | информационная, космическая, будущее.</summary>
    public static int Group(int era) => era switch { <= 0 => 0, <= 2 => 1, <= 4 => 2, <= 7 => 3, _ => 4 };

    public static EraSkin ForGroup(int g) => g switch { 0 => Campfire, 1 => Clay, 2 => Quill, 3 => Brass, _ => Signal };
    public static EraSkin ForEra(int era) => ForGroup(Group(era));

    /// <summary>The skin in force (the palettes hold its colours).</summary>
    public static EraSkin Current { get; private set; } = Graphite;

    /// <summary>Make s the look of everything built from now on: palettes, the surface film, the themes. Controls
    /// already built keep their old colours — the HUD is rebuilt when its skin changes (Main).</summary>
    public static void Apply(EraSkin s)
    {
        if (s == Current) return;
        Current = s;
        PixelKit.ApplySkin(s);
        Pal.ApplySkin(s);
        PixelTex.SetTexture(s.Texture, s.Light);
        PixelTheme.Invalidate();
        UiTheme.Invalidate();
    }
}
