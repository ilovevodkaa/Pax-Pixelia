namespace PaxPixelia.Sim;

/// <summary>
/// The one game clock. Every rule counts ticks of constant real length; the calendar is only derived from progress
/// (see <see cref="Calendar"/>), so no rule ever depends on frame time, speed or the date.
///
/// Speeds 1..5 = 2 / 4 / 8 / 16 / 40 ticks per real second. Why these numbers:
///  • a rules cycle of <see cref="CycleTicks"/> = 4 ticks lasts 2 / 1 / 0.5 / 0.25 / 0.1 s — exactly the old «год» step,
///    so the tuned economy, bots, events and scouts keep their pace at every speed;
///  • at speed 3 (8 ticks/s) the historical calendar gives ≈ a month per tick in the Первобытная era, a few days per tick
///    in the Middle Ages and a fraction of a day per tick in modern times (≈ 1.5–2 days a second, HOI4-like);
///  • a 100 ms network turn carries 0.2…4 ticks; the fraction stays in an integer accumulator (<see cref="TickPump"/>);
///  • at most 40 ticks a second at speed 5 keeps the per-tick budget large.
/// </summary>
public static class Clock
{
    public static readonly int[] TicksPerSecond = { 0, 2, 4, 8, 16, 40 };
    public const int MinSpeed = 1, MaxSpeed = 5, ReferenceSpeed = 3;

    /// <summary>Ticks per rules cycle (economy, growth, mood, research, bots, events, capital projects).</summary>
    public const int CycleTicks = 4;

    /// <summary>Does the rules cycle run on this tick? The first one runs after a full cycle (0.5 s at speed 3).</summary>
    public static bool IsCycleTick(long tick) => (tick + 1) % CycleTicks == 0;

    /// <summary>Index of the cycle a tick belongs to (seeds the per-cycle rolls).</summary>
    public static int CycleOf(long tick) => (int)(tick / CycleTicks);

    /// <summary>Ticks in a span of real seconds at a speed (default: speed 3, the pacing reference).</summary>
    public static long TicksFor(long seconds, int speed = ReferenceSpeed) => seconds * TicksPerSecond[speed];

    /// <summary>Real hours at speed 3 that a number of ticks takes, in hundredths (for reports without floats).</summary>
    public static long HoursX100AtReference(long ticks) => ticks * 100 / (TicksPerSecond[ReferenceSpeed] * 3600L);
}
