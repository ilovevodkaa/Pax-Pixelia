using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.Map;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>
/// --savetest: saves and loads the real game (world, map, UI) and prints one PASS/FAIL line per check, then quits with
/// the number of failures as the exit code. Works in its own folder (user://savetest, emptied first).
/// Flow: play a little (claims, a building, scouts caught mid-walk, a few hundred cycles with events) → move the camera,
/// switch the map mode, select a province → save (thumbnail, time) → play on as the reference → load (time) → the
/// state, the journal and the view come back bit for bit and go on to the same hash → broken, foreign, too new and
/// missing files are refused without touching the running game; a world that differs is refused → three rotating
/// autosave slots, the save on leaving never overwrites the save being loaded → the calendar autosave fires →
/// the synchronous save (Alt+F4) → list, latest, delete. Budgets: save &lt; 300 ms of work, load &lt; 1.5 s with the world.
/// </summary>
public partial class SaveTest : Node
{
    const long SaveBudgetMs = 300, LoadBudgetMs = 1500;
    const int Ahead = 1200;   // ticks played after the save (300 cycles)

    readonly Main _main;
    int _pass, _fail;
    readonly List<string> _timings = new();

    Game G => Game.I;

    public SaveTest(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "SaveTest";
        SaveStore.Dir = Cli.Str("savetest-dir") ?? "user://savetest";
        if (Directory.Exists(SaveStore.Dir))
            foreach (var f in Directory.GetFiles(SaveStore.Dir)) File.Delete(f);
        Run();
    }

    async void Run()
    {
        GD.Print($"[savetest] start, folder {SaveStore.Dir}");
        try
        {
            while (!G.IsReady) await Frames(1);
            await Seconds(.6);
            var path = await SaveAndReload();
            if (path != null)
            {
                await Refusals(path);
                await Rotation(path);
                await CalendarAutosave();
                SyncSave();
                Listing();
            }
        }
        catch (Exception e) { Fail("exception", e.ToString()); }
        foreach (var t in _timings) GD.Print("  timing " + t);
        GD.Print($"[savetest] {(_fail == 0 ? "PASS" : "FAIL")} — {_pass} passed, {_fail} failed");
        GetTree().Quit(_fail);
    }

    // ------------------------------------------------------------------ save → play on / load → play on

    async Task<string> SaveAndReload()
    {
        var w = G.World; var s = G.State;
        // a game with some history: claims, a building, scouts, events
        for (int k = 0; k < 2; k++)
        {
            int best = -1;
            var fert = WorldFacts.Of(w).FertPm;
            for (int p = 0; p < w.P; p++) if (G.CanClaim(p) && (best < 0 || fert[p] > fert[best])) best = p;
            if (best >= 0) G.Claim(best);
        }
        int plot = Enumerable.Range(0, w.P).FirstOrDefault(p => s.Owner[p] == GameState.LocalPlayer && G.BuildOptions(p).Count > 0, -1);
        if (plot >= 0) G.Build(plot, G.BuildOptions(plot)[0]);
        G.FastForward(1600);   // 400 cycles: the deck deals, bots grow
        G.SendScoutAuto(); G.SendScoutAuto();
        for (int i = 0; i < 40 && !s.Scouts.Any(x => x.Nation == 0 && x.Sub > 0 && x.Sub < Scouts.SubSteps); i++) G.RunTicks(1);
        G.SetPaused(true);
        Check("setup: a scout of the player is mid-step", s.Scouts.Any(x => x.Nation == 0 && x.Sub > 0 && x.Sub < Scouts.SubSteps),
            string.Join(" ", s.Scouts.Where(x => x.Nation == 0).Select(x => $"{x.Step}/{x.Path.Length}:{x.Sub}")));
        Check("setup: events were dealt", s.Nat.Sum(n => n.EventCount) > 0, $"{s.Nat.Sum(n => n.EventCount)} cards");

        G.SetMode(MapMode.Religion);
        var capital = s.NationCapital[0];
        var target = new Vector2(w.PCX[capital] + 300, w.PCY[capital] - 120);
        _main.Camera.CenterOn(target, glide: false);
        G.RequestZoom(+1);
        await Seconds(.6);   // the zoom glide lands
        int selected = Enumerable.Range(0, w.P).First(p => s.Owner[p] == 0 && p != capital);
        G.Select(selected);
        await Frames(3);
        var camBefore = G.CameraRect.GetCenter();
        int zoomBefore = G.ZoomLevel;
        long playBefore = G.PlaytimeMs;
        var memory = G.MapMemoryWriter?.Invoke();

        var hash0 = s.Hash();
        var snap0 = SaveFile.Snapshot(s);
        int journal0 = G.Journal.Count;
        var r = await G.SaveAsync(SaveKind.Manual, "Проверка сохранения");
        Check("save: written", r.Ok && File.Exists(r.Path), r.Error ?? Path.GetFileName(r.Path));
        if (!r.Ok) return null;
        Check($"save: work < {SaveBudgetMs} ms", r.Ms < SaveBudgetMs, $"{r.Ms} ms, {r.Bytes / 1024} KB");
        _timings.Add($"save (async, rendered thumbnail): {r.Ms} ms of work, {r.Bytes / 1024} KB");
        Check("save: the state did not move while saving", s.Hash().Equals(hash0));
        var png = Path.ChangeExtension(r.Path, ".png");
        var img = File.Exists(png) ? Image.LoadFromFile(png) : null;
        Check("save: thumbnail 320×180", img != null && img.GetWidth() == SaveThumb.Width && img.GetHeight() == SaveThumb.Height,
            SaveThumb.LastWasCpu ? "CPU picture" : "rendered map");
        if (DisplayServer.GetName() != "headless") Check("save: the thumbnail is the rendered map", !SaveThumb.LastWasCpu);
        var header = SaveStore.List().FirstOrDefault(e => Same(e.Path, r.Path))?.Header;
        Check("save: header (name, nation, date, era, seed)", header != null && header.Name == "Проверка сохранения" && header.NationName == G.Nations[0].Name
            && header.DateText == G.DateText && header.Era == G.EraIndex && header.Seed == w.Seed && header.Tick == s.Tick,
            header == null ? "" : $"«{header.Name}» {header.NationName} · {header.DateText} · эпоха {header.Era} · зерно {header.Seed}");

        // the reference: the same game played on without a save in between
        G.RunTicks(Ahead);
        var refHash = G.State.Hash();
        var refSnap = SaveFile.Snapshot(G.State);

        var lr = await G.LoadGame(r.Path);
        Check("load: loaded", lr.Ok && G.IsReady, lr.Error);
        if (!lr.Ok) return null;
        Check($"load: < {LoadBudgetMs} ms with the world", G.LastLoadMs < LoadBudgetMs, $"{G.LastLoadMs} ms (world {G.LastGenerationMs} ms)");
        _timings.Add($"load: {G.LastLoadMs} ms (world regeneration {G.LastGenerationMs} ms)");
        s = G.State;
        Check("load: state hash as saved", s.Hash().Equals(hash0), $"{s.Hash()} vs {hash0}{(s.Hash().Equals(hash0) ? "" : " · " + s.Hash().Diff(hash0))}");
        Check("load: snapshot bytes as saved", SaveFile.Snapshot(s).AsSpan().SequenceEqual(snap0), $"{snap0.Length} bytes");
        Check("load: journal restored", G.Journal.Count == journal0, $"{G.Journal.Count} commands");
        Check("load: starts paused", s.Paused);
        Check("load: map mode restored", G.Mode == MapMode.Religion);
        Check("load: playtime restored", G.PlaytimeMs >= playBefore && G.PlaytimeMs - playBefore < 5000, $"{G.PlaytimeMs / 1000} s");
        Check("load: SavePath is the file", Same(G.SavePath, r.Path));
        await Frames(4);
        var camAfter = G.CameraRect.GetCenter();
        float dx = Mathf.PosMod(camAfter.X - camBefore.X + w.W / 2f, w.W) - w.W / 2f;
        Check("load: camera where it was", Mathf.Abs(dx) < 2 && Mathf.Abs(camAfter.Y - camBefore.Y) < 2 && G.ZoomLevel == zoomBefore,
            $"{camAfter} vs {camBefore}, zoom {G.ZoomLevel} vs {zoomBefore}");
        Check("load: selection restored", G.Selected == selected, $"{G.Selected} vs {selected}");
        var memoryAfter = G.MapMemoryWriter?.Invoke();
        Check("load: the map's memory of stale land restored", memory != null && memoryAfter != null && memory.AsSpan().SequenceEqual(memoryAfter));
        Check("load: the chapter card is gone", !_main.Hud.Loading.Visible || _main.Hud.Loading.Modulate.A < 1);

        G.RunTicks(Ahead);
        var h = G.State.Hash();
        Check($"load: {Ahead} ticks later = the uninterrupted game", h.Equals(refHash), $"{h} vs {refHash}{(h.Equals(refHash) ? "" : " · " + h.Diff(refHash))}");
        Check("load: … bit for bit (snapshot)", SaveFile.Snapshot(G.State).AsSpan().SequenceEqual(refSnap));
        return r.Path;
    }

    // ------------------------------------------------------------------ bad files

    async Task Refusals(string good)
    {
        var hash = G.State.Hash();
        var bytes = File.ReadAllBytes(good);
        string dir = SaveStore.Dir;
        var flip = (byte[])bytes.Clone();
        flip[^40] ^= 0x5A;
        var head = (byte[])bytes.Clone();
        head[30] ^= 0x01;
        var future = (byte[])bytes.Clone();
        future[4] = 0xFF;   // the format version (u16 after the magic)
        var bad = new (string Name, byte[] Data, SaveError Expect)[]
        {
            ("bad_body.pxs", flip, SaveError.Corrupt),
            ("bad_header.pxs", head, SaveError.Corrupt),
            ("truncated.pxs", bytes[..(bytes.Length / 2)], SaveError.Corrupt),
            ("empty.pxs", Array.Empty<byte>(), SaveError.NotASave),
            ("foreign.pxs", System.Text.Encoding.UTF8.GetBytes("not a save at all, just some text in a file"), SaveError.NotASave),
            ("future.pxs", future, SaveError.TooNew),
        };
        foreach (var b in bad) File.WriteAllBytes(Path.Combine(dir, b.Name), b.Data);
        var listed = SaveStore.List();
        // a damaged body passes the header-only read of the list; Check (the whole file) and loading refuse it
        Check("list: files that cannot be read are listed with the reason", bad.Where(b => b.Name is not ("bad_body.pxs" or "truncated.pxs"))
            .All(b => listed.Any(e => e.FileName == b.Name && !e.Ok && e.Error != null)), string.Join(", ", listed.Where(e => !e.Ok).Select(e => $"{e.FileName}: {e.Error}")));
        Check("check: damaged bodies are caught before loading", listed.Where(e => e.FileName is "bad_body.pxs" or "truncated.pxs").All(e => SaveStore.Check(e) != null));
        Check("latest: never a broken file", SaveStore.Latest() is { Ok: true } l && Same(l.Path, good), SaveStore.Latest()?.FileName);
        foreach (var b in bad.Append(("missing.pxs", null, SaveError.Missing)))
        {
            var lr = await G.LoadGame(Path.Combine(dir, b.Name));
            Check($"refused: {b.Name} ({b.Expect})", !lr.Ok && lr.Error != null && lr.Error.StartsWith(SaveException.Text(b.Expect)), lr.Error);
            Check($"refused: {b.Name} — the running game is untouched", G.IsReady && G.State.Hash().Equals(hash));
        }
        foreach (var b in bad) File.Delete(Path.Combine(dir, b.Name));

        // a world that comes out differently: refused after the regeneration (the running game is gone by then)
        var (h, body) = SaveStore.ReadFile(good);
        h.WorldHash ^= 0x1234;
        var other = Path.Combine(dir, "other_world.pxs");
        SaveStore.WriteFile(other, h, body, null);
        var r = await G.LoadGame(other);
        Check("refused: a world that differs", !r.Ok && r.Error == SaveException.Text(SaveError.WorldMismatch), r.Error);
        File.Delete(other);
        r = await G.LoadGame(good);
        Check("reload after a refusal", r.Ok && G.IsReady && G.State.Hash().All == SaveStore.ReadFile(good).Item1.StateHash, r.Error);
        await Frames(2);
    }

    static bool Same(string a, string b) => a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ autosave slots

    async Task Rotation(string manual)
    {
        await G.AutoSave.Autosave();
        await G.AutoSave.Autosave();
        await G.AutoSave.Autosave();
        var autos = AutoFiles();
        Check("autosave: three slots filled", autos.Length == 3, string.Join(" ", autos.Select(Path.GetFileName)));
        var oldest = autos.OrderBy(File.GetLastWriteTimeUtc).First();
        G.RunTicks(8);
        var fourth = await G.AutoSave.Autosave();
        Check("autosave: the fourth replaces the oldest slot", fourth.Ok && Same(fourth.Path, oldest) && AutoFiles().Length == 3,
            $"{Path.GetFileName(fourth.Path)} (oldest {Path.GetFileName(oldest)})");
        Check("autosave: the manual save is untouched", File.Exists(manual));
        // loading an autosave from the pause menu: the save on leaving must not overwrite the chosen slot
        var keep = AutoFiles().OrderBy(File.GetLastWriteTimeUtc).First();
        var stamp = File.GetLastWriteTimeUtc(keep);
        var leave = await G.AutoSave.SaveOnLeave(keep: keep);
        Check("save on leaving: never over the save being loaded", leave.Ok && !Same(leave.Path, keep) && File.GetLastWriteTimeUtc(keep) == stamp,
            $"{Path.GetFileName(leave.Path)} kept {Path.GetFileName(keep)}");
        var e = SaveStore.List().First(x => Same(x.Path, leave.Path));
        Check("save on leaving: kind Exit, default name", e.Header.Kind == SaveKind.Exit && e.Header.Name == G.DefaultSaveName(), e.Header.Name);
    }

    static string[] AutoFiles() => Directory.GetFiles(SaveStore.Dir, "auto_*" + SaveFile.Extension);

    async Task CalendarAutosave()
    {
        var a = G.AutoSave;
        bool enabled = a.Enabled; long gap = a.MinGapMs;
        a.Enabled = true; a.MinGapMs = 0;
        int before = a.Count;
        G.SetPaused(false);
        // well over ten calendar years
        long day = G.State.Day256;
        for (int i = 0; i < 400 && G.State.Day256 - day < 11 * 366 * Calendar.DayUnit; i++) G.RunTicks(10);
        await Frames(3);
        while (a.Busy) await Frames(1);
        Check("calendar autosave: fires after ten years", a.Count == before + 1, $"{(G.State.Day256 - day) / Calendar.DayUnit / 365} years, {a.Count - before} saves");
        a.MinGapMs = 3 * 60 * 1000;
        G.RunTicks(400);
        await Frames(3);
        while (a.Busy) await Frames(1);
        Check("calendar autosave: not again within the real-time gap", a.Count == before + 1);
        G.SetPaused(true);
        a.Enabled = enabled; a.MinGapMs = gap;
    }

    void SyncSave()
    {
        // the window's cross / Alt+F4: the OS close request reaches every node; AutoSaver saves before quitting
        G.AutoSave.QuitOnClose = false;
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        G.AutoSave.QuitOnClose = true;
        var r = G.AutoSave.LastCloseSave;
        Check("close request (Alt+F4): quit is held back until the game is saved", !GetTree().AutoAcceptQuit && r.Ok && File.Exists(r.Path ?? ""), r.Error ?? Path.GetFileName(r.Path));
        Check($"synchronous save (Alt+F4) < {SaveBudgetMs} ms", r.Ok && r.Ms < SaveBudgetMs, $"{r.Ms} ms");
        _timings.Add($"save (synchronous, CPU thumbnail): {r.Ms} ms");
        Check("synchronous save: CPU thumbnail written", File.Exists(Path.ChangeExtension(r.Path, ".png")));
    }

    void Listing()
    {
        var list = SaveStore.List();
        Check("list: newest first", list.Zip(list.Skip(1)).All(z => z.First.SortKey >= z.Second.SortKey), $"{list.Count} saves");
        var latest = SaveStore.Latest();
        Check("latest: the newest loadable save", latest != null && latest.Ok && latest.SortKey == list.Where(e => e.Ok).Max(e => e.SortKey));
        var victim = list.First(e => e.Header?.Kind == SaveKind.Manual);
        Check("delete: save and thumbnail gone", SaveStore.Delete(victim) && !File.Exists(victim.Path) && !File.Exists(victim.ThumbPath));
    }

    // ------------------------------------------------------------------ helpers

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    void Check(string what, bool ok, string detail = null)
    {
        if (ok) { _pass++; GD.Print($"  PASS {what}{(detail == null ? "" : " — " + detail)}"); }
        else Fail(what, detail);
    }

    void Fail(string what, string detail = null)
    {
        _fail++;
        GD.PrintErr($"  FAIL {what}{(detail == null ? "" : " — " + detail)}");
    }
}
