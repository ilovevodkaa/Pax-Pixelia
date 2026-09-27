using System;

namespace PaxPixelia.World;

/// <summary>
/// The few JavaScript math semantics the generator depends on. The C# port reproduces the approved mockup
/// (docs/mockups/js) bit for bit, and .NET differs from JS here: Math.Round is banker's rounding, and V8's
/// Math.hypot is not sqrt(a²+b²) to the last ulp (it decides ties in capital placement).
/// </summary>
public static class JsMath
{
    /// <summary>JS Math.round: halves go towards +infinity. Written like V8 (ceil, then step back) so that
    /// 0.49999999999999994 still rounds to 0, which floor(v + .5) gets wrong.</summary>
    public static double Round(double v)
    {
        double r = Math.Ceiling(v);
        return r - .5 > v ? r - 1 : r;
    }

    /// <summary>V8's Math.hypot for two finite arguments: normalised by the larger one, Kahan-compensated sum.</summary>
    public static double Hypot(double a, double b)
    {
        a = Math.Abs(a); b = Math.Abs(b);
        double max = a > b ? a : b;
        if (max == 0) return 0;
        double sum = 0, comp = 0;
        double n = a / max, summand = n * n - comp, pre = sum + summand;
        comp = pre - sum - summand; sum = pre;
        n = b / max; summand = n * n - comp; pre = sum + summand;
        sum = pre;
        return Math.Sqrt(sum) * max;
    }
}
