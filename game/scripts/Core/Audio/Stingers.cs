using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// Event stingers (AUDIO.md §1 p.6, §6.1): a queue with priorities — бедствие → эпоха → чудо → религия → открытие →
/// остальное — at most one stinger every <see cref="Gap"/> s; a key already waiting is merged, a stinger that waited
/// longer than <see cref="MaxWait"/> s is dropped (late fanfares read as glitches). While one sounds, Music and
/// Ambience duck by −6 / −5 dB in 0.1 s and come back over 1.5 s (their own amplifiers, not the sliders).
/// An era fanfare takes a 0.4 s breath of near-silence first. Child of the Sfx autoload (process mode Always).
/// </summary>
public partial class Stingers : Node
{
    public static Stingers I { get; private set; }

    /// <summary>Priorities of the peaceful MVP (war goes above disaster in stage two).</summary>
    public const int Other = 0, Discovery = 1, Religion = 2, Wonder = 3, Era = 4, Disaster = 5;

    public const double Gap = 1.6, MaxWait = 6, PreSilence = .4;
    const double Tail = .25;
    const int MaxQueue = 4;
    const float MusicDuckDb = -6f, AmbienceDuckDb = -5f, SilenceDb = -30f, Attack = .1f, Release = 1.5f;

    sealed class Entry
    {
        public string Key, Cause;
        public int Priority;
        public double Queued;
        public bool Silence;
    }

    readonly List<Entry> _queue = new();
    Entry _armed;          // an era fanfare waiting out its breath of silence
    double _armedAt, _nextFree;
    Tween _duck;

    static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Stingers waiting (the armed one included).</summary>
    public int Waiting => _queue.Count + (_armed != null ? 1 : 0);

    /// <summary>Nothing waiting and the last stinger's gap is over.</summary>
    public bool Idle => Waiting == 0 && Now >= _nextFree;

    public override void _EnterTree() => I = this;
    public override void _ExitTree() { if (I == this) I = null; }
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    /// <summary>Queue a stinger. <paramref name="preSilence"/>: 0.4 s of near-silence before it (the era fanfare).</summary>
    public void Enqueue(string key, int priority, string cause, bool preSilence = false)
    {
        if (Sfx.I == null || !Sfx.I.Has(key)) { Log("drop", cause, key, "missing"); return; }
        if (_armed?.Key == key || _queue.Exists(e => e.Key == key)) { Log("drop", cause, key, "merged:waiting"); return; }
        _queue.Add(new Entry { Key = key, Cause = cause, Priority = priority, Queued = Now, Silence = preSilence });
        if (_queue.Count > MaxQueue)
        {
            int worst = 0;   // the lowest priority, the oldest of those
            for (int i = 1; i < _queue.Count; i++) if (_queue[i].Priority < _queue[worst].Priority) worst = i;
            Log("drop", _queue[worst].Cause, _queue[worst].Key, "queue-full");
            _queue.RemoveAt(worst);
        }
        Log("queue", cause, key, $"prio={priority} waiting={Waiting}");
    }

    /// <summary>Forget everything waiting and lift the duck (leaving the game).</summary>
    public void Clear()
    {
        _queue.Clear();
        _armed = null;
        _nextFree = 0;
        Duck(0, 0, Attack, 0);
    }

    public override void _Process(double delta)
    {
        double now = Now;
        if (_armed != null)
        {
            if (now < _armedAt) return;
            var e = _armed;
            _armed = null;
            Start(e, now);
            return;
        }
        if (_queue.Count == 0 || now < _nextFree) return;
        for (int i = _queue.Count - 1; i >= 0; i--)
            if (now - _queue[i].Queued > MaxWait) { Log("drop", _queue[i].Cause, _queue[i].Key, "stale"); _queue.RemoveAt(i); }
        if (_queue.Count == 0) return;
        int best = 0;
        for (int i = 1; i < _queue.Count; i++) if (_queue[i].Priority > _queue[best].Priority) best = i;
        var next = _queue[best];
        _queue.RemoveAt(best);
        if (next.Silence)
        {
            _armed = next;
            _armedAt = now + PreSilence;
            Duck(SilenceDb, SilenceDb, Attack, -1);   // hold until the fanfare starts
            return;
        }
        Start(next, now);
    }

    void Start(Entry e, double now)
    {
        float length = Sfx.I?.PlayNow(e.Key, e.Cause) ?? 0f;
        if (length <= 0) { _nextFree = now; Duck(0, 0, Attack, 0); return; }
        _nextFree = now + Math.Max(Gap, length + Tail);
        Duck(MusicDuckDb, AmbienceDuckDb, Attack, length);
    }

    /// <summary>Tween the duck amplifiers to (music, ambience) dB; after <paramref name="hold"/> s back to 0 (hold &lt; 0: stay).</summary>
    void Duck(float music, float ambience, float attack, float hold)
    {
        if (AudioBuses.MusicDuck == null || !IsInsideTree()) return;
        _duck?.Kill();
        _duck = CreateTween().SetParallel();
        _duck.TweenProperty(AudioBuses.MusicDuck, "volume_db", music, attack);
        _duck.TweenProperty(AudioBuses.AmbienceDuck, "volume_db", ambience, attack);
        if (hold < 0 || (music == 0 && ambience == 0)) return;
        _duck.Chain().TweenInterval(hold);
        _duck.Chain().TweenProperty(AudioBuses.MusicDuck, "volume_db", 0f, Release).SetTrans(Tween.TransitionType.Sine);
        _duck.TweenProperty(AudioBuses.AmbienceDuck, "volume_db", 0f, Release).SetTrans(Tween.TransitionType.Sine);
    }

    static void Log(string what, string cause, string key, string detail)
    {
        if (what == "drop" && Sfx.I != null) Sfx.I.Drop(cause, key, detail);   // logs it too
        else if (Sfx.LogOn) GD.Print(Sfx.Line(what, cause, key, detail));
    }
}
