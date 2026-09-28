namespace PaxPixelia.Sim;

/// <summary>City spheres (see <see cref="Cities"/>). Allocated by Cities.Init in Simulation.Begin.</summary>
public sealed partial class GameState
{
    /// <summary>Per province: the city province it belongs to (-1: unowned or no city).</summary>
    public int[] City;
    /// <summary>Per city province: influence gathered towards the next province (0..Cities.Need).</summary>
    public int[] Growth;
    /// <summary>Per city province: the «sphere is full» hint was shown (reset when the city can grow again).</summary>
    public bool[] SphereNoted;
}
