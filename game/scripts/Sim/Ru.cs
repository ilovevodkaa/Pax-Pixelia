using System.Globalization;

namespace PaxPixelia.Sim;

/// <summary>Russian text helpers for generated messages.</summary>
public static class Ru
{
    static readonly NumberFormatInfo Nf = new() { NumberGroupSeparator = " ", NumberDecimalSeparator = "," };

    /// <summary>Plural form: Plural(n, "провинция", "провинции", "провинций").</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        int m = System.Math.Abs(n) % 100, k = m % 10;
        return m is > 10 and < 20 ? many : k == 1 ? one : k is > 1 and < 5 ? few : many;
    }

    /// <summary>"5 провинций".</summary>
    public static string Count(int n, string one, string few, string many) => $"{n} {Plural(n, one, few, many)}";

    /// <summary>Rounded integer grouped with non-breaking spaces: «12 345».</summary>
    public static string Num(double v) => System.Math.Round(v).ToString("#,0", Nf);

    /// <summary>
    /// Genitive of a state name for «вошла в состав …»: «Ардания» → «Ардании», «Торн» → «Торна», «Кесарат Мирры» → «Кесарата Мирры».
    /// Only the first word declines (the rest is already an attribute in genitive).
    /// </summary>
    public static string Genitive(string name)
    {
        int sp = name.IndexOf(' ');
        string head = sp < 0 ? name : name[..sp], tail = sp < 0 ? "" : name[sp..];
        return GenitiveWord(head) + tail;
    }

    static string GenitiveWord(string w)
    {
        if (w.Length < 2) return w;
        if (w.EndsWith("ия") || w.EndsWith("ея")) return w[..^1] + "и";
        if (w.EndsWith("я")) return w[..^1] + "и";
        if (w.EndsWith("а"))
        {
            char c = w[^2];
            return w[..^1] + ("кгхжшщч".IndexOf(c) >= 0 ? "и" : "ы");
        }
        if (w.EndsWith("ь")) return w[..^1] + "я";
        if ("аеёиоуыэюя".IndexOf(w[^1]) >= 0) return w; // indeclinable (e.g. «Эльдоро»)
        return w + "а";
    }
}
