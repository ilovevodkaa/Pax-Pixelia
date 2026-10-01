using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Core;

/// <summary>How <see cref="Game.LoadGame"/> ended: loaded, refused (Error is Russian text for the player) or superseded
/// by a newer NewGame / LoadGame / EndGame while the world was being regenerated.</summary>
public readonly record struct LoadResult(bool Ok, string Error, bool Superseded)
{
    public static readonly LoadResult Done = new(true, null, false), Dropped = new(false, null, true);
    public static LoadResult Fail(string error) => new(false, error, false);
}

/// <summary>
/// Saves on the Game side (F-4). <see cref="CaptureSave"/> snapshots the running game — header, state, command journal,
/// camera / map mode / map memory — on the main thread in a few ms; <see cref="SaveAsync"/> adds the rendered
/// thumbnail and writes the file on a worker, <see cref="SaveNow"/> does it all at once (Alt+F4).
/// <see cref="LoadGame"/> reads a file, regenerates its world from the seed, refuses it when the world hash differs,
/// rebuilds the state and raises WorldReady exactly like a new game, so the chapter card and every module take the
/// same path. Files and slots: <see cref="SaveStore"/>; the calendar autosave, playtime and Alt+F4: <see cref="AutoSaver"/>.
/// </summary>
public partial class Game
{
    /// <summary>Real time spent in this game over all its sessions (pauses included), ms; AutoSaver counts it.</summary>
    public long PlaytimeMs { get; set; }

    /// <summary>The save being loaded, from LoadStarted until its WorldReady has been handled (null otherwise): the
    /// chapter card shows its nation, era and date while the world regenerates.</summary>
    public SaveHeader Loading { get; private set; }

    /// <summary>Camera, map mode, selection and map memory of the save being shown; MapCamera and MapView read it
    /// during WorldReady (null otherwise).</summary>
    public SaveView RestoreView { get; private set; }

    /// <summary>The file this game was loaded from or last saved to; null for a fresh game.</summary>
    public string SavePath { get; set; }

    /// <summary>Wall-clock ms of the last LoadGame (file + world regeneration + state) and of the last save's own work.</summary>
    public long LastLoadMs { get; private set; }
    public long LastSaveMs { get; private set; }

    /// <summary>Raised after a save was written: kind and file path (the «Сохранено» indicator, tests).</summary>
    public event Action<SaveKind, string> Saved;

    /// <summary>A load began (the file is read and valid, the world is about to regenerate): Loading is set.</summary>
    public event Action LoadStarted;

    /// <summary>The map's memory of stale provinces as a blob (set by MapView while a map is shown).</summary>
    public Func<byte[]> MapMemoryWriter { get; set; }

    /// <summary>The calendar autosave, playtime counter and the save on closing the window.</summary>
    public AutoSaver AutoSave { get; private set; }

    WorldData _hashedWorld;
    ulong _worldHash;

    /// <summary>Called from _Ready: the autosaver lives next to the Game (it outlives every scene).</summary>
    void AttachSaves()
    {
        AutoSave = new AutoSaver { Name = "AutoSaver" };
        AddChild(AutoSave);
    }

    /// <summary>A new game starts from nothing: no playtime, no file.</summary>
    void ResetSaveInfo()
    {
        PlaytimeMs = 0;
        SavePath = null;
        Loading = null;
        RestoreView = null;
    }

    /// <summary>World hash of the running world (computed once per world, ≈10 ms).</summary>
    ulong CurrentWorldHash()
    {
        if (!ReferenceEquals(_hashedWorld, World)) { _worldHash = WorldHash.Of(World); _hashedWorld = World; }
        return _worldHash;
    }

    /// <summary>«Ардания · март 3200 до н. э.»: the default name of a save of the running game.</summary>
    public string DefaultSaveName() => IsReady ? $"{Nations[Viewer].Name} · {DateText}" : "";

    /// <summary>Snapshot of the running game for a save file (main thread, a few ms). Null before the world is ready.</summary>
    public (SaveHeader Header, SaveBody Body)? CaptureSave(SaveKind kind, string name = null)
    {
        if (!IsReady) return null;
        var s = State; var w = World;
        var me = Nations[Viewer];
        var h = new SaveHeader
        {
            GameVersion = ProjectSettings.GetSetting("application/config/version", "0.1").AsString(),
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(name) ? DefaultSaveName() : name.Trim(),
            SavedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PlaytimeMs = PlaytimeMs,
            Seed = w.Seed,
            SeedText = Setup?.SeedText ?? w.Seed.ToString(),
            NationCount = s.Nat.Length,
            Fog = Setup?.Fog ?? s.FogEnabled,
            PacePermille = s.Pace,
            JokePercent = Setup?.JokePercent ?? 100,
            Player = Setup?.Player,
            DateText = DateText,
            Year = s.Year,
            Era = EraIndex,
            NationName = me.Name,
            R = me.R, G = me.G, B = me.B,
            Flag = me.Flag,
            Tick = s.Tick,
            WorldW = w.W, WorldH = w.H, Provinces = w.P,
            WorldHash = CurrentWorldHash(),
            StateHash = s.Hash().All,
            ContentHash = SaveFile.ContentSignature(s.Events?.Db),
        };
        var body = new SaveBody { State = SaveFile.Snapshot(s), Journal = new List<Cmd>(Journal), View = CaptureView() };
        _commands.Sequences.CopyTo(body.Seq);
        return (h, body);
    }

    SaveView CaptureView()
    {
        var v = new SaveView { Zoom = ZoomLevel, Mode = (byte)Mode, Selected = Selected };
        var r = CameraRect;
        if (r.Size.X > 0 && r.Size.Y > 0 && World != null)
        {
            var c = r.Position + r.Size / 2;
            v.CamX = Mathf.PosMod(c.X, World.W); v.CamY = c.Y; v.HasCamera = true;
        }
        try { v.MapMemory = MapMemoryWriter?.Invoke(); }
        catch (Exception e) { GD.PushWarning($"save: map memory skipped: {e.Message}"); }
        return v;
    }

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// Save the running game at once (main thread, CPU thumbnail): the window is closing. Path: the kind's slot unless
    /// given. Waits for a write still running on the worker.
    /// </summary>
    public SaveResult SaveNow(SaveKind kind, string name = null, string path = null)
    {
        var sw = Stopwatch.StartNew();
        var data = CaptureSave(kind, name);
        if (data == null) return SaveResult.Fail("Партия ещё не началась");
        var png = SaveThumb.CaptureNow().SavePngToBuffer();
        path ??= SaveStore.PathFor(kind);
        try
        {
            int bytes = SaveStore.WriteFile(path, data.Value.Header, data.Value.Body, png);
            return Finish(kind, path, data.Value.Header, bytes, sw.ElapsedMilliseconds, sw.ElapsedMilliseconds, _generation);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            GD.PushError($"save: writing {path} failed: {e.Message}");
            return SaveResult.Fail(SaveException.Text(SaveError.Io) + ": " + e.Message, path, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Save the running game: the snapshot is taken now, the thumbnail renders with the next frame, the file is written
    /// on a worker. <paramref name="keep"/>: an autosave slot not to overwrite (the save about to be loaded).
    /// </summary>
    public async Task<SaveResult> SaveAsync(SaveKind kind, string name = null, string path = null, string keep = null)
    {
        var sw = Stopwatch.StartNew();
        var data = CaptureSave(kind, name);
        if (data == null) return SaveResult.Fail("Партия ещё не началась");
        long captureMs = sw.ElapsedMilliseconds;
        int gen = _generation;
        var img = await SaveThumb.CaptureAsync(this);
        long waited = sw.ElapsedMilliseconds;
        var t = Stopwatch.StartNew();
        var png = img?.SavePngToBuffer();
        path ??= SaveStore.PathFor(kind, keep);
        var (h, body) = data.Value;
        try
        {
            int bytes = await Task.Run(() => SaveStore.WriteFile(path, h, body, png));
            // own work: the snapshot, the thumbnail's read-back and PNG, the file (the frame waited for is not work)
            return Finish(kind, path, h, bytes, captureMs + t.ElapsedMilliseconds, sw.ElapsedMilliseconds, gen, waited - captureMs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            GD.PushError($"save: writing {path} failed: {e.Message}");
            return SaveResult.Fail(SaveException.Text(SaveError.Io) + ": " + e.Message, path, sw.ElapsedMilliseconds);
        }
    }

    SaveResult Finish(SaveKind kind, string path, SaveHeader h, int bytes, long workMs, long wallMs, int gen, long frameMs = 0)
    {
        if (gen == _generation) SavePath = path;   // the same game is still running
        LastSaveMs = workMs;
        GD.Print($"save: {kind} → {Path.GetFileName(path)} «{h.Name}» tick={h.Tick} {bytes / 1024} KB in {workMs} ms" +
                 (frameMs > 0 ? $" (+{frameMs} ms waiting for the thumbnail frame{(SaveThumb.LastWasCpu ? ", CPU picture" : "")})" : ""));
        Saved?.Invoke(kind, path);
        return new SaveResult(true, path, null, workMs, bytes);
    }

    // ------------------------------------------------------------------ loading

    /// <summary>
    /// Load a save: read the file (a bad one is refused before anything changes), drop the running game, regenerate its
    /// world (a front-end world of the same seed is reused), refuse it when the world hash differs, rebuild the state,
    /// the journal and the view, then raise WorldReady as a new game does. The loaded game starts paused.
    /// </summary>
    public async Task<LoadResult> LoadGame(string path, WorldData pregenerated = null)
    {
        var sw = Stopwatch.StartNew();
        SaveHeader h; SaveBody body;
        try { (h, body) = SaveStore.ReadFile(path); }
        catch (SaveException e) { GD.PushWarning($"save: {path}: {e.Message}"); return LoadResult.Fail(e.Message); }
        long readMs = sw.ElapsedMilliseconds;

        int gen = ++_generation;
        _genCancel?.Cancel();
        var cancel = _genCancel = new CancellationTokenSource();
        Setup = h.ToSetup();
        World = null; State = null; Seed = h.Seed;
        Hovered = -1; Selected = -1;
        ResetClock();
        PlaytimeMs = h.PlaytimeMs;
        SavePath = path;
        Loading = h; RestoreView = null;
        LoadStarted?.Invoke();
        var progress = new Progress<string>(s => { if (gen == _generation && World == null) GenerationProgress?.Invoke(s); });
        WorldData world; GameState state; long genMs;
        try
        {
            (world, state, genMs) = await Task.Run(() =>
            {
                var t = Stopwatch.StartNew();
                bool reuse = pregenerated != null && pregenerated.Seed == h.Seed && pregenerated.W == h.WorldW && pregenerated.H == h.WorldH;
                var w = reuse ? pregenerated : WorldGen.Generate(h.Seed, h.WorldW, h.WorldH, s => ((IProgress<string>)progress).Report(s), cancel.Token);
                long g = t.ElapsedMilliseconds;
                cancel.Token.ThrowIfCancellationRequested();
                ((IProgress<string>)progress).Report("Державы и границы…");
                if (w.P != h.Provinces || WorldHash.Of(w) != h.WorldHash) throw new SaveException(SaveError.WorldMismatch);
                var st = SaveFile.Restore(body.State, w, Content, h.JokePercent);
                // same layout and content pack → the state must come out exactly as saved (SaveFile.Consistent)
                if (!SaveFile.Consistent(h, body.State, st)) throw new SaveException(SaveError.Corrupt, "состояние не сошлось");
                return (w, st, g);
            }, cancel.Token);
        }
        catch (OperationCanceledException) when (gen != _generation) { DropLoading(h); return LoadResult.Dropped; }
        catch (SaveException e)
        {
            DropLoading(h);
            SaveStore.MarkBroken(path, e.Message);
            GD.PushWarning($"save: {path}: {e.Message}");
            return gen == _generation ? LoadResult.Fail(e.Message) : LoadResult.Dropped;
        }
        catch (Exception e)
        {
            DropLoading(h);
            GD.PushError($"save: loading {path} failed: {e}");
            return gen == _generation ? LoadResult.Fail("Не удалось загрузить сохранение: " + e.Message) : LoadResult.Dropped;
        }
        if (gen != _generation) { DropLoading(h); return LoadResult.Dropped; }

        state.Paused = true;   // a loaded game waits for the player
        World = world; State = state;
        Seed = world.Seed;
        ResetClock();
        _commands.Restore(body.Journal, body.Seq);
        _hashedWorld = world; _worldHash = h.WorldHash;
        if (Enum.IsDefined(typeof(MapMode), (int)body.View.Mode)) Mode = (MapMode)body.View.Mode;
        RestoreView = body.View;
        LastGenerationMs = genMs;
        LastLoadMs = sw.ElapsedMilliseconds;
        GD.Print($"save: loaded {Path.GetFileName(path)} tick={state.Tick} «{DateText}» in {LastLoadMs} ms (file {readMs} ms, world {genMs} ms), hash {state.Hash()}");
        try
        {
            WorldReady?.Invoke();
            RaiseDateChanged();
            TimeControlChanged?.Invoke(State.Paused, State.Speed);
        }
        finally
        {
            Loading = null;
            RestoreView = null;
        }
        // after the HUD's own «select the capital» (deferred during WorldReady): the province the player had open
        int selected = body.View.Selected;
        if (selected >= 0 && !Cli.Has("select")) Callable.From(() => { if (gen == _generation && IsReady) Select(selected); }).CallDeferred();
        else if (selected < 0) Callable.From(() => { if (gen == _generation && IsReady && Selected >= 0) Select(-1); }).CallDeferred();
        return LoadResult.Done;
    }

    void DropLoading(SaveHeader h)
    {
        if (ReferenceEquals(Loading, h)) Loading = null;
    }
}
