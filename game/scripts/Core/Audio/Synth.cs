using System;
using Godot;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// Tiny offline synthesiser (port of the «Mr. President» sfx.gd helpers): renders a sample function into a 16-bit
/// mono AudioStreamWav once at start-up. Used for sounds without a file and as a fallback when a file is missing.
/// </summary>
public static class Synth
{
    public const int MixRate = 22050;
    static readonly Random Noise = new(1337);

    public static float N() => (float)(Noise.NextDouble() * 2 - 1);

    /// <summary>Samples f(t) for <paramref name="duration"/> seconds; the last 128 samples fade out (no click at the cut).</summary>
    public static AudioStreamWav Render(float duration, Func<float, float> f)
    {
        int count = (int)(duration * MixRate);
        var data = new byte[count * 2];
        int fade = Math.Min(128, count);
        for (int i = 0; i < count; i++)
        {
            float s = f((float)i / MixRate);
            if (i > count - fade) s *= (float)(count - i) / fade;
            short v = (short)(Math.Clamp(s, -1f, 1f) * 32767f);
            data[i * 2] = (byte)v; data[i * 2 + 1] = (byte)(v >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MixRate, Stereo = false, Data = data };
    }

    /// <summary>Noise through a one-pole low-pass whose cutoff slides from → to: an airy «whoosh».</summary>
    public static AudioStreamWav Whoosh(float duration, float fromCutoff, float toCutoff, float gain)
    {
        float state = 0;
        return Render(duration, t =>
        {
            float p = t / duration;
            state = Mathf.Lerp(state, N(), Mathf.Lerp(fromCutoff, toCutoff, p));
            float w = Mathf.Sin(Mathf.Pi * p);
            return state * w * w * gain * 3f;
        });
    }

    public static AudioStreamWav TwoNotes(float first, float second) => Render(.3f, t =>
    {
        float s = Mathf.Sin(Mathf.Tau * first * t) * Env(t, .003f, .05f) * .25f;
        if (t > .08f) s += Mathf.Sin(Mathf.Tau * second * (t - .08f)) * Env(t - .08f, .003f, .08f) * .25f;
        return s;
    });

    /// <summary>Linear attack, exponential decay.</summary>
    public static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);
}
