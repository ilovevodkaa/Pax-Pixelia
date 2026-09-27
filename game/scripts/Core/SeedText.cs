using System.Text;

namespace PaxPixelia.Core;

/// <summary>
/// World seed as the player types it (MAIN_MENU.md §3.2): a number is taken as is (masked to 31 bits); words are
/// normalised (lower case, ё→е, single spaces) and hashed with FNV-1a 32 over UTF-8 — never string.GetHashCode,
/// which differs between runs and machines, so «лиса у реки» is the same world for every friend.
/// </summary>
public static class SeedText
{
    public static int Parse(string text)
    {
        var s = Normalize(text);
        if (s.Length == 0) return 0;
        if (long.TryParse(s, out long n)) return (int)(n & 0x7FFFFFFF);
        uint h = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619; }
        return (int)(h & 0x7FFFFFFF);
    }

    /// <summary>True when the text is words (then the UI shows the resulting number under the field).</summary>
    public static bool IsWords(string text)
    {
        var s = Normalize(text);
        return s.Length > 0 && !long.TryParse(s, out _);
    }

    public static string Normalize(string text)
    {
        var sb = new StringBuilder();
        bool space = false;
        foreach (char raw in (text ?? "").Trim().ToLowerInvariant())
        {
            char c = raw == 'ё' ? 'е' : raw;
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A fresh 5-digit seed for the dice button.</summary>
    public static int Random5(System.Random rng) => rng.Next(10000, 100000);
}
