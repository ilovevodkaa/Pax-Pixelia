using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using PaxPixelia.Sim;
using PaxPixelia.UI;

namespace PaxPixelia.Core.Discord;

/// <summary>
/// What the Discord card says (pure: reads <see cref="Game"/>, builds the SET_ACTIVITY activity). The first line is
/// the nation and its era; the second turns every <see cref="TurnSeconds"/> through what the player is busy with —
/// a wonder, a study, the capital's building, scouts, people, the leaderboard, the date — each with its own small
/// picture. The large picture is the city of the era (art keys of docs/discord/README.md).
/// </summary>
public static class PresenceCard
{
    public const int TurnSeconds = 20;   // > DiscordIpc's 15 s gap, so every line gets shown
    const int MaxText = 128;             // Discord refuses longer details / state
    const string Site = "https://github.com/ilovevodkaa/Pax-Pixelia";

    readonly record struct Line(string Icon, string Text, string Tip);

    /// <param name="since">Unix ms the timer counts from.</param>
    /// <param name="generating">The «Творим мир…» text while a world is being made, else null.</param>
    public static JsonObject Build(Game g, long since, string generating)
    {
        var assets = new JsonObject { ["large_image"] = "logo", ["large_text"] = "Pax Pixelia" };
        string details, state;
        if (g is not { IsReady: true })
        {
            details = generating != null ? "🌋 Творит новый мир" : "🏠 В главном меню";
            state = generating;
        }
        else
        {
            var s = g.State;
            int v = g.Viewer, era = Math.Clamp(g.EraIndex, 0, Eras.Last);
            assets["large_image"] = $"scene{era}";
            assets["large_text"] = era >= Eras.Last ? $"{g.EraName} — последняя эпоха"
                : $"{g.EraName} · {g.EraProgressPermille / 10}% пути до эпохи «{g.NextEraName}»";

            string nation = g.Nations[v].Name;
            details = g.IsBlitz ? $"⚡ Блиц недели · {nation}" : $"👑 {nation} · {g.EraName}";
            var lines = g.BlitzOver ? new List<Line> { new("act_rank", $"🏆 Блиц окончен: {Blitz.Score(s, v).Total} очков", "Итог блица") }
                : Lines(g, s, v);
            var line = lines[(int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / TurnSeconds % lines.Count)];
            state = line.Text;
            assets["small_image"] = s.Paused ? "act_pause" : line.Icon;
            assets["small_text"] = s.Paused ? "Игра на паузе" : line.Tip;
        }

        var activity = new JsonObject
        {
            ["details"] = Fit(details),
            ["timestamps"] = new JsonObject { ["start"] = since },
            ["assets"] = assets,
            ["buttons"] = new JsonArray(new JsonObject { ["label"] = "Pax Pixelia на GitHub", ["url"] = Site }),
        };
        if (Fit(state) is { } st) activity["state"] = st;
        return activity;
    }

    /// <summary>Everything worth telling right now, most telling first (never empty: the date is always there).</summary>
    static List<Line> Lines(Game g, GameState s, int v)
    {
        var me = s.Nat[v];
        var list = new List<Line>();

        if (g.IsBlitz)
        {
            long min = (g.BlitzTicksLeft + Clock.TicksPerSecond[3] * 60 - 1) / (Clock.TicksPerSecond[3] * 60);
            list.Add(new("act_blitz", $"⏳ До конца блица ~{min} {Fmt.Plural((int)min, "минута", "минуты", "минут")}", "Блиц недели"));
            list.Add(new("act_rank", $"⭐ Очки блица: {Blitz.Score(s, v).Total}", "Очки блица"));
        }

        if (me.Camp >= 0)
        {
            string people = $"{Fmt.Pop(me.TribePop)} {(me.TribePop >= 10_000 ? "человек" : Fmt.Plural(me.TribePop, "человек", "человека", "человек"))}";
            list.Add(new("act_nomad", me.CampPath != null ? $"🐾 Племя кочует · {people}" : $"🔥 Племя стоит лагерем · {people}", "Кочевое племя"));
            list.Add(new("act_nomad", $"🎒 Припасов осталось: {me.Supplies * 100 / Nomads.StartSupplies}%", "Ищет землю для столицы"));
            int legends = System.Numerics.BitOperations.PopCount((uint)me.Legends);
            if (legends > 0) list.Add(new("act_glory", $"📖 Собрано легенд: {legends} из {Nomads.MaxLegends}", "Легенды пути"));
        }
        else
        {
            if (me.Wonder >= 0)
                list.Add(new("act_wonder", $"🏛 Возводит чудо: {Wonders.All[me.Wonder].Name} · {Wonders.ProgressPermille(me) / 10}%", "Чудо света"));
            if (me.Researching >= 0)
            {
                long cost = Techs.Cost(me.Researching, s.Pace);
                list.Add(new("act_research", $"💡 Изучает: {Techs.All[me.Researching].Name} · {Math.Min(99, me.TechPts[me.Researching] * 100 / cost)}%", "Наука"));
            }
            else if (g.ResearchIdle)
                list.Add(new("act_research", "💡 Мудрецы ждут, что изучать дальше", "Наука"));
            if (me.ProjectIndex >= 0 && me.ProjectIndex < Simulation.Projects.Length)
                list.Add(new("act_build", $"🔨 Строит в столице: {Simulation.Projects[me.ProjectIndex].Name} · {me.QueuePct}%", "Стройка"));
            int scouts = Scouts.Count(s, v);
            if (scouts > 0)
                list.Add(new("act_scouts", $"🧭 Разведчики в пути: {scouts}", "Разведка"));

            double pop = 0;
            int lands = 0;
            for (int p = 0; p < s.Owner.Length; p++)
                if (s.Owner[p] == v) { pop += s.Pop[p]; lands++; }
            list.Add(new("act_people", $"👥 {Fmt.Pop(pop)} жителей · {lands} {Fmt.Plural(lands, "провинция", "провинции", "провинций")}", "Держава"));
            if (me.Glory > 0) list.Add(new("act_glory", $"✨ Слава: {me.Glory}", "Чудеса и мировые первенства"));
        }

        var board = Rules.Leaderboard(s, v);
        if (board.Count > 1)
        {
            int place = board.FindIndex(r => r.nation == v) + 1;
            list.Add(new("act_rank", place == 1 ? $"🏆 Первая среди {board.Count} известных держав"
                : $"🏆 {place}-е место из {board.Count} известных держав", "Таблица держав"));
        }
        else list.Add(new("act_land", "🗺 Соседей ещё не встретил", "Туман войны"));

        list.Add(new("act_date", $"📜 {g.DateText}", $"Скорость ×{s.Speed}"));
        return list;
    }

    /// <summary>Discord takes 2…128 characters.</summary>
    static string Fit(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.Length < 2) s += " ";
        return s.Length <= MaxText ? s : s[..(MaxText - 1)] + "…";
    }
}
