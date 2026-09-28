using System;
using System.Collections.Generic;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// Player actions and queries exposed to the UI/map (partial part of the Game autoload). Every action checks the rule
/// first for an instant, local refusal toast, then issues a command (Sim/Commands.cs) that runs at the tick boundary —
/// in single player at once, also while paused. Chronicle notes about the player's own deeds are written here.
/// Game implements ISimSink implicitly (Notify / RaiseProvincesChanged / RaiseFogChanged in Game.cs).
/// </summary>
public partial class Game : ISimSink
{
    /// <summary>Raised when scout-targeting mode starts/ends (map shows a crosshair cursor while true).</summary>
    public event Action<bool> TargetingChanged;
    public bool IsTargeting { get; private set; }
    /// <summary>Scouts were sent, stepped into a new province or came back (scout box, leaderboard).</summary>
    public event Action ScoutsChanged;

    public const int ClaimCost = Rules.ClaimCost, SurveyCost = Rules.SurveyCost, MaxScouts = Scouts.Max;
    const float PickToastSeconds = 86400f;   // stays until replaced by the outcome of the pick

    SimDriver _driver;

    /// <summary>Called from Game._Ready: the sim must see WorldReady before every other module.</summary>
    void AttachSimulation()
    {
        WorldReady += OnSimWorldReady;             // before the driver's own handler (CLI commands) is added
        _driver = new SimDriver { Name = "SimDriver" };
        AddChild(_driver);
    }

    void OnSimWorldReady()
    {
        if (IsTargeting) EndTargeting();           // quietly: the UI drops the long «выберите цель» toast with it
        ScoutsChanged?.Invoke();
    }

    internal void RaiseScoutsChanged() => ScoutsChanged?.Invoke();

    // ------------------------------------------------------------------ claim

    public bool CanClaim(int p) => IsReady && Rules.CheckClaim(World, State, p, Viewer) == ClaimError.None;

    /// <summary>Why p cannot be claimed now (Russian, for a disabled button's tooltip), or null if it can.</summary>
    public string ClaimProblem(int p) => !IsReady ? "Мир ещё не создан" : ClaimText(Rules.CheckClaim(World, State, p, Viewer));

    static string ClaimText(ClaimError e) => e switch
    {
        ClaimError.None => null,
        ClaimError.NotLand => "Море нельзя присоединить",
        ClaimError.Owned => "У этих земель уже есть хозяин",
        ClaimError.Unexplored => "Сначала разведайте эти земли",
        ClaimError.NotAdjacent => "Слишком далеко от ваших границ",
        ClaimError.NoGold => $"Не хватает золота: нужно {ClaimCost}",
        ClaimError.CityFull => "Ближайшие города уже освоили свои земли — основайте новый город",
        _ => "Нельзя присоединить",
    };

    public void Claim(int p)
    {
        var why = ClaimProblem(p);
        if (why != null) { ShowRefusal(why); return; }
        int r = Issue(Cmd.Claim(Viewer, p));
        if (r != 0) { ShowRefusal(ClaimText((ClaimError)r) ?? "Нельзя присоединить"); return; }
        Notify("flag", $"Провинция {World.PName[p]} вошла в состав {Sim.Ru.Genitive(Nations[Viewer].Name)}");
    }

    // ------------------------------------------------------------------ cities

    public const int FoundCityCost = Cities.FoundCost;

    /// <summary>City sphere of the city p belongs to (p may be the city itself): city, provinces, cap, cycles to the next
    /// province (-1 = stalled). city = -1 when p has no city.</summary>
    public (int city, int count, int cap, int cyclesToNext) CityInfo(int p)
    {
        if (!IsReady || !Valid(p) || State.City == null || State.Owner[p] < 0) return (-1, 0, 0, -1);
        int c = State.City[p];
        if (c < 0) return (-1, 0, 0, -1);
        return (c, Cities.Counts(World, State)[c], Cities.Cap(State, c), Cities.CyclesToNext(World, State, c));
    }

    /// <summary>Real seconds at the current speed that a number of rules cycles takes.</summary>
    public double CyclesToSeconds(int cycles) => IsReady ? cycles * (double)Clock.CycleTicks / Clock.TicksPerSecond[State.Speed] : 0; // pax-allow: UI

    public bool CanFoundCity(int p) => IsReady && Cities.Check(World, State, p, Viewer) == FoundError.None;

    public string FoundCityProblem(int p) => !IsReady ? "Мир ещё не создан" : FoundText(Cities.Check(World, State, p, Viewer));

    static string FoundText(FoundError e) => e switch
    {
        FoundError.None => null,
        FoundError.NotLand => "Город можно основать только на суше",
        FoundError.NotYours => "Только в своих землях или рядом с границей",
        FoundError.IsCity => "Здесь уже стоит город",
        FoundError.TooClose => $"Слишком близко к другому городу: нужно не меньше {Cities.MinCityDistance} провинций",
        FoundError.NoGold => $"Не хватает золота: нужно {Cities.FoundCost}",
        FoundError.NoSettlers => $"Ни один город не может дать поселенцев: нужно больше {Cities.SettlersKeep + Cities.SettlersMin} жителей",
        FoundError.Unexplored => "Сначала разведайте эти земли",
        _ => "Здесь нельзя основать город",
    };

    /// <summary>Where the settlers would come from and how many (for the button's tooltip), or (-1, 0).</summary>
    public (int source, int people) FoundCityPlan(int p)
    {
        if (!IsReady || !Valid(p)) return (-1, 0);
        int src = Cities.SettlerSource(World, State, p, Viewer);
        return src < 0 ? (-1, 0) : (src, Cities.Settlers(State, src));
    }

    public void FoundCity(int p)
    {
        var why = FoundCityProblem(p);
        if (why != null) { ShowRefusal(why); return; }
        var (src, people) = FoundCityPlan(p);
        int r = Issue(Cmd.FoundCity(Viewer, p));
        if (r != 0) { ShowRefusal(FoundText((FoundError)r) ?? "Здесь нельзя основать город"); return; }
        Notify("map-pin", $"Основан город {World.PName[p]}: {people:N0} поселенцев пришли из {World.PName[src]}".Replace(' ', ' '));
    }

    // ------------------------------------------------------------------ buildings & geology

    public IReadOnlyList<Data.Bld> BuildOptions(int p) => IsReady ? Rules.BuildOptions(World, State, p, Viewer) : Array.Empty<Data.Bld>();
    public int BuildCost(Data.Bld b) => Rules.BuildCost(b);

    static string BuildText(BuildError e, Data.Bld b) => e switch
    {
        BuildError.NotOwned => "Строить можно только в своих провинциях",
        BuildError.NoSlot => "Свободных участков не осталось",
        BuildError.AlreadyBuilt => "Такая постройка здесь уже есть",
        BuildError.NotAllowed => "Местность не подходит для этой постройки",
        BuildError.NoGold => $"Не хватает золота: нужно {Rules.BuildCost(b)}",
        _ => "Строительство невозможно",
    };

    public void Build(int p, Data.Bld b)
    {
        if (!IsReady) return;
        var err = Rules.CheckBuild(World, State, p, b, Viewer);
        if (err != BuildError.None) { ShowRefusal(BuildText(err, b)); return; }
        int r = Issue(Cmd.Build(Viewer, p, b));
        if (r != 0) { ShowRefusal(BuildText((BuildError)r, b)); return; }
        Notify("hammer", $"{World.PName[p]}: заложена постройка «{Data.BldName[(int)b]}» (−{Rules.BuildCost(b)} золота)");
    }

    static string SurveyText(SurveyError e) => e switch
    {
        SurveyError.NotOwned => "Геологов можно отправить только в свои провинции",
        SurveyError.AlreadyDone => "Недра здесь уже разведаны",
        SurveyError.NoGold => $"Не хватает золота: нужно {SurveyCost}",
        _ => "Разведка недр невозможна",
    };

    public void Survey(int p)
    {
        if (!IsReady) return;
        var err = Rules.CheckSurvey(State, p, Viewer);
        if (err != SurveyError.None) { ShowRefusal(SurveyText(err)); return; }
        int r = Issue(Cmd.Survey(Viewer, p));
        if (r != 0) { ShowRefusal(SurveyText((SurveyError)r)); return; }
        int ore = State.Ore[p];
        Notify("shovel", ore >= 0
            ? $"Геологи нашли {Data.Ores[ore].ToLowerInvariant()} в провинции {World.PName[p]} (−{SurveyCost} золота)"
            : $"Геологи обошли провинцию {World.PName[p]}: залежей не найдено (−{SurveyCost} золота)");
    }

    /// <summary>Could geologists find anything in p (hills or mountains)? Says nothing about what is really there.</summary>
    public bool MayHaveOre(int p) => IsReady && Valid(p) && Rules.MayHaveOre(World, State, p);

    /// <summary>Taxes of p per rules cycle in gold (same formula as the treasury income).</summary>
    public double ProvinceTax(int p) => IsReady && Valid(p) ? Rules.ProvinceTax(State, p) / 100.0 : 0;

    bool Valid(int p) => (uint)p < (uint)World.P;

    // ------------------------------------------------------------------ scouts

    public int FreeScouts => IsReady ? Scouts.Free(State, Viewer) : 0;

    /// <summary>Provinces the party has added to the map so far.</summary>
    public int ScoutFound(GameState.Scout sc) => sc?.Found ?? 0;

    public void BeginScoutTargeting()
    {
        if (!IsReady) return;
        if (IsTargeting) { CancelScoutTargeting(); return; }
        if (FreeScouts == 0) { ShowRefusal(ScoutText(ScoutError.Max)); return; }
        IsTargeting = true;
        TargetingChanged?.Invoke(true);
        ShowToast("Выберите цель для разведчиков · Esc — отмена", PickToastSeconds, ToastKind.Pick);
    }

    public void CancelScoutTargeting()
    {
        if (!IsTargeting) return;
        EndTargeting();
        ShowToast("Отправка разведчиков отменена");
    }

    void EndTargeting()
    {
        IsTargeting = false;
        TargetingChanged?.Invoke(false);
    }

    /// <summary>
    /// Send a party from the capital to target. In targeting mode a bad pick (sea, unreachable) keeps the mode on
    /// with an explanation, as in the mockup; otherwise the mode ends. Returns false (with a toast) on failure.
    /// </summary>
    public bool SendScout(int target)
    {
        if (!IsReady || target < 0 || target >= World.P) return false;
        bool picking = IsTargeting;
        var err = Scouts.Check(World, State, Viewer, target, out _);
        if (err == ScoutError.None) err = (ScoutError)Issue(Cmd.ScoutTo(Viewer, target));
        if (err != ScoutError.None)
        {
            // under the clouds the refusal must not reveal whether it is sea or another continent
            string why = err is ScoutError.Sea or ScoutError.Far && State.FogEnabled && State.Fog[target] == 0
                ? "Разведчики не нашли туда пути по суше" : ScoutText(err);
            if (picking && err != ScoutError.Max) { ShowToast(why, PickToastSeconds, ToastKind.Error); return false; }
            if (picking) EndTargeting();
            ShowRefusal(why);
            return false;
        }
        if (picking) EndTargeting();
        ShowToast(State.Fog[target] != 0
            ? $"Разведчики выступили к провинции {World.PName[target]}"
            : "Разведчики выступили в неизведанные земли");
        ScoutsChanged?.Invoke();
        return true;
    }

    /// <summary>Send a party towards the nearest unexplored land.</summary>
    public bool SendScoutAuto()
    {
        if (!IsReady) return false;
        if (IsTargeting) EndTargeting();
        var err = Scouts.Check(World, State, Viewer, -1, out _);
        if (err == ScoutError.None) err = (ScoutError)Issue(Cmd.ScoutAuto(Viewer));
        if (err != ScoutError.None) { ShowRefusal(ScoutText(err)); return false; }
        ShowToast("Разведчики отправились к ближайшим неизведанным землям");
        ScoutsChanged?.Invoke();
        return true;
    }

    static string ScoutText(ScoutError e) => e switch
    {
        ScoutError.Max => $"Все разведчики уже в пути ({Scouts.Max} из {Scouts.Max})",
        ScoutError.Sea => "Разведчики ходят только по суше — выберите сухопутную провинцию",
        ScoutError.Here => "Разведчики уже в столице — выберите цель подальше",
        ScoutError.Far => "Туда не добраться по суше — корабли появятся с мореплаванием",
        ScoutError.NoTargets => "Поблизости не осталось неизведанных земель",
        _ => "Разведчики не могут выступить",
    };

    // ------------------------------------------------------------------ fog, nations, world

    public bool NationMet(int n) => IsReady && (uint)n < (uint)State.NationCapital.Length && Rules.Met(State, Viewer, n);
    public int UnmetNations => IsReady ? Rules.UnmetCount(State, Viewer) : 0;

    /// <summary>Met nations only (all in observer mode), sorted by score.</summary>
    public IReadOnlyList<(int nation, int score)> Leaderboard() => IsReady ? Rules.Leaderboard(State, Viewer) : Array.Empty<(int, int)>();

    /// <summary>Observer mode toggle: fog stays computed underneath, only the view changes.</summary>
    public void SetFogEnabled(bool on)
    {
        if (!IsReady || State.FogEnabled == on) return;
        State.FogEnabled = on;
        if (on && Selected >= 0 && State.Fog[Selected] == 0) Select(-1);
        RaiseFogChanged(null);
        ShowToast(on ? "Туман войны включён" : "Режим наблюдателя: туман войны выключен");
    }

    public void RegenerateWorld(int seed)
    {
        if (IsTargeting) EndTargeting();           // no «отменена» toast carried into the new world
        _ = NewWorld(seed);
    }

    /// <summary>Run the simulation forward without the clock (CLI fast-forward, tests): events are raised once at the end.</summary>
    public TickReport FastForward(long ticks)
    {
        var total = new TickReport();
        while (ticks > 0 && IsReady)
        {
            int n = (int)Math.Min(ticks, 4096);
            total.Add(RunTicks(n));
            ticks -= n;
        }
        return total;
    }
}
