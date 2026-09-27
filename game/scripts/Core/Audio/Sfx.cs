using System.Collections.Generic;
using Godot;
using PaxPixelia.UI;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// UI sounds (autoload "Sfx", port of «Mr. President» core/sfx.gd): 12 voices on the SFX bus, random file variants
/// (name_N.ogg), a minimal repeat interval for rapid sounds, synthesised stand-ins for missing files. Files are the
/// Kenney «Interface Sounds» (CC0) in res://assets/audio/sfx. Usage: Sfx.I?.Play("click"), or through PixelKit.Sfx.
/// </summary>
public partial class Sfx : Node
{
    public static Sfx I { get; private set; }

    const int Voices = 12;
    public const string Bus = "SFX", MusicBus = "Music";
    const string SampleDir = "res://assets/audio/sfx/";
    static readonly Dictionary<string, int> MinIntervalMs = new() { ["tick"] = 28, ["hover"] = 45 };
    /// <summary>Sound → number of file variants (1 = name.ogg, N = name_1.ogg … name_N.ogg).</summary>
    static readonly Dictionary<string, int> Samples = new()
    {
        ["hover"] = 2, ["click"] = 1, ["open"] = 1, ["close"] = 1, ["error"] = 1, ["confirm"] = 1, ["tick"] = 3,
    };

    readonly Dictionary<string, AudioStream> _synth = new();
    readonly Dictionary<string, List<AudioStream>> _files = new();
    readonly Dictionary<string, ulong> _last = new();
    readonly List<AudioStreamPlayer> _players = new();
    readonly RandomNumberGenerator _rng = new();

    /// <summary>Plays through the autoload if it exists (scenes launched without autoloads stay silent).</summary>
    public static void Try(string sound, float pitch = 1f) => I?.Play(sound, pitch);

    public override void _EnterTree() => I = this;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        EnsureBus(Bus, -4f);
        EnsureBus(MusicBus, 0f);
        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer { Bus = Bus };
            AddChild(p);
            _players.Add(p);
        }
        BuildSynth();
        LoadFiles();
        PixelKit.Sfx = (name, pitch) => Play(name, pitch);
    }

    public override void _ExitTree()
    {
        if (I != this) return;
        I = null;
        PixelKit.Sfx = null;
    }

    public void Play(string sound, float pitch = 1f, float volumeDb = 0f)
    {
        AudioStream stream = _files.TryGetValue(sound, out var variants) ? variants[_rng.RandiRange(0, variants.Count - 1)]
            : _synth.GetValueOrDefault(sound);
        if (stream == null) return;
        ulong now = Time.GetTicksMsec();
        if (MinIntervalMs.TryGetValue(sound, out int gap) && _last.TryGetValue(sound, out ulong last) && now - last < (ulong)gap) return;
        _last[sound] = now;
        var player = FreePlayer();
        player.Stream = stream;
        player.PitchScale = pitch;
        player.VolumeDb = volumeDb;
        player.Play();
    }

    AudioStreamPlayer FreePlayer()
    {
        var oldest = _players[0];
        foreach (var p in _players)
        {
            if (!p.Playing) return p;
            if (p.GetPlaybackPosition() > oldest.GetPlaybackPosition()) oldest = p;
        }
        return oldest;
    }

    /// <summary>Master → Music, SFX: created in code (as _ensure_bus in Mr. President) so the project needs no bus layout.</summary>
    public static void EnsureBus(string name, float volumeDb)
    {
        if (AudioServer.GetBusIndex(name) != -1) return;
        AudioServer.AddBus();
        int i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, "Master");
        AudioServer.SetBusVolumeDb(i, volumeDb);
    }

    void LoadFiles()
    {
        foreach (var (sound, count) in Samples)
        {
            var list = new List<AudioStream>();
            for (int v = 1; v <= count; v++)
                if (LoadOgg(SampleDir + (count == 1 ? $"{sound}.ogg" : $"{sound}_{v}.ogg")) is { } s) list.Add(s);
            if (list.Count > 0) _files[sound] = list;
            else GD.PushWarning($"Sfx: no file for «{sound}» in {SampleDir}, using the synthesised one");
        }
    }

    /// <summary>The imported resource first; a raw .ogg from disk if the editor has not imported it yet.</summary>
    static AudioStream LoadOgg(string path)
    {
        if (ResourceLoader.Exists(path) && GD.Load<AudioStream>(path) is { } imported) return imported;
        return FileAccess.FileExists(path) ? AudioStreamOggVorbis.LoadFromFile(path) : null;
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
