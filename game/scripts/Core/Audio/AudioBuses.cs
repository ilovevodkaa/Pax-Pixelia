using Godot;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// The bus layout of AUDIO.md §6.1, created in code (no default_bus_layout.tres):
/// <code>
/// Master     hard limiter, ceiling −1 dB
/// ├ Music     «Музыка»      [duck amp, pause amp]
/// ├ Ambience  (no slider yet: no ambience in this build) [duck amp, pause amp, low-pass]
/// └ SFX       «Эффекты», −4 dB headroom
///   ├ UI        interface
///   ├ World     map sounds (the piece, hammer, pick, scouts)
///   └ Stingers  event stingers (Stingers.cs queues them)
/// </code>
/// Settings owns the bus volumes (sliders); ducking under a stinger and the «world freezes» filter on pause never
/// touch them — they move their own AudioEffectAmplify / LowPass, so a slider and a duck can not fight.
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master", Music = "Music", Ambience = "Ambience", Sfx = "SFX",
        Ui = "UI", World = "World", Stingers = "Stingers";

    const float OpenCutoff = 20000f, PausedCutoff = 1200f;

    public static AudioEffectAmplify MusicDuck { get; private set; }
    public static AudioEffectAmplify MusicPause { get; private set; }
    public static AudioEffectAmplify AmbienceDuck { get; private set; }
    public static AudioEffectAmplify AmbiencePause { get; private set; }
    public static AudioEffectLowPassFilter AmbienceLowPass { get; private set; }

    /// <summary>Creates the missing buses and their effects (idempotent; Settings and Sfx both call it).</summary>
    public static void Ensure()
    {
        Audio.Sfx.EnsureBus(Music, 0f);
        Audio.Sfx.EnsureBus(Ambience, 0f);
        Audio.Sfx.EnsureBus(Sfx, -4f);
        Audio.Sfx.EnsureBus(Ui, 0f, Sfx);
        Audio.Sfx.EnsureBus(World, 0f, Sfx);
        Audio.Sfx.EnsureBus(Stingers, 0f, Sfx);

        int master = AudioServer.GetBusIndex(Master);
        if (Find<AudioEffectHardLimiter>(master, "px_limiter") == null)
            Add(master, new AudioEffectHardLimiter { CeilingDb = -1f, ResourceName = "px_limiter" });

        int music = AudioServer.GetBusIndex(Music);
        MusicDuck = Find<AudioEffectAmplify>(music, "px_duck") ?? Add(music, new AudioEffectAmplify { ResourceName = "px_duck" });
        MusicPause = Find<AudioEffectAmplify>(music, "px_pause") ?? Add(music, new AudioEffectAmplify { ResourceName = "px_pause" });

        int amb = AudioServer.GetBusIndex(Ambience);
        AmbienceDuck = Find<AudioEffectAmplify>(amb, "px_duck") ?? Add(amb, new AudioEffectAmplify { ResourceName = "px_duck" });
        AmbiencePause = Find<AudioEffectAmplify>(amb, "px_pause") ?? Add(amb, new AudioEffectAmplify { ResourceName = "px_pause" });
        AmbienceLowPass = Find<AudioEffectLowPassFilter>(amb, "px_lowpass")
            ?? Add(amb, new AudioEffectLowPassFilter { CutoffHz = OpenCutoff, ResourceName = "px_lowpass" });
    }

    /// <summary>Game paused: ambience goes behind a 1.2 kHz low-pass at −6 dB, music −4 dB (AUDIO.md §1 p.7, §6.5 p.5).</summary>
    public static void SetPaused(Node owner, bool paused)
    {
        if (MusicPause == null || !GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree()) return;
        var t = owner.CreateTween().SetParallel();
        t.TweenProperty(MusicPause, "volume_db", paused ? -4f : 0f, .3f);
        t.TweenProperty(AmbiencePause, "volume_db", paused ? -6f : 0f, .3f);
        t.TweenProperty(AmbienceLowPass, "cutoff_hz", paused ? PausedCutoff : OpenCutoff, .3f)
            .SetTrans(Tween.TransitionType.Expo).SetEase(paused ? Tween.EaseType.Out : Tween.EaseType.In);
    }

    static T Find<T>(int bus, string name) where T : AudioEffect
    {
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
            if (AudioServer.GetBusEffect(bus, i) is T e && e.ResourceName == name) return e;
        return null;
    }

    static T Add<T>(int bus, T effect) where T : AudioEffect
    {
        AudioServer.AddBusEffect(bus, effect);
        return effect;
    }
}
