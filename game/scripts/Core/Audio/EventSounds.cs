using System.Collections.Generic;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// Which sound an event of the deck (game/data/events) makes when the player's chronicle writes it or its choice
/// window opens: jokes get the easter-egg stinger (AUDIO.md §2.6), disasters, faith and discoveries their stingers
/// (§2.5), gold the coins, world-wide news the «bong»; the rest turns a page of the chronicle. Decided by the event id
/// first (the icon does not always tell: «Пожар» has a torch, «Премия учёным» a trophy), then by its icon.
/// </summary>
public static class EventSounds
{
    public readonly record struct Sound(string Key, bool Stinger, int Priority);

    const string Disaster = "disaster", Faith = "religion", Eureka = "eureka", Gold = "coins", News = "toast_important";

    static readonly Dictionary<string, string> ById = new()
    {
        // disasters whose icon is a torch, anchor, pick, paw, coins…
        ["town_fire"] = Disaster, ["library_fire"] = Disaster, ["sea_storm"] = Disaster, ["mine_collapse"] = Disaster,
        ["herd_sickness"] = Disaster, ["flu_season"] = Disaster, ["reactor_alarm"] = Disaster, ["orbital_debris"] = Disaster,
        ["stock_panic"] = Disaster,
        // faith (no religion system yet: the events that are about it)
        ["priest_schism"] = Faith, ["sacred_spring"] = Faith, ["eclipse_omen"] = Faith, ["eclipse_panic"] = Faith,
        // discoveries behind a book, flask, eye or trophy icon
        ["first_writing"] = Eureka, ["treatise"] = Eureka, ["printing_press"] = Eureka, ["science_prize"] = Eureka,
        ["broad_street_pump"] = Eureka, ["wash_hands"] = Eureka, ["longevity_cure"] = Eureka, ["first_broadcast"] = Eureka,
        ["orbital_photo"] = Eureka,
    };

    static readonly Dictionary<string, string> ByIcon = new()
    {
        ["alert-triangle"] = Disaster, ["droplet-off"] = Disaster, ["cloud-fog"] = Disaster, ["mountain"] = Disaster,
        ["bulb"] = Eureka, ["atom"] = Eureka, ["compass"] = Eureka,
        ["coins"] = Gold, ["diamond"] = Gold,
        ["trophy"] = News,
    };

    /// <param name="id">event id</param><param name="icon">its icon (null = «feather»)</param>
    /// <param name="joke">tone «joke»</param><param name="world">kind «world» (news for every nation)</param>
    /// <param name="importance">chronicle importance 0…3</param>
    public static Sound Classify(string id, string icon, bool joke, bool world, int importance)
    {
        if (joke) return new("stinger_egg", true, Stingers.Other);
        string k = (id != null ? ById.GetValueOrDefault(id) : null) ?? ByIcon.GetValueOrDefault(icon ?? "feather");
        k ??= world || importance >= 2 ? News : "page";
        return k switch
        {
            Disaster => new("stinger_disaster", true, Stingers.Disaster),
            Faith => new("stinger_religion", true, Stingers.Religion),
            Eureka => new("stinger_eureka", true, Stingers.Discovery),
            _ => new(k, false, 0),
        };
    }
}
