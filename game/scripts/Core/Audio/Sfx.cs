using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using PaxPixelia.UI;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// Sound effects (autoload "Sfx", port of «Mr. President» core/sfx.gd, grown to AUDIO.md §6.4): the table of
/// <see cref="SoundBank"/> (Kenney CC0 files under res://assets/audio), one voice pool per bus (UI, World, Stingers),
/// file variants without an immediate repeat, a small random pitch / volume per sound, a shortest interval and a voice
/// limit per sound, and synthesised stand-ins for the front-end extras (whoosh, stamp, pop…).
/// Sounds cued within one frame are merged: only the highest-ranked one plays (<see cref="SoundDef.Rank"/>), so a
/// button that opens a book sounds as the book. Usage stays Sfx.I?.Play("click") / Sfx.Try / PixelKit.Sfx.
/// Children: <see cref="Stingers"/> (event stingers: queue + ducking) and <see cref="GameSounds"/> (Game.I events → sounds).
/// --sfxlog prints every played and every dropped sound (time, cause, key, file, bus, volume, pitch).
/// </summary>
public partial class Sfx : Node
{
    public static Sfx I { get; private set; }

    public const string Bus = AudioBuses.Sfx, MusicBus = AudioBuses.Music;

    /// <summary>A sound that really started (tests, the probe).</summary>
    public readonly record struct Played(double Time, string Cause, string Key, string File, string Bus, float VolumeDb, float Pitch);

    /// <summary>Raised for every sound that starts.</summary>
    public event Action<Played> PlayedSound;
    /// <summary>Raised for a sound that did not play: (cause, key, reason = merged:…, interval, missing).</summary>
    public event Action<string, string, string> DroppedSound;

    static readonly (string bus, int voices)[] Pools = { (AudioBuses.Ui, 10), (AudioBuses.World, 6), (AudioBuses.Stingers, 3) };

    sealed class Voice
    {
        public AudioStreamPlayer Player;
        public string Key;
        public ulong Started;
    }

    readonly record struct Cue(string Key, float Pitch, float Db, string Cause);

    readonly Dictionary<string, AudioStream> _synth = new();
    readonly Dictionary<string, List<(AudioStream stream, string file)>> _files = new();
    readonly Dictionary<string, ulong> _last = new();
    readonly Dictionary<string, int> _lastVariant = new();
    readonly Dictionary<string, List<Voice>> _voices = new();
    readonly List<Cue> _pending = new(), _order = new();
    readonly RandomNumberGenerator _rng = new();
    ulong _lastMotion;   // ms of the last mouse motion: hover only follows a moving hand

    /// <summary>A hover needs the mouse to have moved within this many ms (a panel rebuilt under a resting cursor is silent).</summary>
    const ulong HoverMotionMs = 250;

    /// <summary>--sfxlog: every played / dropped sound goes to the console.</summary>
    public static bool LogOn { get; } = Cli.Has("sfxlog");

    /// <summary>Plays through the autoload if it exists (scenes launched without autoloads stay silent).</summary>
    public static void Try(string sound, float pitch = 1f) => I?.Play(sound, pitch);

    public override void _EnterTree() => I = this;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        AudioBuses.Ensure();
        foreach (var (bus, count) in Pools)
        {
            var list = new List<Voice>();
            for (int i = 0; i < count; i++)
            {
                var p = new AudioStreamPlayer { Bus = bus };
                AddChild(p);
                list.Add(new Voice { Player = p });
            }
            _voices[bus] = list;
        }
        BuildSynth();
        LoadFiles();
        PixelKit.Sfx = (name, pitch) => Play(name, pitch);
        AddChild(new Stingers { Name = "Stingers" });
        AddChild(new GameSounds { Name = "GameSounds" });
        if (LogOn) GD.Print($"[sfx] log on: {_files.Count} sounds with files, {_synth.Count} synthesised; buses {BusList()}");
    }

    public override void _ExitTree()
    {
        foreach (var pool in _voices.Values)
            foreach (var v in pool) { v.Player.Stop(); v.Player.Stream = null; }   // no playback outlives the quit
        if (I != this) return;
        I = null;
        PixelKit.Sfx = null;
    }

    /// <summary>
    /// Cue a sound. It starts on this autoload's next _Process unless a higher-ranked sound is cued before that:
    /// input and the deferred calls it causes (Ui.Deferred button actions, the pause-menu check) all run before the
    /// frame's _process, so a click and what the click does meet in one merge. At most one frame of latency.
    /// <paramref name="cause"/> names the game event for --sfxlog («map.select», «event:drought»…).
    /// </summary>
    public void Play(string sound, float pitch = 1f, float volumeDb = 0f, string cause = null) =>
        _pending.Add(new Cue(sound, pitch, volumeDb, cause ?? "ui"));

    public override void _Process(double delta)
    {
        if (_pending.Count > 0) Flush();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseMotion) _lastMotion = Time.GetTicksMsec();
    }

    /// <summary>Start a sound now, outside the per-frame merge (the stinger queue). Returns its length in seconds (0 = not played).</summary>
    public float PlayNow(string sound, string cause, float pitch = 1f, float volumeDb = 0f) => Math.Max(0f, Start(new Cue(sound, pitch, volumeDb, cause)));

    /// <summary>Whether a sound has files or a synthesised stand-in.</summary>
    public bool Has(string sound) => _files.ContainsKey(sound) || _synth.ContainsKey(sound);

    /// <summary>The frame's cues, highest rank first (ties: the first cued): the first that may play plays, the rest merge into it.</summary>
    void Flush()
    {
        _order.Clear();
        _order.AddRange(_pending);
        _pending.Clear();
        for (int i = 1; i < _order.Count; i++)   // stable insertion sort by rank, descending (a handful of cues)
            for (int j = i; j > 0 && RankOf(_order[j].Key) > RankOf(_order[j - 1].Key); j--)
                (_order[j], _order[j - 1]) = (_order[j - 1], _order[j]);
        string played = null;
        foreach (var c in _order)
        {
            if (played != null) Drop(c.Cause, c.Key, "merged:" + played);
            else if (Start(c) >= 0) played = c.Key;   // one held back by its interval lets the next in rank speak
        }
    }

    static int RankOf(string key) => SoundBank.Defs.TryGetValue(key, out var d) ? d.Rank : SoundBank.SynthRank;

    /// <summary>Plays a cue; its length in seconds, or −1 when it may not play (no sound, too soon).</summary>
    float Start(Cue c)
    {
        var def = SoundBank.Defs.GetValueOrDefault(c.Key);
        AudioStream stream;
        string file, bus = def?.Bus ?? AudioBuses.Ui;
        if (_files.TryGetValue(c.Key, out var variants))
        {
            var v = variants[PickVariant(c.Key, variants.Count)];
            stream = v.stream; file = v.file;
        }
        else if (_synth.TryGetValue(c.Key, out var s)) { stream = s; file = "synth"; }
        else { Drop(c.Cause, c.Key, "missing"); return -1; }

        ulong now = Time.GetTicksMsec();
        if (c.Key == "hover" && now - _lastMotion > HoverMotionMs) { Drop(c.Cause, c.Key, "still"); return -1; }
        int minMs = def?.MinMs ?? (c.Key == "tick" ? 28 : 0);
        if (minMs > 0 && _last.TryGetValue(c.Key, out ulong last) && now - last < (ulong)minMs) { Drop(c.Cause, c.Key, "interval"); return -1; }
        _last[c.Key] = now;

        float jitter = def?.Jitter ?? 0f, dbJitter = def?.DbJitter ?? 0f;
        float pitch = Mathf.Clamp(c.Pitch * (1f + (jitter > 0 ? _rng.RandfRange(-jitter, jitter) : 0f)), .5f, 2f);
        float db = (def?.Db ?? 0f) + c.Db + (dbJitter > 0 ? _rng.RandfRange(-dbJitter, dbJitter) : 0f);
        var voice = TakeVoice(bus, c.Key, def?.Poly ?? 2);
        voice.Key = c.Key;
        voice.Started = now;
        voice.Player.Stream = stream;
        voice.Player.PitchScale = pitch;
        voice.Player.VolumeDb = db;
        voice.Player.Play();
        var played = new Played(now / 1000.0, c.Cause, c.Key, file, bus, db, pitch);
        if (LogOn) GD.Print(Line("play", c.Cause, c.Key, FormattableString.Invariant($"file={file} bus={bus} vol={db:+0.0;-0.0;0.0}dB pitch={pitch:0.000}")));
        PlayedSound?.Invoke(played);
        return (float)(stream.GetLength() / pitch);
    }

    /// <summary>Report a sound that did not play (--sfxlog, <see cref="DroppedSound"/>).</summary>
    public void Drop(string cause, string key, string reason)
    {
        if (LogOn) GD.Print(Line("drop", cause, key, reason));
        DroppedSound?.Invoke(cause, key, reason);
    }

    /// <summary>One --sfxlog line, invariant culture: «[sfx] 12.345 play  map.select  piece  file=… bus=World vol=+0.8dB pitch=1.012».</summary>
    public static string Line(string what, string cause, string key, string detail) =>
        string.Create(CultureInfo.InvariantCulture, $"[sfx] {Time.GetTicksMsec() / 1000.0:0.000} {what,-5} {cause,-24} {key,-17} {detail}");

    /// <summary>A variant other than the previous one (2+ files).</summary>
    int PickVariant(string key, int count)
    {
        if (count <= 1) return 0;
        int last = _lastVariant.GetValueOrDefault(key, -1);
        int v = last < 0 ? _rng.RandiRange(0, count - 1) : _rng.RandiRange(0, count - 2);
        if (last >= 0 && v >= last) v++;
        _lastVariant[key] = v;
        return v;
    }

    /// <summary>A free voice of the bus; at the sound's voice limit its own oldest voice restarts; else the bus's oldest.</summary>
    Voice TakeVoice(string bus, string key, int poly)
    {
        var pool = _voices.GetValueOrDefault(bus) ?? _voices[AudioBuses.Ui];
        Voice free = null, oldest = null, oldestSame = null;
        int same = 0;
        foreach (var v in pool)
        {
            if (!v.Player.Playing) { free ??= v; continue; }
            if (v.Key == key)
            {
                same++;
                if (oldestSame == null || v.Started < oldestSame.Started) oldestSame = v;
            }
            if (oldest == null || v.Started < oldest.Started) oldest = v;
        }
        if (same >= Math.Max(1, poly) && oldestSame != null) return oldestSame;
        return free ?? oldest;
    }

    /// <summary>Creates a bus (as _ensure_bus in Mr. President) sending to <paramref name="send"/>; the layout is <see cref="AudioBuses"/>.</summary>
    public static void EnsureBus(string name, float volumeDb, string send = AudioBuses.Master)
    {
        if (AudioServer.GetBusIndex(name) != -1) return;
        AudioServer.AddBus();
        int i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, send);
        AudioServer.SetBusVolumeDb(i, volumeDb);
    }

    static string BusList()
    {
        var parts = new List<string>();
        for (int i = 0; i < AudioServer.BusCount; i++)
        {
            string name = AudioServer.GetBusName(i), send = AudioServer.GetBusSend(i);
            parts.Add(i == 0 ? name : $"{name}→{send}");
        }
        return string.Join(", ", parts);
    }

    void LoadFiles()
    {
        var missing = new List<string>();
        foreach (var (key, def) in SoundBank.Defs)
        {
            var list = new List<(AudioStream, string)>();
            foreach (var f in def.Files)
                if (LoadOgg(SoundBank.Root + f) is { } s) list.Add((s, f)); else missing.Add(f);
            if (list.Count > 0) _files[key] = list;
        }
        if (missing.Count > 0) GD.PushWarning($"Sfx: {missing.Count} sound files missing in {SoundBank.Root} ({string.Join(", ", missing)}); synthesised stand-ins or silence");
    }

    /// <summary>The imported resource first (.ogg or .wav); a raw .ogg from disk if the editor has not imported it yet.</summary>
    static AudioStream LoadOgg(string path)
    {
        if (ResourceLoader.Exists(path) && GD.Load<AudioStream>(path) is { } imported) return imported;
        return path.EndsWith(".ogg") && FileAccess.FileExists(path) ? AudioStreamOggVorbis.LoadFromFile(path) : null;
    }

    void BuildSynth()
    {
        _synth["hover"] = Synth.Render(.05f, t => Mathf.Sin(Mathf.Tau * 1800 * t) * Synth.Env(t, .002f, .012f) * .10f);
        _synth["click"] = Synth.Render(.09f, t =>
        {
            float ph = Mathf.Tau * (900 * t - 1250 * t * t);
            return (Mathf.Sin(ph) + .4f * Mathf.Sin(ph * 2)) * Synth.Env(t, .001f, .022f) * .32f;
        });
        _synth["tick"] = Synth.Render(.035f, t =>
            Synth.N() * Synth.Env(t, .0005f, .004f) * .35f + Mathf.Sin(Mathf.Tau * 2300 * t) * Synth.Env(t, .0005f, .007f) * .3f);
        _synth["open"] = Synth.Whoosh(.3f, .05f, .35f, .16f);
        _synth["close"] = Synth.Whoosh(.22f, .3f, .05f, .12f);
        _synth["whoosh"] = Synth.Whoosh(.32f, .04f, .3f, .14f);   // the dither wipe between scenes
        _synth["pop"] = Synth.Render(.13f, t => Mathf.Sin(Mathf.Tau * (420 * t + 2000 * t * t)) * Synth.Env(t, .003f, .04f) * .32f);
        _synth["remove"] = Synth.Render(.13f, t => Mathf.Sin(Mathf.Tau * (760 * t - 1600 * t * t)) * Synth.Env(t, .003f, .04f) * .3f);
        _synth["ready"] = Synth.TwoNotes(784f, 1046.5f);
        _synth["confirm"] = _synth["ready"];
        _synth["error"] = Synth.Render(.26f, t =>
        {
            float gate = t < .1f || t > .13f ? 1 : 0, local = t < .13f ? t : t - .13f;
            return Mathf.Sign(Mathf.Sin(Mathf.Tau * 170 * t)) * Synth.Env(local, .002f, .05f) * gate * .12f;
        });
        // the title «stamp»: a low thud with a paper slap
        _synth["stamp"] = Synth.Render(.25f, t =>
            Mathf.Sin(Mathf.Tau * (140 * t - 120 * t * t)) * Synth.Env(t, .001f, .07f) * .5f + Synth.N() * Synth.Env(t, .001f, .02f) * .25f);
    }
}
