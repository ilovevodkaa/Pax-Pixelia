using System;
using System.Linq;
using PaxPixelia.Core.Nations;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// Archaeology on the Game side (Sim/Archaeology.cs): the dig as a command with its refusals and its chronicle line,
/// and who left the ruins — a name for the story only (the rules never read it): one of the player's former nations
/// from «Мои народы» when there are any («руины вашей державы из прошлого мира»), else an old name made from the seed.
/// </summary>
public partial class Game
{
    public bool HasRuins(int p) => IsReady && Archaeology.HasRuins(World, p);
    public bool RuinsDug(int p) => IsReady && Archaeology.Dug(World, State, p);

    /// <summary>Who left the ruins of p, and whether it is one of the player's own former nations.</summary>
    public (string Name, bool Mine) RuinPeople(int p)
    {
        if (!IsReady) return ("", false);
        uint h = SimRng.Hash(World.Seed, 94, p, 0);
        var mine = NationStore.List().Where(d => d.LastUsedUnix > 0 && d.Id != Setup?.Player?.Id && !string.IsNullOrWhiteSpace(d.Name))
                                     .OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        if (mine.Count > 0 && h % 3 == 0) return (mine[(int)(h / 3 % (uint)mine.Count)].Name, true);   // a third of the ruins
        return (NationNames.Random(new Random((int)h)), false);
    }

    /// <summary>«Руины Ардании — вашей державы из прошлого мира» / «Руины народа Ксилов».</summary>
    public string RuinTitle(int p)
    {
        var (name, mine) = RuinPeople(p);
        return mine ? $"Руины {Ru.Genitive(name)} — вашей державы из прошлого мира" : $"Руины народа «{name}»";
    }

    public string ExcavateProblem(int p) => !IsReady ? "Мир ещё не создан" : Archaeology.Check(World, State, p, Viewer) switch
    {
        ExcavateError.None => null,
        ExcavateError.NotRuin => "Здесь нечего копать",
        ExcavateError.NotOwned => "Копать можно только в своих землях",
        ExcavateError.Dug => "Руины уже раскопаны",
        ExcavateError.NeedEra => $"Раскопки откроет эпоха «{Eras.Name(Archaeology.Era)}»",
        ExcavateError.NoGold => $"Не хватает золота: нужно {Archaeology.Cost}",
        _ => "Копать нельзя",
    };

    public void Excavate(int p)
    {
        if (ExcavateProblem(p) is { } why) { ShowRefusal(why); return; }
        if (Issue(Cmd.Excavate(Viewer, p)) != 0) { ShowRefusal("Копать нельзя"); return; }
        Notify("shovel", $"Раскопки в {World.PName[p]}: нашли {Archaeology.FindAt(World, p)}. Слава +{Archaeology.Glory}, знания +{Archaeology.Knowledge}");
    }
}
