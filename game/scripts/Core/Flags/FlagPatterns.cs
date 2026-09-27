using System;

namespace PaxPixelia.Core.Flags;

/// <summary>
/// The flag vocabulary (ART_BIBLE §12, port of docs/design/concepts/src/sheet_flags.py): 9 heraldic tinctures
/// + the nation colour, 15 field divisions and 12 charges as 1-bit pixel masks. Pure C#.
/// </summary>
public static class FlagPatterns
{
    public const int W = 18, H = 12;
    public const byte NationTincture = 9;
    public const int DivisionCount = 15, ChargeCount = 12;

    // ---- tinctures: or argent gules azure vert sable purpure tenne celeste ----
    static readonly byte[][] Tinct =
    {
        new byte[] { 232, 194, 88 }, new byte[] { 240, 237, 228 }, new byte[] { 182, 58, 48 }, new byte[] { 48, 86, 160 },
        new byte[] { 58, 122, 70 }, new byte[] { 38, 34, 38 }, new byte[] { 112, 66, 136 }, new byte[] { 204, 116, 46 },
        new byte[] { 104, 156, 206 },
    };
    public static readonly string[] TinctureName = { "Золото", "Серебро", "Червлень", "Лазурь", "Зелень", "Чернь", "Пурпур", "Тенне", "Небесный", "Цвет державы" };
    public const byte Or = 0, Argent = 1, Sable = 5;
    public static bool IsMetal(byte t) => t is Or or Argent;

    /// <summary>RGB of a tincture; <see cref="NationTincture"/> resolves to the nation colour.</summary>
    public static (byte R, byte G, byte B) Rgb(byte t, (byte R, byte G, byte B) nation) =>
        t >= NationTincture ? nation : (Tinct[t][0], Tinct[t][1], Tinct[t][2]);

    // ---- divisions ----
    public static readonly string[] DivisionName =
    {
        "Одноцветное", "Две полосы", "Две половины", "Три полосы", "Триколор", "Северный крест", "Андреевский крест",
        "По диагонали", "Клин у древка", "Кантон", "Кайма", "Четверти", "Пояс", "Зубчатый край", "Вилообразный крест",
    };

    /// <summary>How many tincture slots a division paints (1..3): the editor dims the unused ones.</summary>
    public static int SlotsUsed(int division) => division switch
    {
        0 => 1,
        3 or 4 or 5 or 8 or 9 or 14 => 3,
        _ => 2,
    };

    /// <summary>Which tincture slot (0 = T1, 1 = T2, 2 = T3) paints pixel (x, y) of a w×h flag.</summary>
    public static int Slot(int division, int x, int y, int w = W, int h = H)
    {
        float sx = w / 18f, sy = h / 12f;
        switch (division)
        {
            case 1: return y < h / 2 ? 0 : 1;
            case 2: return x < w / 2 ? 0 : 1;
            case 3: return y < h / 3 ? 0 : y < 2 * h / 3 ? 1 : 2;
            case 4: return x < w / 3 ? 0 : x < 2 * w / 3 ? 1 : 2;
            case 5:
            {
                float dx = Math.Abs(x - 6 * sx + .5f), dy = Math.Abs(y - h / 2 + .5f);
                if (dx >= 2 * sx && dy >= 2 * sy) return 0;
                return dx < sx || dy < sy ? 2 : 1;
            }
            case 6: return Math.Abs(x * (float)h / w - y) < 1.4f || Math.Abs(x * (float)h / w - (h - 1 - y)) < 1.4f ? 1 : 0;
            case 7: return x * (float)h / w + y < h ? 0 : 1;
            case 8: return x < (h / 2f - Math.Abs(y - h / 2f + .5f)) * 1.1f * sx / sy ? 2 : y < h / 2 ? 0 : 1;
            case 9: return x < 8 * sx && y < 6 * sy ? 2 : (y / Math.Max(1, (int)(2 * sy))) % 2 == 0 ? 0 : 1;
            case 10:
            {
                int bx = Math.Max(1, (int)(2 * sx)), by = Math.Max(1, (int)(2 * sy));
                return x < bx || y < by || x >= w - bx || y >= h - by ? 1 : 0;
            }
            case 11: return (x < w / 2) == (y < h / 2) ? 0 : 1;
            case 12: return y >= h / 3 && y < 2 * h / 3 ? 1 : 0;
            case 13:
            {
                int m = y % 4;
                return x < (5 + (m is 1 or 2 ? 1 : 0) - (m == 0 ? 1 : 0)) * sx ? 1 : 0;
            }
            case 14:
            {
                float cy = Math.Abs(y - h / 2f + .5f);
                bool stem = cy < 1.2f * sy && x >= w / 2f - 2 * sx;
                bool arms = Math.Abs(x * .9f - (h / 2f - cy) * sx / sy) < 1.3f * sx && x < w / 2f;
                return stem || arms ? 2 : y < h / 2 ? 0 : 1;
            }
            default: return 0;
        }
    }

    // ---- charges (1..12; 0 = none) ----
    public static readonly string[] ChargeName =
        { "Нет", "Солнце", "Звезда", "Полумесяц", "Древо", "Башня", "Око", "Шестерня", "Гора", "Птица", "Колос", "Мечи", "Ключ" };

    static readonly string[][] Masks =
    {
        new[] { "#..#..#", ".#.#.#.", "..###..", "#######", "..###..", ".#.#.#.", "#..#..#" },
        new[] { "..#..", "#####", ".###.", ".#.#.", "#...#" },
        new[] { ".###.", "##...", "#....", "##...", ".###." },
        new[] { "..#..", ".###.", "#####", ".###.", "#####", "..#..", "..#.." },
        new[] { "#.#.#", "#####", ".###.", ".#.#.", ".###.", "#####" },
        new[] { "..###..", ".#...#.", "#..#..#", ".#...#.", "..###.." },
        new[] { ".#.#.", "#####", "##.##", "#####", ".#.#." },
        new[] { "...#...", "..###..", ".#####.", "#######" },
        new[] { "#..#..#", "##.#.##", ".#####.", "..###..", "..#.#.." },
        new[] { "#.#.#", ".###.", "..#..", "..#..", "..#.." },
        new[] { "#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", ".#...#.", "#.....#" },
        new[] { ".##....", "#..####", ".##.#.#" },
    };

    /// <summary>1-bit mask rows of charge 1..12.</summary>
    public static string[] Mask(int charge) => Masks[Math.Clamp(charge, 1, ChargeCount) - 1];

    public static readonly string[] PosName = { "Центр", "У древка", "Кантон" };

    /// <summary>Centre pixel of the charge for a position (18×12 grid; scaled for other sizes).</summary>
    public static (int X, int Y) Anchor(int pos, int w = W, int h = H) => pos switch
    {
        1 => (4 * w / W, h / 2),
        2 => (4 * w / W, 3 * h / H),
        _ => (w / 2, h / 2),
    };
}
