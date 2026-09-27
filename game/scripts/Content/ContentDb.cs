using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaxPixelia.Content;

/// <summary>One JSON file of the content pack: its path relative to the data root («events/ancient.json») and text.</summary>
public readonly record struct ContentSource(string Path, string Json);

/// <summary>
/// All data-driven content (game/data): the event deck, deck settings, flag registry, leader traits, dogmas and wonders.
/// Load parses and compiles names into indexes; problems are collected in Issues instead of throwing, so one broken
/// file never takes the game down (the ContentTests fail on any issue). Pure C#: the Godot side only reads the files.
/// </summary>
public sealed class ContentDb
{
    public DeckDef Deck { get; private set; } = new();
    public List<EventDef> EventDefs { get; } = new();
    public EventRt[] Events { get; private set; } = Array.Empty<EventRt>();
    public List<TraitDef> Traits { get; } = new();
    public List<DogmaDef> Dogmas { get; } = new();
    public WondersFile Wonders { get; private set; } = new();
    public List<FlagDecl> ExternalFlags { get; } = new();
    /// <summary>Flags [0, SystemFlagCount) are set by the simulation, the rest by events.</summary>
    public int SystemFlagCount { get; private set; }

    /// <summary>
    /// Every flag the game knows: external ones, «trait.&lt;id&gt;» per leader trait and «dogma.&lt;id&gt;» per dogma
    /// (the simulation sets those for the ruler and the state faith), then the memory flags set by events.
    /// </summary>
    public List<string> Flags { get; } = new();
    public string[] Threads { get; private set; } = Array.Empty<string>();

    /// <summary>Load and reference errors («file: message»). Empty for a healthy pack.</summary>
    public List<string> Issues { get; } = new();

    readonly Dictionary<string, int> _flagIndex = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _eventIndex = new(StringComparer.Ordinal);

    public int FlagIndex(string id) => id != null && _flagIndex.TryGetValue(id, out int i) ? i : -1;
    public int EventIndex(string id) => id != null && _eventIndex.TryGetValue(id, out int i) ? i : -1;
    public int ThreadIndex(string id) => Array.IndexOf(Threads, id);

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,   // a typo in a key is an error, not silence
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Read every *.json under a directory (tests, tools). The game uses ContentLoader (res://).</summary>
    public static ContentDb LoadDirectory(string root)
    {
        var list = new List<ContentSource>();
        foreach (var f in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            list.Add(new ContentSource(Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText(f)));
        return Load(list);
    }

    public static ContentDb Load(IEnumerable<ContentSource> sources)
    {
        var db = new ContentDb();
        var ordered = new List<ContentSource>(sources);
        ordered.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));   // file order must not depend on the OS listing
        foreach (var src in ordered) db.Parse(src);
        db.Compile();
        return db;
    }

    void Parse(ContentSource src)
    {
        string p = src.Path;
        try
        {
            if (p.StartsWith("schema/", StringComparison.Ordinal)) return;
            if (p.StartsWith("events/", StringComparison.Ordinal))
            {
                var f = JsonSerializer.Deserialize<EventFile>(src.Json, Json);
                foreach (var e in f.Events) { e.File = p; EventDefs.Add(e); }
                return;
            }
            switch (p)
            {
                case "core/deck.json": Deck = JsonSerializer.Deserialize<DeckDef>(src.Json, Json); break;
                case "core/flags.json": ExternalFlags.AddRange(JsonSerializer.Deserialize<FlagsFile>(src.Json, Json).External); break;
                case "core/leader_traits.json": Traits.AddRange(JsonSerializer.Deserialize<TraitsFile>(src.Json, Json).Traits); break;
                case "core/dogmas.json": Dogmas.AddRange(JsonSerializer.Deserialize<DogmasFile>(src.Json, Json).Dogmas); break;
                case "core/wonders.json": Wonders = JsonSerializer.Deserialize<WondersFile>(src.Json, Json); break;
                default: Issues.Add($"{p}: unknown content file (expected events/*.json or a core/ table)"); break;
            }
        }
        catch (JsonException ex)
        {
            Issues.Add($"{p}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ compile

    void Compile()
    {
        foreach (var f in ExternalFlags) AddFlag(f.Id);
        foreach (var t in Traits) AddFlag("trait." + t.Id);
        foreach (var d in Dogmas) AddFlag("dogma." + d.Id);
        SystemFlagCount = Flags.Count;
        foreach (var e in EventDefs)
        {
            AddFlags(e.Effects?.SetFlags);
            if (e.Options != null)
                foreach (var o in e.Options)
                {
                    AddFlags(o.Effects?.SetFlags);
                    if (o.Roll != null) foreach (var r in o.Roll) AddFlags(r.Effects?.SetFlags);
                }
        }

        var threads = new List<string>();
        for (int i = 0; i < EventDefs.Count; i++)
        {
            var e = EventDefs[i];
            if (string.IsNullOrEmpty(e.Id)) { Issues.Add($"{e.File}: event #{i} has no id"); continue; }
            if (!_eventIndex.TryAdd(e.Id, i)) Issues.Add($"{e.File}: duplicate event id «{e.Id}»");
            if (e.Thread != null && !threads.Contains(e.Thread)) threads.Add(e.Thread);
        }
        Threads = threads.ToArray();

        Events = new EventRt[EventDefs.Count];
        for (int i = 0; i < EventDefs.Count; i++) Events[i] = CompileEvent(i, EventDefs[i]);
    }

    void AddFlags(List<string> ids) { if (ids != null) foreach (var f in ids) AddFlag(f); }

    void AddFlag(string id)
    {
        if (string.IsNullOrEmpty(id) || _flagIndex.ContainsKey(id)) return;
        _flagIndex[id] = Flags.Count;
        Flags.Add(id);
    }

    EventRt CompileEvent(int index, EventDef d)
    {
        string where = $"{d.File}: {d.Id}";
        int eraMin = 0, eraMax = 10;
        if (d.Eras is { Length: 2 }) { eraMin = d.Eras[0]; eraMax = d.Eras[1]; }
        else if (d.Eras != null) Issues.Add($"{where}: eras must be [first, last]");

        var rt = new EventRt
        {
            Index = index, Def = d, Kind = d.Kind, Tone = d.Tone, EraMin = eraMin, EraMax = eraMax,
            Weight = d.Weight, Once = d.Once, FollowUpOnly = d.FollowUpOnly, Trigger = d.Trigger,
            Thread = d.Thread == null ? -1 : ThreadIndex(d.Thread), Subject = d.Subject,
            Cooldown = d.Cooldown > 0 ? d.Cooldown : d.Kind == EventKind.Chronicle ? Deck.ChronicleCooldown : Deck.ChoiceCooldown,
            When = CompileCond(d.When, where),
            Effects = CompileEffects(d.Effects, where),
        };
        if (d.Boost != null)
        {
            rt.BoostFlag = new int[d.Boost.Count];
            rt.BoostPercent = new int[d.Boost.Count];
            for (int k = 0; k < d.Boost.Count; k++)
            {
                rt.BoostFlag[k] = RefFlag(d.Boost[k].Flag, where);
                rt.BoostPercent[k] = d.Boost[k].Percent;
            }
        }
        var opts = d.Options ?? new List<OptionDef>();
        rt.Options = new OptionRt[opts.Count];
        for (int k = 0; k < opts.Count; k++)
        {
            var o = opts[k];
            string ow = $"{where} option {k + 1}";
            int ai = o.Ai > 0 ? o.Ai : k < Deck.AiDefaults.Length ? Deck.AiDefaults[k] : 10;
            var ort = new OptionRt { Ai = ai, When = CompileCond(o.When, ow), Effects = CompileEffects(o.Effects, ow) };
            if (o.Roll != null)
            {
                ort.RollPermille = new int[o.Roll.Count];
                ort.RollEffects = new EffectsRt[o.Roll.Count];
                for (int r = 0; r < o.Roll.Count; r++)
                {
                    ort.RollPermille[r] = o.Roll[r].Permille;
                    ort.RollEffects[r] = CompileEffects(o.Roll[r].Effects, $"{ow} roll {r + 1}");
                }
            }
            rt.Options[k] = ort;
        }
        return rt;
    }

    CondRt CompileCond(CondDef c, string where)
    {
        if (c == null) return CondRt.Always;
        var rt = new CondRt();
        if (c.Years is { Length: 2 }) { rt.YearFrom = c.Years[0]; rt.YearTo = c.Years[1]; }
        else if (c.Years != null) Issues.Add($"{where}: years must be [from, to]");
        rt.Terrain = ParseTerrain(c.Terrain, where);
        rt.Flags = RefFlags(c.Flags, where);
        rt.NotFlags = RefFlags(c.NotFlags, where);
        rt.AnyFlags = RefFlags(c.AnyFlags, where);
        (rt.MinStat, rt.MinValue) = ParseStats(c.Min, where);
        (rt.MaxStat, rt.MaxValue) = ParseStats(c.Max, where);
        if (c.Buildings != null)
        {
            rt.Buildings = new string[c.Buildings.Count];
            rt.BuildingMin = new int[c.Buildings.Count];
            int k = 0;
            foreach (var (id, n) in c.Buildings)
            {
                if (!Deck.Buildings.Contains(id)) Issues.Add($"{where}: unknown building «{id}» (core/deck.json buildings)");
                rt.Buildings[k] = id; rt.BuildingMin[k++] = n;
            }
        }
        if (c.Requires != null)
        {
            foreach (var s in c.Requires)
                if (!Deck.Systems.Contains(s)) Issues.Add($"{where}: unknown system «{s}» (core/deck.json systems)");
            rt.Requires = c.Requires.ToArray();
        }
        return rt;
    }

    EffectsRt CompileEffects(EffectsDef e, string where)
    {
        if (e == null) return EffectsRt.None;
        var rt = new EffectsRt
        {
            Gold = e.Gold, Mood = e.Mood, MoodLocal = e.MoodLocal, MoodCapital = e.MoodCapital, Pop = e.Pop, PopLocal = e.PopLocal,
            Stability = e.Stability, Science = e.Science, Culture = e.Culture, Faith = e.Faith, Legacy = e.Legacy, Food = e.Food,
            SetFlags = RefFlags(e.SetFlags, where), ClearFlags = RefFlags(e.ClearFlags, where), Trait = e.Trait,
        };
        if (e.Trait != null && !Traits.Exists(t => t.Id == e.Trait)) Issues.Add($"{where}: unknown leader trait «{e.Trait}»");
        if (e.Axes != null)
        {
            rt.Axes = new string[e.Axes.Count];
            rt.AxisDelta = new int[e.Axes.Count];
            int k = 0;
            foreach (var (axis, v) in e.Axes)
            {
                if (!Deck.Axes.Contains(axis)) Issues.Add($"{where}: unknown character axis «{axis}» (core/deck.json axes)");
                rt.Axes[k] = axis; rt.AxisDelta[k++] = v;
            }
        }
        if (e.Modifiers != null) rt.Modifiers = e.Modifiers.ToArray();
        if (e.FollowUp != null)
        {
            rt.FollowUp = EventIndex(e.FollowUp.Event);
            rt.FollowUpAfter = e.FollowUp.After;
            if (rt.FollowUp < 0) Issues.Add($"{where}: follow-up event «{e.FollowUp.Event}» does not exist");
        }
        rt.Any = rt.Gold != 0 || rt.Mood != 0 || rt.MoodLocal != 0 || rt.MoodCapital != 0 || rt.Pop != 0 || rt.PopLocal != 0
                 || rt.Stability != 0 || rt.Science != 0 || rt.Culture != 0 || rt.Faith != 0 || rt.Legacy != 0 || rt.Food != 0
                 || rt.SetFlags.Length + rt.ClearFlags.Length > 0 || rt.Axes != null || rt.Modifiers != null || rt.Trait != null || rt.FollowUp >= 0;
        return rt;
    }

    int[] RefFlags(List<string> ids, string where)
    {
        if (ids == null || ids.Count == 0) return Array.Empty<int>();
        var r = new int[ids.Count];
        for (int k = 0; k < ids.Count; k++) r[k] = RefFlag(ids[k], where);
        return r;
    }

    int RefFlag(string id, string where)
    {
        int i = FlagIndex(id);
        if (i < 0) Issues.Add($"{where}: flag «{id}» is never set (no event sets it and core/flags.json does not declare it)");
        return i;
    }

    Terrain ParseTerrain(List<string> names, string where)
    {
        var t = Terrain.None;
        if (names == null) return t;
        foreach (var n in names)
            if (Enum.TryParse<Terrain>(n, ignoreCase: true, out var v) && v != Terrain.None) t |= v;
            else Issues.Add($"{where}: unknown terrain «{n}»");
        return t;
    }

    (Stat[], long[]) ParseStats(Dictionary<string, long> d, string where)
    {
        if (d == null || d.Count == 0) return (Array.Empty<Stat>(), Array.Empty<long>());
        var s = new Stat[d.Count];
        var v = new long[d.Count];
        int k = 0;
        foreach (var (name, val) in d)
        {
            if (!Enum.TryParse(name, ignoreCase: true, out s[k])) Issues.Add($"{where}: unknown stat «{name}»");
            v[k++] = val;
        }
        return (s, v);
    }
}

// ---------------------------------------------------------------- compiled runtime tables

public sealed class EventRt
{
    public int Index;
    public EventDef Def;
    public EventKind Kind;
    public Tone Tone;
    public int EraMin, EraMax, Weight, Cooldown, Thread;
    public bool Once, FollowUpOnly, Trigger;
    public Subject Subject;
    public CondRt When;
    public int[] BoostFlag, BoostPercent;
    public EffectsRt Effects;
    public OptionRt[] Options;
    public bool IsChoice => Options.Length > 0;
}

public sealed class CondRt
{
    public static readonly CondRt Always = new();
    public int YearFrom = int.MinValue, YearTo = int.MaxValue;
    public Terrain Terrain;
    public int[] Flags = Array.Empty<int>(), NotFlags = Array.Empty<int>(), AnyFlags = Array.Empty<int>();
    public Stat[] MinStat = Array.Empty<Stat>(), MaxStat = Array.Empty<Stat>();
    public long[] MinValue = Array.Empty<long>(), MaxValue = Array.Empty<long>();
    public string[] Buildings;
    public int[] BuildingMin;
    public string[] Requires;
}

public sealed class OptionRt
{
    public int Ai;
    public CondRt When;
    public EffectsRt Effects;
    public int[] RollPermille;
    public EffectsRt[] RollEffects;
}

public sealed class EffectsRt
{
    public static readonly EffectsRt None = new();
    public bool Any;
    public int Gold, Mood, MoodLocal, MoodCapital, Pop, PopLocal, Stability, Science, Culture, Faith, Legacy, Food;
    public int[] SetFlags = Array.Empty<int>(), ClearFlags = Array.Empty<int>();
    public string[] Axes;
    public int[] AxisDelta;
    public ModifierDef[] Modifiers;
    public string Trait;
    public int FollowUp = -1, FollowUpAfter;
}
