namespace PaxPixelia.Content;

/// <summary>
/// Read-only view of one nation that the event deck tests conditions against. The simulation implements it over
/// GameState (one adapter per nation); ContentTests implement it with a scripted fake. Called only on metronome
/// beats (every 45–90 s at speed 3), never per frame, so plain loops over provinces are fine.
/// </summary>
public interface IEventState
{
    /// <summary>Era of this nation, 0 = Первобытная … 10 = Будущее.</summary>
    int Era { get; }
    /// <summary>Calendar year (negative = до н. э., no year 0).</summary>
    int Year { get; }
    long Stat(Stat stat);
    /// <summary>Buildings of kind id (core/deck.json «buildings») in the whole nation.</summary>
    int Buildings(string id);
    /// <summary>Whether a system exists in this build (core/deck.json «systems»); false keeps «requires» events out.</summary>
    bool HasSystem(string id);
    /// <summary>
    /// An owned province having any of the terrain bits (Terrain.None = any province), chosen by roll
    /// (e.g. candidates[roll % count]); -1 when there is none. Must be deterministic for the same state and roll.
    /// </summary>
    int PickProvince(Terrain any, uint roll);
    /// <summary>A met foreign nation chosen by roll, -1 when none met.</summary>
    int PickForeign(uint roll);
    /// <summary>Capital province, -1 while nomadic.</summary>
    int Capital { get; }
}

/// <summary>
/// Where the runner applies effects and reports events. Amounts are integers: gold in whole coins, mood / stability /
/// science / culture / faith / legacy / food in points, Pop in permille of the population. province = -1 means
/// the whole nation.
/// </summary>
public interface IEventSink
{
    void Add(int nation, Resource what, int province, int amount);
    /// <summary>Character axis (core/deck.json «axes»), delta in accumulator units (×1000 in Sim, IDEAS N-2).</summary>
    void Axis(int nation, string axis, int delta);
    /// <summary>Timed modifier: id, permille of the affected value, duration in sim ticks, province or -1.</summary>
    void Modifier(int nation, string id, int permille, int ticks, int province);
    void Trait(int nation, string traitId);
    /// <summary>A choice event opened for a human player (Option = -1): show the window, deadline in r.Deadline.</summary>
    void Fired(in EventRecord r);
    /// <summary>
    /// An event happened or a choice was made: write the chronicle entry. Text = EventText.Result(db, r) in the voice
    /// of r.Era; the Sim stores only this record (key + args), never strings.
    /// </summary>
    void Resolved(in EventRecord r);
}

/// <summary>What happened, as indexes and arguments (lockstep-safe, serialisable). Texts live in ContentDb.</summary>
public readonly record struct EventRecord(
    int Nation, long Tick, int Event, int Option, int Branch,
    int Province, int Foreign, int Era, int Year, long Deadline);
