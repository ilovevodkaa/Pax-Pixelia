using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// The right-hand card for the selected province. Variants: unexplored (fog), sea zone, unclaimed tribes (claim),
/// foreign nation, and own province (stats, class bar, buildings + build menu, ore survey, capital-only scouts and
/// construction queue). Sticky header (dithered band, kicker, spaced pixel title, owner-colour bar), scrolling body
/// with a Bayer-dithered bottom fade and a thin square overlay scrollbar.
/// Rebuilt on selection / ownership / fog changes — never while a mouse button is held, so a press on one of its
/// buttons is not lost; per-year values, button states and the scout rows update in place (<see cref="_live"/>).
/// </summary>
public partial class ProvincePanel : PanelContainer
{
    public const int Width = 360;
    const int Gutter = 12;
    static int Top => TopBar.Height + 12;

    public int Province { get; private set; } = -1;

    readonly Box _headBox = St.Header(56).Pad(16, 12, 10, 12);
    readonly Label _kicker = Ui.Text("", "Kick");
    readonly Label _title = Ui.Text("", "PanelTitle");
    readonly TextureRect _titleIcon = Ui.Icon("crown", 1, Pal.Ac);
    readonly ColorRect _ownerBar = new() { CustomMinimumSize = new Vector2(48, 4), SizeFlagsHorizontal = SizeFlags.ShrinkBegin, MouseFilter = MouseFilterEnum.Ignore };
    readonly Label _sub = Ui.Text("", "Sub");
    readonly PanelContainer _head;
    readonly ScrollContainer _scroll;
    readonly VBoxContainer _body = Ui.VBox(0);
    readonly MarginContainer _bodyMargin;
    readonly BottomFade _fade = new();
    readonly ThinScrollBar _bar;
    readonly List<Action> _live = new();

    // capital-only scouts section: built once per Rebuild, updated in place (a rebuild under the cursor eats clicks)
    VBoxContainer _scouts, _scoutRows;
    Control _scoutRowsGap;
    Label _scoutAside;
    TextButton _scoutPick;
    Button _scoutAuto;
    readonly List<(Label text, Label meta)> _scoutRowLabels = new();
    bool _rebuildPending;
    bool _buildOpen;
    int _fogAtBuild = -1;
    int _restoreScroll = -1;
    float _maxHeight = 600;
    float _slide;   // slide-in offset (px), eased to 0

    public ProvincePanel()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", St.Card().Pad(2));
        CustomMinimumSize = new Vector2(Width, 0);

        var close = Ui.IconButton("x", "X", 26, 26, 1, () => Game.I.Select(-1)).Tip("Закрыть", null, "Esc");
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        _kicker.Uppercase = true;
        _title.Uppercase = true;
        _titleIcon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var titleRow = Ui.HBox(8, _title, _titleIcon);
        var col = Ui.VBox(0, _kicker, Ui.Gap(0, 4), titleRow, Ui.Gap(0, 6), _ownerBar, Ui.Gap(0, 8), _sub).Grow();
        _head = Ui.Panel(_headBox, Ui.HBox(8, col, close));

        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,   // scrolls; the thin bar below is drawn over it
            MouseFilter = MouseFilterEnum.Pass,
        };
        _bodyMargin = Ui.Margin(_body, 16, 0, 16, 16);
        _bodyMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_bodyMargin);
        var stack = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        stack.AddChild(_scroll);
        _fade.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        stack.AddChild(_fade);
        _bar = new ThinScrollBar(_scroll) { SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
        stack.AddChild(_bar);
        AddChild(Ui.VBox(0, _head, stack));
    }

    // ---------------- open / close ----------------
    public void Open(int p)
    {
        if (!Game.I.IsReady || p < 0) { Close(); return; }
        bool wasOpen = Visible;
        if (p != Province) { _buildOpen = false; _restoreScroll = 0; }
        Province = p;
        Rebuild();
        Visible = true;
        if (!wasOpen) SlideIn();
    }

    public void Close()
    {
        Visible = false;
        Province = -1;
        _live.Clear();
    }

    void SlideIn()
    {
        _slide = 8;
        Modulate = new Color(1, 1, 1, 0);
    }

    internal string TitleText => _title.Text;
    internal void DebugOpenBuild() { _buildOpen = true; Rebuild(); }
    internal void DebugScroll(int px) => _scroll.ScrollVertical = px;

    public void SetViewport(Vector2 size)
    {
        _maxHeight = size.Y - Top - 12;
    }

    // ---------------- events ----------------
    /// <summary>Values that change with time (gold → button states, population, queue): in place, nothing rebuilt.</summary>
    public void RefreshLive()
    {
        if (!Visible) return;
        foreach (var a in _live) a();
        RefreshScouts();
    }
    /// <summary>Rebuild only when the change touches this province or a neighbour (claimability depends on them).</summary>
    public void OnProvincesChanged(IReadOnlyList<int> changed)
    {
        if (!Visible || !Game.I.IsReady) return;
        if (changed == null) { RequestRebuild(); return; }
        var adj = Game.I.World.Adj[Province];
        foreach (int q in changed)
            if (q == Province || Array.IndexOf(adj, q) >= 0) { RequestRebuild(); return; }
        foreach (var a in _live) a();
    }
    public void OnTargetingChanged() { if (Visible) RefreshScouts(); }
    public void OnScoutsChanged() { if (!Visible) return; RefreshScouts(); foreach (var a in _live) a(); }
    public void OnFogChanged()
    {
        if (!Visible || !Game.I.IsReady) return;
        if (FogOf(Province) != _fogAtBuild) RequestRebuild(); else RefreshScouts();
    }

    /// <summary>A rebuild frees the buttons: wait while a mouse button is held (the press would lose its release).</summary>
    void RequestRebuild()
    {
        if (Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right)) _rebuildPending = true;
        else Rebuild();
    }

    static int FogOf(int p) { var s = Game.I.State; return !s.FogEnabled || s.Fog == null ? 2 : s.Fog[p]; }

    // ---------------- layout: shrink to content, scroll beyond the column height ----------------
    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (_rebuildPending && !Input.IsMouseButtonPressed(MouseButton.Left) && !Input.IsMouseButtonPressed(MouseButton.Right)) Rebuild();
        float head = _head.GetCombinedMinimumSize().Y;
        float content = _bodyMargin.GetCombinedMinimumSize().Y;
        float h = Mathf.Max(40, Mathf.Min(content, _maxHeight - head - 2));
        if (Mathf.Abs(_scroll.CustomMinimumSize.Y - h) > .5f) _scroll.CustomMinimumSize = new Vector2(0, h);
        // explicit geometry: the column keeps its right gutter whatever the content does
        float k = 1 - Mathf.Exp(-(float)delta * 22);
        _slide = _slide < .3f ? 0 : Mathf.Lerp(_slide, 0, k);
        if (Modulate.A < 1) Modulate = new Color(1, 1, 1, Mathf.MoveToward(Modulate.A, 1, (float)delta * 5));
        Size = new Vector2(Width, GetCombinedMinimumSize().Y);
        Position = new Vector2(GetParentAreaSize().X - Size.X - Gutter + Mathf.Round(_slide), Top);
        if (_restoreScroll >= 0 && content > 0) { _scroll.ScrollVertical = _restoreScroll; _restoreScroll = -1; }
        var bar = _scroll.GetVScrollBar();
        _fade.Visible = bar.MaxValue > bar.Page + 1 && bar.Value + bar.Page < bar.MaxValue - 2;
    }

    // ---------------- content ----------------
    internal void Rebuild()
    {
        int p = Province;
        if (p < 0 || !Game.I.IsReady) return;
        if (_restoreScroll < 0) _restoreScroll = (int)_scroll.ScrollVertical;
        _rebuildPending = false;
        Ui.Clear(_body);
        _live.Clear();
        _scouts = null;
        _scoutRowLabels.Clear();

        var w = Game.I.World; var s = Game.I.State;
        int f = FogOf(p);
        _fogAtBuild = f;
        var flow = new Flow(_body, 14);
        bool land = w.PLand[p] == 1;
        int owner = s.VisibleOwner(p);
        string sub = $"{w.TerrainName(p)} · климат {Data.Climate(w.PBiome[p])}{(w.PRiver[p] != 0 ? " · река" : "")}{(w.PCoast[p] != 0 ? " · побережье" : "")}";

        bool cap = s.CapitalOf[p] >= 0;
        // fog: nothing about the place is told — not even land or sea (the terrain class would leak through the sub-line)
        if (f == 0) { Head("Туман войны", "Неизведанные земли", "Что там — узнают разведчики", "cloud-fog", Pal.Mu2); FogBody(flow, p, land); }
        else if (!land) { Head("Морская зона", w.PName[p], "Рыба и морские пути", "anchor", Pal.Info); SeaBody(flow, p, f == 1); }
        else if (owner < 0) { Head("Ничья земля", w.PName[p], sub, null, Pal.Unowned); UnownedBody(flow, p, f == 1); }
        else
        {
            string who = Game.I.Nations[owner].Name + (cap ? " · столица" : owner == GameState.LocalPlayer ? " · провинция" : "");
            Head(who, w.PName[p], sub, cap ? "crown" : null, Pal.Nation(owner));
            if (owner == GameState.LocalPlayer) OwnBody(flow, p); else ForeignBody(flow, p, owner, f == 1);
        }
    }

    void Head(string kicker, string title, string sub, string icon, Color bar)
    {
        _kicker.Text = kicker;
        _title.Text = title;
        // long names step down to the next size instead of pushing the card wider than its column
        const float room = Width - 4 - 16 - 10 - 8 - 26 - 8 - 13;   // frame, paddings, close button, crown
        bool fits = UiFonts.Width(UiFonts.Spaced(1), title.ToUpperInvariant(), UiFonts.Title) <= room;
        _title.Sized(fits ? UiFonts.Title : UiFonts.Value);
        _sub.Text = sub;
        _titleIcon.Visible = icon != null;
        if (icon != null) _titleIcon.Texture = Icons.Get(icon, 1);
        _ownerBar.Color = bar;
    }

    /// <summary>Population estimate; for a stale province nobody knows it now.</summary>
    Label LivePop(int p, bool stale)
    {
        if (stale) return Ui.Text("нет сведений", "Mu");
        var l = Kit.Value("≈" + Fmt.Int(Game.I.State.Pop[p]));
        _live.Add(() => l.Text = "≈" + Fmt.Int(Game.I.State.Pop[p]));
        return l;
    }

    /// <summary>Unexplored: nothing about the place is told — not even whether it is land (the scouts will find out).</summary>
    void FogBody(Flow flow, int p, bool land)
    {
        flow.Add(Kit.Para("Здесь могут быть племена, ресурсы и чужие державы. Туман рассеивается там, где проходят ваши разведчики, границы и торговые пути."), 10);
        var send = Ui.Button("Отправить разведчиков сюда", "map-search", "Pri", () => Game.I.SendScout(p), 1, 34);
        flow.Add(Kit.Acts(send), 14);
        var fine = flow.Add(Kit.Para("", true, UiFonts.Small), 8);
        void Sync()
        {
            int n = Game.I.State.Scouts.Count, max = n + Game.I.FreeScouts;
            Ui.Enable(send, Game.I.FreeScouts > 0);
            fine.Text = $"Разведчиков в пути: {n} / {max}";
        }
        Sync();
        _live.Add(Sync);
    }

    void SeaBody(Flow flow, int p, bool stale)
    {
        var w = Game.I.World; var s = Game.I.State;
        if (stale) flow.Add(Kit.Stale(), 0, 10);
        int routes = 0;
        foreach (var r in s.Routes) if (Array.IndexOf(r, p) >= 0) routes++;
        flow.Add(Kit.Grid(("Рыбные угодья", Kit.Value(w.PSize[p] < 1400 ? "Богатые" : "Обычные")), ("Торговые пути", Kit.Value(routes.ToString()))), 12);
        flow.Add(Kit.Para("Морские зоны дают рыбу прибрежным провинциям и связывают торговые пути. Флот появится во втором этапе."), 12);
    }

    void UnownedBody(Flow flow, int p, bool stale)
    {
        var w = Game.I.World; var s = Game.I.State;
        if (stale) flow.Add(Kit.Stale(), 0, 10);
        flow.Add(Kit.Grid(("Кочевые племена", LivePop(p, stale)), ("Плодородие", Kit.Fertility(w.PFert[p]))), 0);
        flow.Add(Kit.H4("Присоединение"), 20, 10);

        bool near = false;
        foreach (var q in w.Adj[p]) if (s.Owner[q] == GameState.LocalPlayer) { near = true; break; }
        if (!near)
        {
            flow.Add(Kit.Para("Слишком далеко от ваших границ. Сначала присоедините соседние земли."), 0);
            return;
        }
        string nation = Game.I.Nations[GameState.LocalPlayer].Name;
        flow.Add(Kit.Para($"Граничит с державой {nation}. Племена можно убедить войти в её состав."), 0, 8);
        var claim = Ui.Button($"Присоединить · {Game.ClaimCost} золота", "flag", "Pri", () => Game.I.Claim(p), 1, 34);
        claim.Tip(t => t.Title("Присоединение").Line("Провинция войдёт в состав державы вместе с племенами.")
            .Kv("Стоимость", $"{Game.ClaimCost} золота").Kv("В казне", Fmt.Int(Game.I.State.Gold), Game.I.State.Gold >= Game.ClaimCost ? Pal.Ok : Pal.Bad));
        flow.Add(claim, 8);
        var note = flow.Add(Kit.Para("", true, UiFonts.Small), 8);
        void Sync()
        {
            string why = Game.I.ClaimProblem(p);
            Ui.Enable(claim, why == null);
            note.Visible = why != null;
            if (why != null) note.Text = why;
        }
        Sync();
        _live.Add(Sync);
        FoundButton(flow, p);
    }

    /// <summary>«Город»: for a city — its sphere (provinces / cap, time to the next province); for another own province —
    /// the city it belongs to and the «Основать город» button.</summary>
    void CitySection(Flow flow, int p)
    {
        var w = Game.I.World;
        var info = Game.I.CityInfo(p);
        if (info.city < 0) return;
        if (info.city != p)
        {
            flow.Add(Kit.Kv(("Город", w.PName[info.city], null)), 12);
            FoundButton(flow, p);
            return;
        }
        flow.Add(Kit.H4("Сфера города", "", out var aside), 20, 10);
        var status = flow.Add(Kit.Para(""), 0);
        void Sync()
        {
            var i = Game.I.CityInfo(p);
            aside.Text = $"{i.count} / {i.cap} {Fmt.Plural(i.cap, "провинция", "провинции", "провинций")}";
            status.Text = i.cyclesToNext >= 0
                ? $"Город сам присоединяет свободные земли вокруг. Следующая — примерно через {Math.Max(1, (int)Math.Ceiling(Game.I.CyclesToSeconds(i.cyclesToNext)))} с"
                : i.count >= i.cap
                    ? $"Предел города — {i.cap} {Fmt.Plural(i.cap, "провинция", "провинции", "провинций")}. Чтобы расти дальше, основайте новый город"
                    : "Свободные земли вокруг освоены. Чтобы расти дальше, основайте новый город";
        }
        Sync();
        _live.Add(Sync);
    }

    /// <summary>What a building gives (the rules: Simulation.Capacity, Moods, Rules.ProvinceTax/ProvinceMaterials, Cities.Influence).
    /// Short: a few words beside the building's name in the list; full: the build menu's tooltip.</summary>
    static string BuildingEffect(GameState s, int p, Data.Bld b, bool full = false)
    {
        bool city = Cities.IsCity(s, p), mine = Rules.KnownOre(s, p) is Rules.OreCopper or Rules.OreTin or Rules.OreIron;
        return b switch
        {
            Data.Bld.Farm => full ? "Предел населения провинции +19%" : "население +19%",
            Data.Bld.Lumber => full ? $"+{Rules.LumberMaterials} материала за цикл. Строится без материалов" : $"+{Rules.LumberMaterials} материала",
            Data.Bld.Quarry => mine
                ? full ? $"+{Rules.QuarryMaterials + Rules.MineMaterials} материала за цикл: под ней жила, это рудник" : $"+{Rules.QuarryMaterials + Rules.MineMaterials} материала · рудник"
                : full ? $"+{Rules.QuarryMaterials} материала за цикл, на разведанной жиле меди, олова или железа ещё +{Rules.MineMaterials}. Строится без материалов" : $"+{Rules.QuarryMaterials} материала",
            Data.Bld.Fishery => full ? "Предел населения провинции +9%" : "население +9%",
            Data.Bld.Pasture => full ? "Предел населения провинции +6%" : "население +6%",
            Data.Bld.Shrine => full ? "Наука, довольство +8" + (city ? ", город растёт быстрее" : "") : "наука · довольство +8",
            Data.Bld.Market => full ? "Налоги +0,5 за цикл, довольство +3" + (city ? ", город растёт быстрее" : "") : "налоги · довольство +3",
            Data.Bld.Granary => full ? "Предел населения провинции +13%, довольство +4" : "население +13%",
            _ => "",
        };
    }

    static string OreEffect(GameState s, int p, int ore) => ore switch
    {
        Rules.OreGold => "+1 золото за цикл",
        Rules.OreSalt => $"довольство +{Rules.SaltMood}",
        _ => Rules.IsMine(s, p) ? $"рудник: +{Rules.MineMaterials} материала" : $"каменоломня даст ещё +{Rules.MineMaterials}",
    };

    void FoundButton(Flow flow, int p)
    {
        var w = Game.I.World;
        var found = Ui.Button($"Основать город · {Game.FoundCityCost} золота", "home", null, () => Game.I.FoundCity(p), 1, 34);
        found.Tip(t =>
        {
            t.Title("Основать город").Line("Новый город получит свою сферу и сам будет присоединять земли вокруг.");
            var (src, people) = Game.I.FoundCityPlan(p);
            if (src >= 0) t.Kv("Поселенцы", $"{Fmt.Int(people)} из {w.PName[src]}");
            t.Kv("Стоимость", $"{Game.FoundCityCost} золота · {Game.FoundCityMaterials} материалов")
             .Kv("В казне", Fmt.Int(Game.I.State.Gold), Game.I.State.Gold >= Game.FoundCityCost ? Pal.Ok : Pal.Bad)
             .Kv("На складе", Fmt.Int(Game.I.State.Materials), Game.I.State.Materials >= Game.FoundCityMaterials ? Pal.Ok : Pal.Bad);
        });
        flow.Add(found, 10);
        var note = flow.Add(Kit.Para("", true, UiFonts.Small), 8);
        void Sync()
        {
            string why = Game.I.FoundCityProblem(p);
            Ui.Enable(found, why == null);
            note.Visible = why != null;
            if (why != null) note.Text = why;
        }
        Sync();
        _live.Add(Sync);
    }

    void ForeignBody(Flow flow, int p, int o, bool stale)
    {
        var s = Game.I.State;
        var n = Game.I.Nations[o];
        if (stale) flow.Add(Kit.Stale(), 0, 10);
        flow.Add(Kit.Own(Kit.Tag(n.Gov)));
        var relation = o == 1 ? Kit.Value("Настороженные", Pal.Warn) : Kit.Value("Нейтральные", Pal.Mu);
        flow.Add(Kit.Grid(("Население", LivePop(p, stale)), ("Отношения", relation), ("Вера", Kit.Faith(s.Religion[p])), ("Культура", Kit.Value(n.CultureAdj))), 12);
        flow.Add(Kit.Acts(
            Ui.Button("Предложить сделку", "scale", null, () => Game.I.ShowToast("Окно сделки — отдельный экран, нарисуем следующим")),
            Ui.Button("Торговый путь", "route", null, () => Game.I.ShowToast("Торговый путь будет проложен по суше и морю"))), 14);
    }

    void OwnBody(Flow flow, int p)
    {
        var w = Game.I.World; var s = Game.I.State;
        bool capital = s.CapitalOf[p] == GameState.LocalPlayer;

        var pop = Kit.Value(Fmt.Int(s.Pop[p]));
        var mood = Kit.Value(s.Mood[p] + "%");
        var tax = Kit.Value(Fmt.Signed(Game.I.ProvinceTax(p), 1));
        void SyncStats()
        {
            pop.Text = Fmt.Int(s.Pop[p]);
            mood.Text = s.Mood[p] + "%";
            if (s.Mood[p] > 60) mood.Colored(Pal.Ok); else mood.RemoveThemeColorOverride("font_color");
            tax.Text = Fmt.Signed(Game.I.ProvinceTax(p), 1);
        }
        SyncStats();
        _live.Add(SyncStats);
        flow.Add(Kit.Grid(("Население", pop), ("Довольство", mood), ("Плодородие", Kit.Fertility(w.PFert[p])), ("Налоги", Kit.ValueUnit(tax, "за цикл"))), 0);
        CitySection(flow, p);

        if (capital) { flow.Add(BuildScouts(), 20); RefreshScouts(); }

        // population classes (ancient era): the mockup's deterministic split around 62/18/12/8
        var classes = Data.AncientClasses;
        int[] baseSplit = { 62, 18, 12, 8 };
        var parts = new float[classes.Length]; var colors = new Color[classes.Length]; var names = new string[classes.Length];
        float sum = 0;
        for (int k = 0; k < classes.Length; k++)
        {
            parts[k] = baseSplit[k] + (int)(Core.Noise.H2(p, k, w.Seed) * 6) - 3;
            sum += parts[k]; colors[k] = Pal.PopClass(k); names[k] = classes[k].Name;
        }
        var pcts = new int[classes.Length];
        for (int k = 0; k < classes.Length; k++) pcts[k] = Mathf.RoundToInt(parts[k] / sum * 100);
        flow.Add(Kit.H4("Население", $"{classes.Length} {Fmt.Plural(classes.Length, "класс", "класса", "классов")}"), 20, 10);
        flow.Add(new ClassBar(parts, colors));
        flow.Add(Kit.Legend(names, colors, pcts), 9);
        int rel = s.Religion[p];
        flow.Add(Kit.Kv(("Культура", Game.I.Nations[GameState.LocalPlayer].CultureAdj, null),
            ("Вера", rel >= 0 ? Data.Religions[rel].Name : "—", rel >= 0 ? Pal.Religion(rel) : null)), 10);

        // buildings
        var blds = s.Buildings[p];
        flow.Add(Kit.H4("Постройки", $"{blds.Count} / {s.Slots[p]}"), 20, 10);
        foreach (var b in blds)
            flow.Add(Kit.Row(BuildingIcon(b), Data.BldName[(int)b], BuildingEffect(s, p, b)), 0, 5);
        if (blds.Count < s.Slots[p])
        {
            flow.Add(Kit.Slot("Свободный участок — построить", () => { _buildOpen = !_buildOpen; Rebuild(); }), 0, 5);
            if (_buildOpen)
            {
                var buttons = new List<Button>();
                var open = new List<Data.Bld>(Game.I.BuildOptions(p));
                var menu = new List<Data.Bld>(open);
                menu.AddRange(Game.I.LockedBuildOptions(p));   // not known yet: shown greyed with what opens them
                foreach (var b in menu)
                {
                    if (blds.Contains(b)) continue;
                    var bb = b;
                    bool locked = !open.Contains(b);
                    int cost = Game.I.BuildCost(b), mat = Game.I.BuildMaterials(b);
                    var btn = Ui.Button(Data.BldName[(int)b], BuildingIcon(b), "Menu", () => { _buildOpen = false; Game.I.Build(p, bb); Rebuild(); }, 1, 28);
                    btn.Tip(t =>
                    {
                        var st = Game.I.State;
                        t.Title(Data.BldName[(int)bb]).Line(BuildingEffect(st, p, bb, full: true)).Kv("Стоимость", mat > 0 ? $"{cost} золота · {mat} материалов" : $"{cost} золота");
                        if (locked) { t.Kv("Закрыто", Game.I.BuildProblem(p, bb) ?? "", Pal.Bad); return; }
                        t.Kv("В казне", Fmt.Int(st.Gold), st.Gold >= cost ? Pal.Ok : Pal.Bad);
                        if (mat > 0) t.Kv("На складе", Fmt.Int(st.Materials), st.Materials >= mat ? Pal.Ok : Pal.Bad);
                    });
                    void SyncBuild() => Ui.Enable(btn, Game.I.BuildProblem(p, bb) == null);   // gold and materials arrive every cycle
                    SyncBuild();
                    _live.Add(SyncBuild);
                    buttons.Add(btn);
                }
                if (buttons.Count > 0) flow.Add(Kit.Menu(buttons), 0, 6);
                else flow.Add(Kit.Para("Здесь пока нечего строить: нужны другие земли.", true, UiFonts.Small), 0, 6);
                if (open.Count == 0 && menu.Count > 0)
                    flow.Add(Kit.Para("Постройки откроют технологии: кнопка с атомом на верхней панели.", true, UiFonts.Small), 0, 6);
            }
        }

        // ore
        flow.Add(Kit.H4("Недра"), 20, 10);
        int ore = s.Ore[p];
        if (s.OreFound[p] && ore >= 0) flow.Add(Kit.Row("diamond", Data.Ores[ore], OreEffect(s, p, ore)), 0, 5);
        else if (s.OreFound[p]) flow.Add(Kit.Para("Геологи обошли провинцию: залежей не найдено"), 0);
        else if (!Game.I.MayHaveOre(p)) flow.Add(Kit.Para("Холмов и гор нет — залежей не ожидается"), 0);
        else
        {
            var survey = Ui.Button("Отправить геологов", "shovel", "Sm", () => { Game.I.Survey(p); Rebuild(); }, 1, 26);
            survey.Tip(t =>
            {
                t.Title("Геологическая разведка").Line("Геологи осмотрят холмы и найдут залежи, если они есть.")
                 .Kv("Стоимость", $"{Game.SurveyCost} золота").Kv("В казне", Fmt.Int(Game.I.State.Gold), Game.I.State.Gold >= Game.SurveyCost ? Pal.Ok : Pal.Bad);
                if (Game.I.SurveyProblem(p) is string why) t.Kv("Нельзя", why, Pal.Bad);
            });
            void SyncSurvey() => Ui.Enable(survey, Game.I.SurveyProblem(p) == null);
            SyncSurvey();
            _live.Add(SyncSurvey);
            flow.Add(Kit.Row("help", "Не разведаны", null, survey, mutedText: true), 0, 5);
        }

        // construction queue of the capital
        if (capital)
        {
            flow.Add(Kit.H4("Строится", "", out var pct), 20, 10);
            var name = Ui.Text("", "Strong");
            flow.Add(Kit.Row("hammer", null, textLabel: name), 0, 5);
            var prog = flow.Add(new Progress(), 7);
            void SyncQueue()
            {
                bool idle = s.ProjectIndex < 0;   // everything the capital can build is built
                pct.Text = idle ? "" : s.QueuePct + "%";
                name.Text = idle ? "Все работы в столице завершены" : s.QueueName;
                prog.Visible = !idle;
                prog.Value = s.QueuePct / 100f;
            }
            SyncQueue();
            _live.Add(SyncQueue);
        }
    }

    /// <summary>Capital-only «Разведчики» section: header, one row per party, and the two buttons, which stay the same
    /// controls for as long as the panel shows the capital (see <see cref="RefreshScouts"/>).</summary>
    Control BuildScouts()
    {
        _scoutRows = Ui.VBox(5);
        _scoutRowsGap = Ui.Gap(0, 8);
        _scoutPick = (TextButton)Ui.Button("Отправить разведчиков", "map-search", null, () =>
        {
            if (Game.I.IsTargeting) Game.I.CancelScoutTargeting(); else Game.I.BeginScoutTargeting();
        });
        _scoutPick.Tip(t =>
        {
            if (Game.I.IsTargeting) t.Title("Отменить выбор цели").Mu("Esc");
            else t.Title("Отправить разведчиков").Line("Щёлкните по неизведанной провинции на карте.").Mu("Разведчики ходят только по суше");
        });
        _scoutAuto = Ui.Button("Авто", "compass", null, () => Game.I.SendScoutAuto());
        _scoutAuto.Tip("Автоматическая разведка", "Разведчики сами пойдут к ближайшим неизведанным землям.");
        var acts = Kit.Acts(_scoutPick, _scoutAuto);
        _scoutPick.SizeFlagsStretchRatio = 1.6f;
        // the buttons sit above the party rows: a party leaving or returning must not move them under the cursor
        _scouts = Ui.VBox(0, Kit.H4("Разведчики", "", out _scoutAside), Ui.Gap(0, 10), acts, _scoutRowsGap, _scoutRows);
        return _scouts;
    }

    /// <summary>Scouts section in place: the aside, the party rows (recreated only when a party leaves or returns —
    /// otherwise their texts change) and the buttons' captions and states. Nothing under the cursor is freed.</summary>
    void RefreshScouts()
    {
        if (_scouts == null || !IsInstanceValid(_scouts) || !Game.I.IsReady) return;
        var w = Game.I.World; var s = Game.I.State;
        int n = s.Scouts.Count, free = Game.I.FreeScouts;
        _scoutAside.Text = $"в пути {n} / {n + free}";

        if (_scoutRowLabels.Count != n)
        {
            Ui.Clear(_scoutRows);
            _scoutRowLabels.Clear();
            for (int i = 0; i < n; i++)
            {
                var text = Ui.Text("", "Strong"); var meta = Ui.Text("", "SmallMu");
                _scoutRows.AddChild(Kit.Row("walk", null, textLabel: text, metaLabel: meta));
                _scoutRowLabels.Add((text, meta));
            }
        }
        _scoutRowsGap.Visible = n > 0;
        for (int i = 0; i < n; i++)
        {
            var sc = s.Scouts[i];
            if (sc.Path == null || sc.Path.Length == 0) continue;
            int target = sc.Path[^1], left = Math.Max(0, sc.Path.Length - 1 - sc.Step);
            var (text, meta) = _scoutRowLabels[i];
            text.Text = sc.Auto ? "Свободный поиск" : "Цель: " + (FogOf(target) > 0 ? w.PName[target] : "неизведанные земли");
            meta.Text = sc.Auto ? $"разведано {Game.I.ScoutFound(sc)}" : $"ещё {left} {Fmt.Plural(left, "провинция", "провинции", "провинций")}";
        }

        bool targeting = Game.I.IsTargeting;
        _scoutPick.Caption = targeting ? "Выберите цель на карте" : "Отправить разведчиков";
        _scoutPick.IconTexture = Icons.Get(targeting ? "crosshair" : "map-search", 1, !targeting);
        var skin = targeting ? "On" : "";
        if (_scoutPick.ThemeTypeVariation != skin) _scoutPick.ThemeTypeVariation = skin;
        Ui.Enable(_scoutPick, targeting || free > 0);
        Ui.Enable(_scoutAuto, free > 0);
    }

    public static string BuildingIcon(Data.Bld b) => b switch
    {
        Data.Bld.Farm => "plant", Data.Bld.Lumber => "trees", Data.Bld.Quarry => "pick", Data.Bld.Fishery => "anchor",
        Data.Bld.Pasture => "paw", Data.Bld.Shrine => "sun", Data.Bld.Market => "building-store", _ => "building-warehouse",
    };
}

/// <summary>Scroll hint at the bottom of the panel: the card colour creeping up in a 4×4 Bayer dither (no alpha ramp).</summary>
public partial class BottomFade : Control
{
    const int H = 28;
    static Texture2D _tex;

    public BottomFade()
    {
        CustomMinimumSize = new Vector2(0, H);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Draw()
    {
        _tex ??= PixelTex.DitherBand(Pal.A(Pal.Card, 0), Pal.Card, H, 2);
        DrawTextureRectRegion(_tex, new Rect2(0, 0, Size.X - 10, H), new Rect2(0, 0, Size.X - 10, H));
    }
}

/// <summary>
/// Thin overlay scrollbar of the panel: a square 4px thumb in the right padding, dim until hovered or dragged.
/// It takes no layout width, so the content stays centred.
/// </summary>
public partial class ThinScrollBar : Control
{
    const float Thumb = 4, Right = 4, MinThumb = 24;
    readonly ScrollContainer _scroll;
    bool _hover, _drag;
    float _grab;

    public ThinScrollBar(ScrollContainer scroll)
    {
        _scroll = scroll;
        CustomMinimumSize = new Vector2(Thumb + Right + 4, 0);
        MouseFilter = MouseFilterEnum.Pass;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
        _scroll.GetVScrollBar().ValueChanged += _ => QueueRedraw();
        _scroll.GetVScrollBar().Changed += QueueRedraw;
    }

    bool Overflows(out float top, out float len)
    {
        var bar = _scroll.GetVScrollBar();
        float h = Size.Y - 8, max = (float)bar.MaxValue, page = (float)bar.Page;
        top = len = 0;
        if (max <= page + 1 || h <= 0) return false;
        len = Mathf.Max(MinThumb, h * page / max);
        top = 4 + (h - len) * (float)(bar.Value / (max - page));
        return true;
    }

    public override void _Draw()
    {
        if (!Overflows(out float top, out float len)) return;
        var c = _drag ? Pal.Ac : _hover ? Pal.Ln3 : Pal.Ln2;
        float x = Size.X - Right - Thumb;
        DrawRect(new Rect2(x, Mathf.Round(top), Thumb, Mathf.Round(len)), c);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!Overflows(out float top, out float len)) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _drag = mb.Pressed;
                if (_drag)
                {
                    // grab the thumb where it was pressed; a press on the track first moves the thumb there
                    if (mb.Position.Y < top || mb.Position.Y > top + len) { top = mb.Position.Y - len / 2; ScrollTo(top, len); }
                    _grab = mb.Position.Y - top;
                }
                AcceptEvent();
                QueueRedraw();
                break;
            case InputEventMouseMotion mm when _drag:
                ScrollTo(mm.Position.Y - _grab, len);
                AcceptEvent();
                break;
        }
    }

    void ScrollTo(float thumbTop, float len)
    {
        var bar = _scroll.GetVScrollBar();
        double t = Mathf.Clamp((thumbTop - 4) / Mathf.Max(1, Size.Y - 8 - len), 0, 1);
        bar.Value = t * (bar.MaxValue - bar.Page);
    }
}
