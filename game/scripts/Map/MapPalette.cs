using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Map;

internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Of(float r, float g, float b) => new(Clamp(r), Clamp(g), Clamp(b));
    public Rgb Scaled(float f, float add = 0) => Of(R * f + add, G * f + add, B * f + add);
    public Color ToColor(float a = 1) => new(R / 255f, G / 255f, B / 255f, a);
    static byte Clamp(float v) => (byte)Math.Clamp((int)v, 0, 255);
}

/// <summary>Map colours (values from docs/mockups/js/render.js + worldgen.js).</summary>
internal static class MapPalette
{
    public static Rgb Nation(int n)
    {
        var roster = Game.I?.Nations ?? Data.Nations;
        if ((uint)n >= (uint)roster.Length) return new(128, 128, 128);
        var d = roster[n];
        return new(d.R, d.G, d.B);
    }
    /// <summary>Country border: the nation colour brightened (natBrd).</summary>
    public static Rgb BorderColor(int n) => Nation(n).Scaled(1.2f, 40);
    /// <summary>Nation name on the map: the nation colour lifted towards white, so it reads over its own tint.</summary>
    public static Color LabelColor(int n) { var c = Nation(n).ToColor(); return c.Lerp(Colors.White, .45f); }
    /// <summary>The 1 px rim round a capital: its nation colour, lifted.</summary>
    public static Color RimColor(int n) => Nation(n).Scaled(1.15f, 30).ToColor();
    public static Rgb Religion(int r) { var d = Data.Religions[r]; return new(d.R, d.G, d.B); }

    public static Rgb Fertility(float f) => f < .5f
        ? Rgb.Of(200, 90 + f * 2 * 120, 70)
        : Rgb.Of(200 - (f - .5f) * 2 * 120, 210, 70 + (f - .5f) * 2 * 20);

    // map text: monochrome like the UI (importance by brightness), pixel font with hard dark effects
    public static readonly Color CapitalText = new(.94f, .94f, .95f);
    public static readonly Color TownText = new(.85f, .85f, .86f);
    public static readonly Color ProvinceText = new(.78f, .79f, .8f, .9f);
    public static readonly Color SeaText = new(150 / 255f, 176 / 255f, 198 / 255f, .85f);
    // light enough to read on dark forest and jungle
    public static readonly Color RiverWater = new(0x6a / 255f, 0x9c / 255f, 0xc6 / 255f);
    public static readonly Color RiverWaterMuted = new(0x62 / 255f, 0x86 / 255f, 0xa8 / 255f);

    /// <summary>Per-province map-mode parameters: tint colour + strength, desaturation, brightness.</summary>
    public static void ModeTint(MapMode mode, WorldData w, GameState s, int p, out Rgb tint, out float a, out float desat, out float dim)
    {
        bool land = w.PLand[p] == 1;
        int o = s.VisibleOwner(p);
        tint = default; a = 0; desat = 0; dim = 1;
        switch (mode)
        {
            case MapMode.Political:
                if (land && o >= 0) { tint = Nation(o); a = .56f; }
                else if (land) { desat = .18f; dim = .94f; }
                break;
            case MapMode.Religion:
                if (land && s.Religion[p] >= 0) { tint = Religion(s.Religion[p]); a = .56f; }
                else if (land) desat = .6f;
                break;
            case MapMode.Trade:
                desat = .62f; dim = .85f;
                if (land && o >= 0) { tint = Nation(o); a = .18f; }
                break;
            case MapMode.Fertility:
                if (land) { tint = Fertility(w.PFert[p]); a = .62f; }
                else desat = .5f;
                break;
        }
    }
}
