using System;
using System.Text;

namespace PaxPixelia.Core.Nations;

/// <summary>A culture group of CONTENT §13 (names of cities and people follow it; the generator itself is wave 4).</summary>
public sealed record CultureGroup(string Name, string Adjective, string[] Cities, string[] People);

/// <summary>
/// Nation names: the 5 culture groups, the random name dice (Data.Syl + Data.Suf, CONTENT §13 filters) and the
/// Russian word forms shown in «Народ»: adjective «арданская», people «арданцы» (genitive is Sim.Ru.Genitive).
/// </summary>
public static class NationNames
{
    public const int MinLength = 2, MaxLength = 20;

    public static readonly CultureGroup[] Cultures =
    {
        new("Славянская", "славянская", new[] { "Звенигорье", "Ярополье", "Белоград" }, new[] { "Радомир", "Велеслава" }),
        new("Латинская", "латинская", new[] { "Валентум", "Порт-Аурий", "Аврелия" }, new[] { "Луций Клавдиан", "Сервилла" }),
        new("Степная", "степная", new[] { "Карабалык", "Алтынсарай", "Ак-Тобе" }, new[] { "Темирбек", "Айгуль" }),
        new("Восточная", "восточная", new[] { "Юньчэн", "Пинхэ", "Байлун" }, new[] { "Вэй Лун", "Лань Мэй" }),
        new("Северная", "северная", new[] { "Скагвик", "Торсхейм", "Исмарк" }, new[] { "Сигмунд", "Асхильд" }),
    };

    const string Vowels = "аеёиоуыэюя";
    static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

    /// <summary>A random nation name: 2–3 syllables + an ending, 4–11 letters, never 3 consonants or 3 vowels in a
    /// row, no syllable twice in a row («Скака…»).</summary>
    public static string Random(Random rng)
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            var sb = new StringBuilder();
            int roots = rng.Next(2, 4);
            string prev = null;
            for (int i = 0; i < roots; i++)
            {
                var syl = Data.Syl[rng.Next(Data.Syl.Length)];
                if (syl == prev) syl = Data.Syl[(Array.IndexOf(Data.Syl, syl) + 1) % Data.Syl.Length];
                sb.Append(syl);
                prev = syl;
            }
            sb.Append(Data.Suf[rng.Next(Data.Suf.Length)]);
            var s = sb.ToString();
            if (s.Length < 4 || s.Length > 11 || MaxRun(s, false) > 2 || MaxRun(s, true) > 2 || !IsVowel(s[^1]) && s.Length > 9) continue;
            return char.ToUpperInvariant(s[0]) + s[1..];
        }
        return "Ардания";
    }

    static int MaxRun(string s, bool vowels)
    {
        int run = 0, max = 0;
        foreach (char c in s)
        {
            bool counts = vowels ? IsVowel(c) : !IsVowel(c) && c is not ('ь' or 'ъ' or 'й');
            run = counts ? run + 1 : 0;
            max = Math.Max(max, run);
        }
        return max;
    }

    /// <summary>Trimmed name if it is valid (2–20 letters, spaces, hyphens), else null.</summary>
    public static string Clean(string raw)
    {
        var s = (raw ?? "").Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        if (s.Length < MinLength || s.Length > MaxLength) return null;
        foreach (char c in s) if (!char.IsLetter(c) && c != ' ' && c != '-') return null;
        return s;
    }

    /// <summary>The adjective stem: «Ардания» → «ардан», «Ксилия» → «ксиль», «Нирея» → «нирей», «Роменна» → «ромен».</summary>
    public static string Stem(string name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        int sp = n.LastIndexOf(' ');
        if (sp >= 0) n = n[(sp + 1)..];   // «Кесарат Мирры» → the last word
        if (n.Length < 2) return n;
        string stem;
        if (n.EndsWith("ея")) stem = n[..^2] + "ей";
        else if (n.EndsWith("ия")) { stem = n[..^2]; if (stem.EndsWith('л')) stem += "ь"; }
        else if (n.EndsWith("ск")) stem = n[..^2];
        else if (n[^1] is 'а' or 'я' or 'ь' or 'ы' or 'и') stem = n[..^1];
        else stem = n;
        // a doubled consonant before «-ск-» reads as one: Роменна → роменская, Скаллия → скальская
        if (stem.Length >= 2 && stem[^1] == stem[^2] && !IsVowel(stem[^1])) stem = stem[..^1];
        else if (stem.Length >= 3 && stem[^1] == 'ь' && stem[^2] == stem[^3]) stem = stem[..^3] + stem[^2..];
        return stem;
    }

    /// <summary>Feminine adjective, as Data.Nation.CultureAdj: «арданская» (for «арданская культура»).</summary>
    public static string Adjective(string name) => Stem(name) + "ская";

    public static string AdjectiveMasculine(string name) => Stem(name) + "ский";

    /// <summary>The people: «арданцы».</summary>
    public static string People(string name) => Stem(name) + "цы";
}
