using System;
using System.Collections.Generic;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// Player actions and queries exposed to the UI/map (partial part of the Game autoload). Rules live in Sim/Rules.cs,
/// Sim/Scouts.cs and Sim/FogOfWar.cs (pure C#); this layer adds toasts, chronicle entries and events.
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
        _driver.Reset();
        ScoutsChanged?.Invoke();
    }

    internal void RaiseScoutsChanged() => ScoutsChanged?.Invoke();


    // ------------------------------------------------------------------ claim

    public bool CanClaim(int p) => IsReady && Rules.CheckClaim(World, State, p) == ClaimError.None;

    /// <summary>Why p cannot be claimed now (Russian, for a disabled button's tooltip), or null if it can.</summary>
    public string ClaimProblem(int p) => !IsReady ? "Мир ещё не создан" : Rules.CheckClaim(World, State, p) switch
    {
        ClaimError.None => null,
        ClaimError.NotLand => "Море нельзя присоединить",
        ClaimError.Owned => "У этих земель уже есть хозяин",
        ClaimError.Unexplored => "Сначала разведайте эти земли",
        ClaimError.NotAdjacent => "Слишком далеко от ваших границ",
        ClaimError.NoGold => $"Не хватает золота: нужно {ClaimCost}",
        _ => "Нельзя присоединить",
    };

    public void Claim(int p)
    {
        var why = ClaimProblem(p);
        if (why != null) { ShowRefusal(why); return; }
        Rules.Claim(World, State, p);
        Notify("flag", $"Провинция {World.PName[p]} вошла в состав {Ru.Genitive(Data.Nations[GameState.LocalPlayer].Name)}");
        RaiseProvincesChanged(new[] { p });
        FogOfWar.Refresh(World, State, this);
    }

    // ------------------------------------------------------------------ buildings & geology

    public IReadOnlyList<Data.Bld> BuildOptions(int p) => IsReady ? Rules.BuildOptions(World, State, p) : Array.Empty<Data.Bld>();
    public int BuildCost(Data.Bld b) => Rules.BuildCost(b);

    public void Build(int p, Data.Bld b)
    {
        if (!IsReady) return;
        var err = Rules.CheckBuild(World, State, p, b);
        if (err != BuildError.None)
        {
            ShowRefusal(err switch
            {
                BuildError.NotOwned => "Строить можно только в своих провинциях",
                BuildError.NoSlot => "Свободных участков не осталось",
                BuildError.AlreadyBuilt => "Такая постройка здесь уже есть",
                BuildError.NotAllowed => "Местность не подходит для этой постройки",
                BuildError.NoGold => $"Не хватает золота: нужно {Rules.BuildCost(b)}",
                _ => "Строительство невозможно",
            });
            return;
        }
        Rules.Build(State, p, b);
        Simulation.SyncQueue(State);   // building the capital's current project by hand moves its queue on
        Notify("hammer", $"{World.PName[p]}: заложена постройка «{Data.BldName[(int)b]}» (−{Rules.BuildCost(b)} золота)");
        RaiseProvincesChanged(new[] { p });
    }

    public void Survey(int p)
    {
        if (!IsReady) return;
        var err = Rules.CheckSurvey(State, p);
        if (err != SurveyError.None)
        {
            ShowRefusal(err switch
            {
                SurveyError.NotOwned => "Геологов можно отправить только в свои провинции",
                SurveyError.AlreadyDone => "Недра здесь уже разведаны",
                SurveyError.NoGold => $"Не хватает золота: нужно {SurveyCost}",
                _ => "Разведка недр невозможна",
            });
            return;
        }
        Rules.Survey(State, p);
        int ore = State.Ore[p];
        Notify("shovel", ore >= 0
            ? $"Геологи нашли {Data.Ores[ore].ToLowerInvariant()} в провинции {World.PName[p]} (−{SurveyCost} золота)"
            : $"Геологи обошли провинцию {World.PName[p]}: залежей не найдено (−{SurveyCost} золота)");
        RaiseProvincesChanged(new[] { p });
    }

    /// <summary>Could geologists find anything in p (hills or mountains)? Says nothing about what is really there.</summary>
    public bool MayHaveOre(int p) => IsReady && Valid(p) && Rules.MayHaveOre(World, State, p);

    /// <summary>Taxes of p per year (same formula as the treasury income).</summary>
    public double ProvinceTax(int p) => IsReady && Valid(p) ? Rules.ProvinceTax(State, p) : 0;

    bool Valid(int p) => (uint)p < (uint)World.P;

    // ------------------------------------------------------------------ scouts

    public int FreeScouts => IsReady ? Math.Max(0, Scouts.Max - State.Scouts.Count) : 0;

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
        var err = Scouts.Send(World, State, target, this, out _);
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
        var err = Scouts.Send(World, State, -1, this, out _);
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

    public bool NationMet(int n) => IsReady && (uint)n < (uint)State.NationCapital.Length && Rules.Met(State, n);
    public int UnmetNations => IsReady ? Rules.UnmetCount(State) : 0;

    /// <summary>Met nations only (all in observer mode), sorted by score.</summary>
    public IReadOnlyList<(int nation, int score)> Leaderboard() => IsReady ? Rules.Leaderboard(State) : Array.Empty<(int, int)>();

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
}
