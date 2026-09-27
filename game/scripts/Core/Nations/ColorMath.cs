using System;

namespace PaxPixelia.Core.Nations;

/// <summary>Perceptual colour distance (OKLab, Björn Ottosson 2020). Used outside Sim only: floats are fine here.</summary>
public static class ColorMath
{
    public static (double L, double A, double B) Oklab((byte R, byte G, byte B) c)
    {
        double r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
        double l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        double m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        double s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        return (.2104542553 * l + .7936177850 * m - .0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + .4505937099 * s,
                .0259040371 * l + .7827717662 * m - .8086757660 * s);
    }

    /// <summary>ΔE in OKLab: ≈0.02 is barely visible, 0.10 is «clearly another nation» on the map.</summary>
    public static double DeltaEOklab((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var (l1, a1, b1) = Oklab(a);
        var (l2, a2, b2) = Oklab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    static double Lin(byte v)
    {
        double c = v / 255.0;
        return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
    }
}
