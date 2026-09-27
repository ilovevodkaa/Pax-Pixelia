using System;
using System.Text;

namespace PaxPixelia.UI;

/// <summary>Russian number / date / plural formatting (no CultureInfo: the runtime may use invariant globalization).</summary>
public static class Fmt
{
    const char Nbsp = ' ';
    const char Minus = '−';

    /// <summary>1240 → «1 240» (non-breaking space groups, like toLocaleString('ru-RU')).</summary>
    public static string Int(double v)
    {
        long n = (long)Math.Round(v);
        bool neg = n < 0; if (neg) n = -n;
        string s = n.ToString();
        if (s.Length <= 3 && !neg) return s;
        var sb = new StringBuilder(s.Length + 4);
        if (neg) sb.Append(Minus);
        int head = s.Length % 3; if (head == 0) head = 3;
        sb.Append(s, 0, head);
        for (int i = head; i < s.Length; i += 3) { sb.Append(Nbsp); sb.Append(s, i, 3); }
        return sb.ToString();
    }

    /// <summary>Compact population: 97 700 → «97,7 тыс», 1 250 000 → «1,25 млн».</summary>
    public static string Pop(double v)
    {
        if (v >= 1e6) return Dec(v / 1e6, v >= 1e7 ? 1 : 2) + " млн";
        if (v >= 1e4) return Dec(v / 1e3, 1) + " тыс";
        return Int(v);
    }

    /// <summary>Decimal with a Russian comma: 12.73 → «12,7».</summary>
    public static string Dec(double v, int digits = 1) =>
        Math.Round(v, digits).ToString("0." + new string('#', digits), System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>+12 / −6 with a real minus sign.</summary>
    public static string Signed(double v, int digits = 0)
    {
        string s = digits == 0 ? Int(Math.Abs(v)) : Dec(Math.Abs(v), digits);
        return (v < 0 ? Minus.ToString() : "+") + s;
    }

    public static string Year(int y) => Sim.GameState.YearText(y);

    /// <summary>Plural form: (1 провинция, 2 провинции, 5 провинций).</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        int m = Math.Abs(n) % 100, k = m % 10;
        return m > 10 && m < 20 ? many : k == 1 ? one : k > 1 && k < 5 ? few : many;
    }

    /// <summary>Real-time duration for the session jokes: «2 ч 14 мин», «45 мин».</summary>
    public static string Duration(int minutes) =>
        minutes >= 60 ? $"{minutes / 60} ч {minutes % 60} мин" : $"{minutes} мин";
}
