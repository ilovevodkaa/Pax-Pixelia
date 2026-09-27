using PaxPixelia.Core;

namespace PaxPixelia.Sim;

/// <summary>Godot-side entry of the yearly rules (the pure part is Simulation.Core.cs).</summary>
public static partial class Simulation
{
    /// <summary>Called by Game._Process once per game year.</summary>
    public static void YearTick(Game g) => Year(g.World, g.State, g);
}
