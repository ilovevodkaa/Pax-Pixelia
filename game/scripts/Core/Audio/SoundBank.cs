using System.Collections.Generic;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// One playable sound: its file variants (paths under res://assets/audio/), bus, loudness offset, random pitch
/// (±Jitter, a fraction: .04 = ±4 %) and volume (±DbJitter), the shortest interval between two plays, how many may
/// sound at once, and its rank. Sounds cued in the same frame are merged: only the highest rank plays (ties: the
/// first), so one action makes one sound — a click that opens a book sounds as the book, not as click + book.
/// </summary>
public sealed record SoundDef(string Key, string[] Files, string Bus, int Rank, float Db = 0f, float Jitter = .04f,
    float DbJitter = 0f, int MinMs = 0, int Poly = 2);

/// <summary>
/// The sound table (AUDIO.md §2.2 keys, adapted to the Kenney CC0 packs; the files are listed in
/// assets/audio/CREDITS_audio.txt and built by tools/make_assets.py, see the report). Ranks: hover 0 · click 1 ·
/// tick 2 · toast / note / page 3 · controls (tab, toggle, open/close, confirm, error, question) 4 · book, gold,
/// time 5 · map and world 6 · the player's claim 7. Stinger keys (stinger_*) are queued by <see cref="Stingers"/>.
/// </summary>
public static class SoundBank
{
    public const string Root = "res://assets/audio/";

    public const int Hover = 0, Click = 1, Tick = 2, Info = 3, Control = 4, Book = 5, World = 6, Action = 7;

    public static readonly IReadOnlyDictionary<string, SoundDef> Defs = Build();

    static Dictionary<string, SoundDef> Build()
    {
        var d = new Dictionary<string, SoundDef>();
        void Add(SoundDef s) => d[s.Key] = s;
        const string ui = AudioBuses.Ui, world = AudioBuses.World, sting = AudioBuses.Stingers;

        // ---- interface (the front-end's files in sfx/ keep their names; more takes of them live in ui/) ----
        // hover/click: PixelKit and UiSounds already pass a random pitch, so no second jitter here
        Add(new("hover", Files("sfx/hover_1", "sfx/hover_2"), ui, Hover, Jitter: 0f, MinMs: 60, Poly: 2));
        Add(new("click", Files("sfx/click", "ui/ui_click_2", "ui/ui_click_3"), ui, Click, Jitter: 0f, MinMs: 30, Poly: 3));
        Add(new("tick", Files("sfx/tick_1", "sfx/tick_2", "sfx/tick_3"), ui, Tick, Jitter: .03f, MinMs: 28, Poly: 3));
        // zoom steps: the tick with a pitch given by the direction (in = higher), no random on top
        Add(new("zoom", Files("sfx/tick_1", "sfx/tick_2", "sfx/tick_3"), ui, Tick, Db: -2f, Jitter: 0f, MinMs: 40, Poly: 2));
        Add(new("tab", Files("ui/ui_tab_1", "ui/ui_tab_2"), ui, Control, Jitter: .03f, MinMs: 50));
        Add(new("toggle_on", Files("ui/ui_toggle_on_1"), ui, Control, Jitter: .02f, MinMs: 80, Poly: 1));
        Add(new("toggle_off", Files("ui/ui_toggle_off_1"), ui, Control, Jitter: .02f, MinMs: 80, Poly: 1));
        Add(new("open", Files("sfx/open", "ui/ui_open_2"), ui, Control, Jitter: .03f, MinMs: 80));
        Add(new("close", Files("sfx/close", "ui/ui_close_2"), ui, Control, Jitter: .03f, MinMs: 80));
        Add(new("confirm", Files("sfx/confirm", "ui/ui_confirm_2"), ui, Control, Jitter: .02f, MinMs: 100, Poly: 1));
        Add(new("error", Files("sfx/error", "ui/ui_error_2"), ui, Control, Jitter: .03f, MinMs: 150, Poly: 1));
        Add(new("question", Files("ui/ui_question_1"), ui, Control, Jitter: .02f, MinMs: 300, Poly: 1));
        Add(new("toast", Files("ui/ui_toast_1", "ui/ui_toast_2"), ui, Info, Jitter: .04f, MinMs: 250, Poly: 1));
        Add(new("toast_important", Files("ui/ui_toast_important_1"), ui, Info, Jitter: .02f, MinMs: 500, Poly: 1));
        Add(new("note", Files("ui/ui_note_1", "ui/ui_note_2"), ui, Info, Jitter: .04f, MinMs: 500, Poly: 1));
        Add(new("page", Files("ui/ui_page_1", "ui/ui_page_2"), ui, Info, Jitter: .04f, MinMs: 400, Poly: 1));
        Add(new("book_open", Files("ui/ui_book_open_1"), ui, Book, Jitter: .03f, MinMs: 300, Poly: 1));
        Add(new("book_close", Files("ui/ui_book_close_1"), ui, Book, Jitter: .03f, MinMs: 300, Poly: 1));
        Add(new("coins", Files("ui/ui_coins_1", "ui/ui_coins_2"), ui, Book, Jitter: .04f, MinMs: 300, Poly: 1));
        Add(new("pause", Files("ui/ui_pause_1"), ui, Book, Jitter: .02f, MinMs: 100, Poly: 1));
        Add(new("unpause", Files("ui/ui_pause_1"), ui, Book, Db: -1f, Jitter: .02f, MinMs: 100, Poly: 1));
        for (int s = 1; s <= 5; s++)   // one physical switch per speed, light → heavy
            Add(new($"speed_{s}", Files($"ui/ui_speed_{s}"), ui, Book, Jitter: .02f, MinMs: 60, Poly: 1));

        // ---- the map: a table-top diorama (AUDIO.md §1 p.2) ----
        // «фишка»: ±2 % pitch and ±1.5 dB so a hundred clicks a game never turn into a machine gun
        Add(new("piece", Files("world/world_piece_1", "world/world_piece_2", "world/world_piece_3", "world/world_piece_4"),
            world, World, Jitter: .02f, DbJitter: 1.5f, MinMs: 40, Poly: 2));
        Add(new("build", Files("world/world_build_1", "world/world_build_2"), world, World, Jitter: .03f, MinMs: 250));
        Add(new("built", Files("world/world_built_1"), world, World, Jitter: .02f, MinMs: 600, Poly: 1));
        Add(new("survey", Files("world/world_survey_1", "world/world_survey_2"), world, World, Jitter: .03f, MinMs: 300, Poly: 1));
        Add(new("ore", Files("world/world_ore_1"), world, World, Jitter: .03f, MinMs: 300, Poly: 1));
        Add(new("scouts_out", Files("world/world_scouts_out_1", "world/world_scouts_out_2"), world, World, Jitter: .04f, MinMs: 300, Poly: 1));
        Add(new("scouts_back", Files("world/world_scouts_back_1", "world/world_scouts_back_2"), world, World, Jitter: .04f, MinMs: 700, Poly: 1));
        // the claim: ~0.9 s with the capture fill, the player's own deed — played at once, not queued
        Add(new("claim", Files("stingers/stinger_claim_1", "stingers/stinger_claim_2"), sting, Action, Jitter: .01f, MinMs: 250));

        // ---- stingers (Stingers.cs: queue, priorities, ducking) ----
        Add(new("stinger_eureka", Files("stingers/stinger_eureka"), sting, Action, Jitter: 0f));
        Add(new("stinger_meet", Files("stingers/stinger_meet"), sting, Action, Jitter: 0f));
        Add(new("stinger_disaster", Files("stingers/stinger_disaster"), sting, Action, Jitter: 0f));
        Add(new("stinger_religion", Files("stingers/stinger_religion"), sting, Action, Jitter: 0f));
        Add(new("stinger_egg", Files("stingers/stinger_egg_1", "stingers/stinger_egg_2", "stingers/stinger_egg_3"), sting, Action, Jitter: 0f));
        for (int g = 2; g <= 5; g++)   // era groups Г2…Г5 (Г1 is where every game starts)
            Add(new($"stinger_era_{g}", Files($"stingers/stinger_era_{g}"), sting, Action, Jitter: 0f));
        return d;
    }

    /// <summary>Paths under <see cref="Root"/>; a stem without an extension is an .ogg (give «x.wav» for a WAV).</summary>
    static string[] Files(params string[] stems)
    {
        var f = new string[stems.Length];
        for (int i = 0; i < stems.Length; i++) f[i] = System.IO.Path.HasExtension(stems[i]) ? stems[i] : stems[i] + ".ogg";
        return f;
    }

    /// <summary>Rank of a synthesised or unknown sound (front-end extras: whoosh, stamp, pop, remove, ready).</summary>
    public const int SynthRank = Control;

    /// <summary>Era → sound group 1…5 (AUDIO.md §1 p.3): Костёр, Глина, Перо, Латунь, Сигнал.</summary>
    public static int EraGroup(int era) => era switch { <= 0 => 1, <= 2 => 2, <= 4 => 3, <= 7 => 4, _ => 5 };
}
