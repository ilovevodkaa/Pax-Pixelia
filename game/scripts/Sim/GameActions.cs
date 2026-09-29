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
    /// <summary>What a map pick in targeting mode does: send scouts (false) or lead the tribe (true).</summary>
    public bool TargetingTribe { get; private set; }
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

    public const int FoundCityCost = Cities.FoundCost, FoundCityMaterials = Cities.FoundMaterials;

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
        FoundError.NoMaterials => $"Не хватает материалов: нужно {Cities.FoundMaterials}. Их дают лесопилки и каменоломни",
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
    public int BuildMaterials(Data.Bld b) => Rules.BuildMaterials(b);

    /// <summary>Where the local nation's materials come from: lumber mills, quarries, mines on metal veins, the capital.</summary>
    public (int lumber, int quarry, int mines, int capital) MaterialSources()
    {
        int l = 0, q = 0, m = 0, c = 0;
        if (!IsReady) return (0, 0, 0, 0);
        var s = State;
        for (int p = 0; p < s.Owner.Length; p++)
        {
            if (s.Owner[p] != Viewer) continue;
            foreach (var b in s.Buildings[p]) { if (b == Data.Bld.Lumber) l++; else if (b == Data.Bld.Quarry) q++; }
            if (Rules.IsMine(s, p)) m++;
            if (s.CapitalOf[p] >= 0) c++;
        }
        return (l, q, m, c);
    }

    static string BuildText(BuildError e, Data.Bld b) => e switch
    {
        BuildError.NotOwned => "Строить можно только в своих провинциях",
        BuildError.NoSlot => "Свободных участков не осталось",
        BuildError.AlreadyBuilt => "Такая постройка здесь уже есть",
        BuildError.NotAllowed => "Местность не подходит для этой постройки",
        BuildError.NoGold => $"Не хватает золота: нужно {Rules.BuildCost(b)}",
        BuildError.NoMaterials => $"Не хватает материалов: нужно {Rules.BuildMaterials(b)}. Постройте лесопилку или каменоломню",
        BuildError.NeedTech => Techs.For(b) is int t and >= 0 ? $"Нужна технология «{Techs.All[t].Name}»" : $"Откроется в эпоху «{Eras.Name(Techs.BuildingEra(b))}»",
        _ => "Строительство невозможно",
    };

    /// <summary>Why b cannot be built in p now (Russian, for a disabled menu button), or null if it can.</summary>
    public string BuildProblem(int p, Data.Bld b)
    {
        if (!IsReady) return "Мир ещё не создан";
        var e = Rules.CheckBuild(World, State, p, b, Viewer);
        return e == BuildError.None ? null : BuildText(e, b);
    }

    /// <summary>Buildings the land of p allows that the nation does not know yet (shown locked in the build menu).</summary>
    public IReadOnlyList<Data.Bld> LockedBuildOptions(int p) => IsReady ? Rules.LockedOptions(World, State, p, Viewer) : Array.Empty<Data.Bld>();

    public void Build(int p, Data.Bld b)
    {
        if (!IsReady) return;
        var err = Rules.CheckBuild(World, State, p, b, Viewer);
        if (err != BuildError.None) { ShowRefusal(BuildText(err, b)); return; }
        int r = Issue(Cmd.Build(Viewer, p, b));
        if (r != 0) { ShowRefusal(BuildText((BuildError)r, b)); return; }
        int mat = Rules.BuildMaterials(b);
        Notify("hammer", $"{World.PName[p]}: заложена постройка «{Data.BldName[(int)b]}» (−{Rules.BuildCost(b)} золота{(mat > 0 ? $", −{mat} материалов" : "")})");
    }

    static string SurveyText(SurveyError e) => e switch
    {
        SurveyError.NotOwned => "Геологов можно отправить только в свои провинции",
        SurveyError.AlreadyDone => "Недра здесь уже разведаны",
        SurveyError.NoGold => $"Не хватает золота: нужно {SurveyCost}",
        SurveyError.NeedTech => $"Нужна технология «{Techs.All[Techs.SurveyTech].Name}»",
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

    /// <summary>Why geologists cannot go to p now, or null.</summary>
    public string SurveyProblem(int p)
    {
        if (!IsReady) return "Мир ещё не создан";
        var e = Rules.CheckSurvey(State, p, Viewer);
        return e == SurveyError.None ? null : SurveyText(e);
    }

    // ------------------------------------------------------------------ technologies

    /// <summary>Raised when the local nation chose, switched or finished a study (the tech card and the top bar refresh).</summary>
    public event Action ResearchChanged;

    public enum TechState { Known, Studying, Open, Later }

    /// <summary>One row of the tech card: the definition, where it stands, points so far, full cost, real seconds left
    /// at the current speed (-1 when not studied now).</summary>
    public readonly record struct TechView(int Id, TechDef Def, TechState State, long Points, int Cost, int SecondsLeft);

    public List<TechView> TechViews()
    {
        var list = new List<TechView>();
        if (!IsReady) return list;
        var nat = State.Nat[Viewer];
        for (int t = 0; t < Techs.Count; t++)
        {
            var d = Techs.All[t];
            if (d.Era > nat.Era + 1) continue;
            int cost = Techs.Cost(t, State.Pace);
            var st = Techs.Known(nat, t) ? TechState.Known : nat.Researching == t ? TechState.Studying : Techs.Open(nat, t) ? TechState.Open : TechState.Later;
            int secs = -1;
            if (st == TechState.Studying && nat.ScienceRate > 0)
                secs = (int)Math.Ceiling(CyclesToSeconds((int)((cost - nat.TechPts[t] + nat.ScienceRate - 1) / nat.ScienceRate)));
            list.Add(new TechView(t, d, st, st == TechState.Known ? cost : nat.TechPts[t], cost, secs));
        }
        return list;
    }

    /// <summary>Science banked while nothing was chosen (goes to the next choice).</summary>
    public long TechPool => IsReady ? State.Nat[Viewer].TechPool : 0;
    /// <summary>The technology being studied, or -1.</summary>
    public int Researching => IsReady ? State.Nat[Viewer].Researching : -1;
    /// <summary>Nothing chosen while something could be studied: the top bar nudges the player.</summary>
    public bool ResearchIdle => IsReady && State.Nat[Viewer].Researching < 0 && Techs.HasOpen(State.Nat[Viewer]);
    /// <summary>Technologies of the current era known / needed to move on (needed 0 = the era has no gate yet).</summary>
    public (int known, int needed, int total) EraKnowledge
    {
        get
        {
            if (!IsReady) return (0, 0, 0);
            var nat = State.Nat[Viewer];
            return (Techs.KnownIn(nat, nat.Era), Techs.Required(nat.Era), Techs.CountIn(nat.Era));
        }
    }

    public void Research(int t)
    {
        if (!IsReady) return;
        if (!Techs.Open(State.Nat[Viewer], t)) { ShowRefusal("Это пока нельзя изучать"); return; }
        if (Issue(Cmd.Research(Viewer, t)) != 0) ShowRefusal("Это пока нельзя изучать");   // Issue raised ResearchChanged
    }

    internal void RaiseResearchChanged() => ResearchChanged?.Invoke();

    /// <summary>Taxes of p per rules cycle in gold (same formula as the treasury income).</summary>
    public double ProvinceTax(int p) => IsReady && Valid(p) ? Rules.ProvinceTax(State, p) / 100.0 : 0;

    bool Valid(int p) => (uint)p < (uint)World.P;

    // ------------------------------------------------------------------ the tribe (nomad phase)

    /// <summary>Raised when a tribe moved a province, set out, stopped or settled (panel, map, top bar).</summary>
    public event Action TribeChanged;
    internal void RaiseTribeChanged() => TribeChanged?.Invoke();

    /// <summary>The local tribe's camp, or -1 once the capital stands.</summary>
    public int Camp => IsReady ? State.Nat[Viewer].Camp : -1;
    public bool IsNomad => Camp >= 0;
    public NationState Me => State?.Nat[Viewer];

    /// <summary>Real seconds (at the current speed) until the elders settle by themselves; 0 when due.</summary>
    public int ElderSeconds => !IsReady ? 0 : (int)Math.Max(0, (Nomads.AutoTicks - State.Tick) / Clock.TicksPerSecond[State.Speed]);

    /// <summary>Real seconds (current speed) until the walking tribe arrives, -1 when it stands.</summary>
    public int TribeArrivalSeconds
    {
        get
        {
            var n = Me;
            if (n == null || n.CampPath == null) return -1;
            long ticks = (long)(n.CampPath.Length - 1 - n.CampStep) * Nomads.StepTicks - n.CampSub;
            return (int)Math.Ceiling(ticks / (double)Clock.TicksPerSecond[State.Speed]); // pax-allow: UI
        }
    }

    public Nomads.SiteParts SiteOf(int p) => IsReady ? Nomads.Site(World, State, Viewer, p) : default;
    public List<int> BestSites(int count = 3) => IsReady ? Nomads.BestSites(World, State, Viewer, count) : new List<int>();

    public string SettleProblem(int p) => !IsReady ? "Мир ещё не создан" : SettleText(Nomads.CheckSettle(World, State, Viewer, p));

    static string SettleText(SettleError e) => e switch
    {
        SettleError.None => null,
        SettleError.Settled => "Столица уже основана",
        SettleError.NotLand => "Очаг разводят только на суше",
        SettleError.Owned => "Эти земли уже чьи-то",
        SettleError.TooClose => $"Слишком близко к чужому городу: нужно не меньше {Cities.MinCityDistance} провинций",
        _ => "Здесь осесть нельзя",
    };

    static string MoveText(TribeMoveError e) => e switch
    {
        TribeMoveError.Settled => "Род уже осел",
        TribeMoveError.Sea => "Племя не ходит по морю",
        TribeMoveError.Here => "Род уже здесь",
        TribeMoveError.Far => "Туда нет пути по суше",
        TribeMoveError.Unexplored => "Сначала разведайте эти земли",
        _ => "Туда не пройти",
    };

    /// <summary>Aim the tribe: the next map click is where it walks (Esc / right click cancel).</summary>
    public void BeginTribeTargeting()
    {
        if (!IsReady || !IsNomad) return;
        if (IsTargeting) { CancelScoutTargeting(); return; }
        IsTargeting = true;
        TargetingTribe = true;
        TargetingChanged?.Invoke(true);
        ShowToast("Куда вести род? Выберите место на карте · Esc — отмена", PickToastSeconds, ToastKind.Pick);
    }

    public bool MoveTribe(int p)
    {
        if (!IsReady) return false;
        bool picking = IsTargeting && TargetingTribe;
        var err = Nomads.CheckMove(World, State, Viewer, p, out _);
        if (err == TribeMoveError.None) err = (TribeMoveError)Issue(Cmd.TribeTo(Viewer, p));
        if (err != TribeMoveError.None)
        {
            if (picking) { ShowToast(MoveText(err), PickToastSeconds, ToastKind.Error); return false; }
            ShowRefusal(MoveText(err));
            return false;
        }
        if (picking) EndTargeting();
        ShowToast($"Род снялся со стоянки и идёт к провинции {World.PName[p]}");
        TribeChanged?.Invoke();
        return true;
    }

    public void HaltTribe()
    {
        if (!IsReady || !IsNomad || Me.CampPath == null) return;
        Issue(Cmd.TribeTo(Viewer, -1));
        ShowToast("Род остановился");
        TribeChanged?.Invoke();
    }

    /// <summary>Found the capital on the camp; the chosen legend (-1 = none) becomes the myth.</summary>
    public void Settle(int myth)
    {
        if (!IsReady) return;
        var why = SettleProblem(Camp);
        if (why != null) { ShowRefusal(why); return; }
        int camp = Camp;
        if (IsTargeting) EndTargeting();
        int r = Issue(Cmd.Settle(Viewer, myth));
        if (r != 0) { ShowRefusal(SettleText((SettleError)r) ?? "Здесь осесть нельзя"); return; }
        TribeChanged?.Invoke();
        Select(camp);
    }

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
        TargetingTribe = false;
        TargetingChanged?.Invoke(true);
        ShowToast("Выберите цель для разведчиков · Esc — отмена", PickToastSeconds, ToastKind.Pick);
    }

    public void CancelScoutTargeting()
    {
        if (!IsTargeting) return;
        bool tribe = TargetingTribe;
        EndTargeting();
        ShowToast(tribe ? "Род остаётся на месте" : "Отправка разведчиков отменена");
    }

    /// <summary>A map click in targeting mode: scouts or the tribe, whichever is being aimed.</summary>
    public void PickTarget(int p)
    {
        if (TargetingTribe) MoveTribe(p); else SendScout(p);
    }

    void EndTargeting()
    {
        IsTargeting = false;
        TargetingTribe = false;
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
