using System;

namespace PaxPixelia.Core.Nations;

/// <summary>
/// Nation colour ramp with hue shifting (port of pp.ramp in docs/design/concepts/src/pp.py): the light step leans
/// toward yellow, the dark one toward blue-violet — how the city sprites and the ramp swatch are painted.
/// </summary>
public static class NationRamp
{
    public static ((byte R, byte G, byte B) Light, (byte R, byte G, byte B) Base, (byte R, byte G, byte B) Dark) From((byte R, byte G, byte B) c)
    {
        var (h, s, v) = ToHsv(c);
        return (Make(h, s, v, 1 / 6.0, .035, .78, 1.26), c, Make(h, s, v, .70, .04, 1.08, .64));
    }

    static (byte, byte, byte) Make(double h, double s, double v, double target, double dh, double ds, double dv)
    {
        double hh = s > .05 ? Toward(h, target, dh) : h;
        return FromHsv(hh, Math.Clamp(s * ds, 0, 1), Math.Clamp(v * dv, 0, 1));
    }

    static double Toward(double h, double target, double amt)
    {
        double d = ((target - h + .5) % 1.0 + 1.0) % 1.0 - .5;
        return ((h + Math.Clamp(d, -amt, amt)) % 1.0 + 1.0) % 1.0;
    }

    static (double H, double S, double V) ToHsv((byte R, byte G, byte B) c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return ((h / 6 + 1) % 1, max == 0 ? 0 : d / max, max);
    }

    static (byte, byte, byte) FromHsv(double h, double s, double v)
    {
        double i = Math.Floor(h * 6), f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = ((int)i % 6) switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return (B(r), B(g), B(b));
    }

    static byte B(double x) => (byte)(int)(x * 255 + .5);
}
