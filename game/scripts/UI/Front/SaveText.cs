using System;
using PaxPixelia.Core.Save;

namespace PaxPixelia.UI.Front;

/// <summary>Russian texts of a save for the title («ПРОДОЛЖИТЬ»), the «Загрузить» screen and the pause menu.</summary>
public static class SaveText
{
    static readonly string[] Months = { "янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек" };

    /// <summary>«2 ч 14 мин», «14 мин», «меньше минуты».</summary>
    public static string Playtime(long ms)
    {
        long min = Math.Max(0, ms) / 60000;
        if (min < 1) return "меньше минуты";
        return min < 60 ? $"{min} мин" : $"{min / 60} ч {min % 60} мин";
    }

    /// <summary>When a save was written, local time: «сегодня 14:05», «вчера 23:40», «12 сен 23:40», «12 сен 2025».</summary>
    public static string When(long unixMs, DateTime? now = null)
    {
        if (unixMs <= 0) return "";
        var t = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().DateTime;
        var today = (now ?? DateTime.Now).Date;
        if (t.Date == today) return $"сегодня {t:HH:mm}";
        if (t.Date == today.AddDays(-1)) return $"вчера {t:HH:mm}";
        var day = $"{t.Day} {Months[t.Month - 1]}";
        return t.Year == today.Year ? $"{day} {t:HH:mm}" : $"{day} {t.Year}";
    }

    /// <summary>The tag of a save's kind: АВТО (by the calendar and on leaving), БЫСТРОЕ, РУЧНОЕ.</summary>
    public static string Kind(SaveKind k) => k switch
    {
        SaveKind.Manual => "РУЧНОЕ",
        SaveKind.Quick => "БЫСТРОЕ",
        _ => "АВТО",
    };

    /// <summary>What a save is called in the lists: the player's name for a manual save, else what wrote it.</summary>
    public static string Title(SaveHeader h) => h.Kind switch
    {
        SaveKind.Auto => "Автосохранение",
        SaveKind.Exit => "Автосохранение при выходе",
        _ => string.IsNullOrWhiteSpace(h.Name) ? "Без названия" : h.Name,
    };

    /// <summary>The second line of «ПРОДОЛЖИТЬ»: «Ардания · 880 до н. э. · 2 ч 14 мин · вчера 23:40».</summary>
    public static string Meta(SaveHeader h, bool withWhen = true) =>
        $"{h.NationName} · {h.DateText} · {Playtime(h.PlaytimeMs)}{(withWhen ? " · " + When(h.SavedUnixMs) : "")}";

    /// <summary>Game length preset by its per-mille.</summary>
    public static string Pace(int permille) => permille switch
    {
        <= 700 => "Быстрая",
        >= 1300 => "Эпическая",
        _ => "Обычная",
    };
}
