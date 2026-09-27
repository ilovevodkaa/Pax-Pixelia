using Godot;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// Background music (autoload "Music", port of «Mr. President» core/music.gd): one looping track on the Music bus,
/// 0.6 s fade-in and 1.2 s fade-out. This wave ships no track (MAIN_MENU.md: silence + Kenney UI sounds), so nothing
/// calls Play yet; the bus exists so the volume slider already works.
/// </summary>
public partial class Music : Node
{
    public static Music I { get; private set; }

    const float Silent = -40f;
    readonly AudioStreamPlayer _player = new() { Bus = Sfx.MusicBus, VolumeDb = Silent };
    Tween _fade;

    public override void _EnterTree() => I = this;
    public override void _ExitTree() { if (I == this) I = null; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Sfx.EnsureBus(Sfx.MusicBus, 0f);
        AddChild(_player);
    }

    public void Play(AudioStream track)
    {
        if (track == null || (_player.Stream == track && _player.Playing)) return;
        if (track is AudioStreamOggVorbis ogg) ogg.Loop = true;
        _player.Stream = track;
        _player.VolumeDb = Silent;
        _player.Play();
        FadeTo(0f, .6f);
    }

    public void Stop()
    {
        if (_player.Playing) FadeTo(Silent, 1.2f).TweenCallback(Callable.From(_player.Stop));
    }

    Tween FadeTo(float db, float seconds)
    {
        _fade?.Kill();
        _fade = CreateTween();
        _fade.TweenProperty(_player, "volume_db", db, seconds).SetTrans(Tween.TransitionType.Sine);
        return _fade;
    }
}
