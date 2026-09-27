using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaxPixelia.Content;

// JSON shapes of game/data/**. Deserialised as-is, then ContentDb compiles them into index-based runtime tables
// (EventRt) so the runner never touches strings on the hot path. Field docs: data/schema/events.schema.json.

public enum EventKind { Chronicle, Choice, World }
public enum Tone { Serious, Joke }
public enum Subject { None, Province, Capital, Foreign }

/// <summary>What a nation's land offers; an event's «terrain» list means «owns at least one of these».</summary>
[Flags]
public enum Terrain
{
    None = 0, Coast = 1, River = 2, Mountain = 4, Hills = 8, Steppe = 16, Savanna = 32, Plains = 64, Desert = 128,
    Forest = 256, Jungle = 512, Tundra = 1024, Swamp = 2048, Island = 4096, Lake = 8192,
}

/// <summary>Integer readouts an event may test (IEventState.Stat). Units in the comments.</summary>
public enum Stat
{
    Provinces,          // owned provinces
    Population,         // people
    Gold,               // whole gold
    Mood,               // average mood 0..100
    Stability,          // 0..100
    Science,            // accumulated research points of the current era
    Culture,
    MetNations,         // foreign nations met
    TradeRoutes,
    Caravans,
    BiggestCity,        // people in the largest town
    Unrest,             // discontent level 0..3 (CONTENT §2 «ступень недовольства»)
    Heirs,              // heirs of the ruler
    FoodPrice,          // permille of the base price (2000 = «цена еды ×2»)
    NeighborMoodMin,    // lowest average mood among met neighbours, 0..100 (100 when none)
    Pollution,          // 0..100
    Peacefulness,       // «Мирность» 0..255
    RulerAge,
}

/// <summary>What an effect changes (IEventSink.Add). Pop is in permille of the population, the rest in points.</summary>
public enum Resource { Gold, Mood, Pop, Stability, Science, Culture, Faith, Legacy, Food }

public sealed class EventFile
{
    [JsonPropertyName("$schema")] public string Schema { get; init; }
    public string Title { get; init; }
    public List<EventDef> Events { get; init; } = new();
}

public sealed class EventDef
{
    public string Id { get; init; }
    public EventKind Kind { get; init; } = EventKind.Chronicle;
    public Tone Tone { get; init; } = Tone.Serious;
    /// <summary>[first, last] era, 0..10.</summary>
    public int[] Eras { get; init; }
    public int Weight { get; init; } = 100;
    /// <summary>Cycles before the event may come again; 0 = the deck default of its kind.</summary>
    public int Cooldown { get; init; }
    public bool Once { get; init; }
    /// <summary>Never dealt by the metronome: fired by a follow-up or Force (world systems, debug).</summary>
    public bool FollowUpOnly { get; init; }
    /// <summary>
    /// Fires as soon as its conditions hold, outside the metronome (checked when flags change and on every beat).
    /// It must be once or clear a flag it requires, or it would repeat every cooldown.
    /// </summary>
    public bool Trigger { get; init; }
    public string Thread { get; init; }
    public int Stage { get; init; }
    public Subject Subject { get; init; }
    public CondDef When { get; init; }
    public List<BoostDef> Boost { get; init; }
    public string Icon { get; init; } = "feather";
    public string Title { get; init; }
    public string Text { get; init; }
    /// <summary>«Справка летописца»: year, place, fact. Required for jokes.</summary>
    public string Lore { get; init; }
    /// <summary>false for runet memes that do not survive translation.</summary>
    public bool Localizable { get; init; } = true;
    /// <summary>Chronicle importance 0..3; ≥2 goes to «Повесть о державе».</summary>
    public int Importance { get; init; }
    /// <summary>Chronicle kind: what happens and the line written after it.</summary>
    public EffectsDef Effects { get; init; }
    public string Result { get; init; }
    public List<OptionDef> Options { get; init; }

    [JsonIgnore] public string File { get; internal set; }
}

public sealed class CondDef
{
    public int[] Years { get; init; }
    public List<string> Terrain { get; init; }
    public List<string> Flags { get; init; }
    public List<string> NotFlags { get; init; }
    /// <summary>At least one of these flags.</summary>
    public List<string> AnyFlags { get; init; }
    public Dictionary<string, long> Min { get; init; }
    public Dictionary<string, long> Max { get; init; }
    /// <summary>Building id → minimal count in the nation.</summary>
    public Dictionary<string, int> Buildings { get; init; }
    /// <summary>Systems that must exist in this build (IEventState.HasSystem); keeps unfinished content out of the deck.</summary>
    public List<string> Requires { get; init; }
}

public sealed class BoostDef
{
    public string Flag { get; init; }
    public int Percent { get; init; } = 200;
}

public sealed class OptionDef
{
    public string Text { get; init; }
    public string Result { get; init; }
    /// <summary>AI weight; 0 = deck default by position (50 / 30 / 20).</summary>
    public int Ai { get; init; }
    public CondDef When { get; init; }
    public EffectsDef Effects { get; init; }
    public List<RollDef> Roll { get; init; }
}

public sealed class RollDef
{
    public int Permille { get; init; }
    public string Result { get; init; }
    public EffectsDef Effects { get; init; }
}

public sealed class EffectsDef
{
    public int Gold { get; init; }
    public int Mood { get; init; }
    public int MoodLocal { get; init; }
    public int MoodCapital { get; init; }
    public int Pop { get; init; }
    public int PopLocal { get; init; }
    public int Stability { get; init; }
    public int Science { get; init; }
    public int Culture { get; init; }
    public int Faith { get; init; }
    public int Legacy { get; init; }
    public int Food { get; init; }
    public List<string> SetFlags { get; init; }
    public List<string> ClearFlags { get; init; }
    public Dictionary<string, int> Axes { get; init; }
    public List<ModifierDef> Modifiers { get; init; }
    public string Trait { get; init; }
    public FollowUpDef FollowUp { get; init; }
}

public sealed class ModifierDef
{
    public string Id { get; init; }
    public int Permille { get; init; }
    public int Cycles { get; init; }
    public bool Local { get; init; }
}

public sealed class FollowUpDef
{
    public string Event { get; init; }
    public int After { get; init; }
}

// ---------------------------------------------------------------- deck settings and registries (data/core)

public sealed class DeckDef
{
    [JsonPropertyName("$schema")] public string Schema { get; init; }
    /// <summary>Sim ticks per cycle is the integrator's; content speaks in cycles (0.5 s at speed 3, IDEAS C-1).</summary>
    public int CyclesPerMinuteAtSpeed3 { get; init; } = 120;
    public int[] Gap { get; init; } = { 90, 180 };
    public int[] ChoiceGap { get; init; } = { 360, 480 };
    public int[] FirstEvent { get; init; } = { 60, 120 };
    public int Retry { get; init; } = 30;
    public int ChoiceDeadline { get; init; } = 120;
    public int ChronicleCooldown { get; init; } = 1800;
    public int ChoiceCooldown { get; init; } = 3600;
    public int JokeStreakAfter { get; init; } = 2;
    public int JokeStreakPercent { get; init; } = 20;
    public int ThreadBoostPercent { get; init; } = 500;
    public int GuaranteedThreads { get; init; } = 2;
    public int GuaranteedBoostPercent { get; init; } = 1000;
    public int[] AiDefaults { get; init; } = { 50, 30, 20 };
    public List<string> Systems { get; init; } = new();
    public List<string> Buildings { get; init; } = new();
    public List<string> Axes { get; init; } = new();
    public List<string> Icons { get; init; } = new();
    /// <summary>Tone rule 3: substrings no text may contain (real faiths, brands, post-1945 politicians).</summary>
    public List<string> Denylist { get; init; } = new();
}

public sealed class FlagsFile
{
    [JsonPropertyName("$schema")] public string Schema { get; init; }
    /// <summary>Flags the simulation sets itself (technologies, ruler traits, world quirks…).</summary>
    public List<FlagDecl> External { get; init; } = new();
}

public sealed class FlagDecl
{
    public string Id { get; init; }
    public string Desc { get; init; }
}

// ---------------------------------------------------------------- data-only tables (no logic yet)

public sealed class TraitsFile
{
    [JsonPropertyName("$schema")] public string Schema { get; init; }
    public List<TraitDef> Traits { get; init; } = new();
}

public sealed class TraitDef
{
    public string Id { get; init; }
    public string Name { get; init; }
    /// <summary>character | upbringing | acquired</summary>
    public string Group { get; init; }
    public string Desc { get; init; }
    public string Stage { get; init; }
    /// <summary>Machine-readable modifiers for the future rules (percent or points by key suffix: …Pct / flat).</summary>
    public Dictionary<string, int> Mods { get; init; }
}

public sealed class DogmasFile
{
    [JsonPropertyName("$schema")] public string Schema { get; init; }
    public List<DogmaDef> Dogmas { get; init; } = new();
}

public sealed class DogmaDef
{
    public string Id { get; init; }
    public string Name { get; init; }
    /// <summary>teaching | rite | founder | preaching</summary>
    public string Slot { get; init; }
    public string Desc { get; init; }
    /// <summary>In the MVP set of 12 (★).</summary>
    public bool Mvp { get; init; }
    public List<string> Excludes { get; init; }
    public Dictionary<string, int> Mods { get; init; }
}

public sealed class WondersFile
{
    [JsonPropertyName("$schema")] public string Schema { get; init; }
    public int[] TierCost { get; init; } = { 800, 2500, 8000 };
    public List<WonderLineDef> Lines { get; init; } = new();
    public List<WonderDef> Singles { get; init; } = new();
    public List<WonderDef> Parodies { get; init; } = new();
}

public sealed class WonderLineDef
{
    public string Id { get; init; }
    public string Name { get; init; }
    public List<WonderDef> Tiers { get; init; } = new();
}

public sealed class WonderDef
{
    public string Id { get; init; }
    public string Name { get; init; }
    public int Era { get; init; }
    public string Desc { get; init; }
    public string Stage { get; init; }
    /// <summary>Share of the tier cost for parodies, permille (Потёмкинская деревня = 200).</summary>
    public int CostPermille { get; init; } = 1000;
}
