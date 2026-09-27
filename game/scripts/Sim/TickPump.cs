namespace PaxPixelia.Sim;

/// <summary>
/// Real time → ticks through an integer accumulator (tick-microseconds). The number of ticks after any amount of real
/// time is floor(µs × ticksPerSecond / 10⁶) however the time was split into frames, so two PCs at different frame rates
/// run the same ticks. A long hitch is capped (<see cref="MaxFrameMicros"/>) instead of firing a burst of ticks.
/// </summary>
public sealed class TickPump
{
    public const long MicrosPerSecond = 1_000_000;
    /// <summary>Longest frame the clock catches up on (0.25 s): 10 ticks at speed 5.</summary>
    public const long MaxFrameMicros = 250_000;

    long _acc;   // tick-microseconds not yet turned into a tick

    /// <summary>Feed one frame; returns how many ticks are due now.</summary>
    public int Advance(long frameMicros, int ticksPerSecond)
    {
        if (frameMicros <= 0 || ticksPerSecond <= 0) return 0;
        if (frameMicros > MaxFrameMicros) frameMicros = MaxFrameMicros;
        _acc += frameMicros * ticksPerSecond;
        int n = (int)(_acc / MicrosPerSecond);
        _acc -= n * MicrosPerSecond;
        return n;
    }

    /// <summary>Progress towards the next tick in 1/65536 (renderers interpolate moving things with it).</summary>
    public int Fraction16 => (int)(_acc * 65536 / MicrosPerSecond);

    public void Reset() => _acc = 0;
}
