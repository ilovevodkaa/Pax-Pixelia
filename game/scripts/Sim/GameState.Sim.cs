using System.Collections.Generic;

namespace PaxPixelia.Sim;

/// <summary>Simulation-owned part of the mutable state (fog memory of nations, scout bookkeeping, economy readouts).</summary>
public sealed partial class GameState
{
    // ---- fog ----
    /// <summary>Per nation: the local player has explored at least one of its provinces. Monotonic.</summary>
    public bool[] Met;

    public int ScoutSeq;            // last Scout.Id handed out

    // ---- economy readouts of the last year (top-bar tooltip: «Налоги +18 · Содержание −6») ----
    public double LastTaxes;
    public double LastUpkeep;
    public double LastIncome => LastTaxes - LastUpkeep;

    // ---- chronicle ----
    public int EventCount;          // events fired so far (drives the event deck)
    public int ProjectIndex;        // current capital project (Simulation.Projects)

    /// <summary>Reusable buffers for BFS passes (not game state: never serialised, rebuilt on demand).</summary>
    internal SimScratch Scratch;
}
