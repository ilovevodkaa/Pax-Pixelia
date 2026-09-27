using System.Collections.Generic;

namespace PaxPixelia.Sim;

/// <summary>Simulation-owned part of the mutable state (fog memory of nations, scout bookkeeping, economy readouts).</summary>
public sealed partial class GameState
{
    // ---- fog ----
    /// <summary>Per nation: the local player has seen at least one province while that nation owned it. Monotonic.</summary>
    public bool[] Met;

    /// <summary>
    /// Per province: the owner the local player last saw (-1 = tribes or never seen). Refreshed while the province is
    /// visible; a stale province keeps showing it, so bot expansion out of sight stays hidden until scouted again.
    /// </summary>
    public short[] KnownOwner;

    /// <summary>The owner the local player's map shows for p: the real one when visible or in observer mode,
    /// the remembered one when stale, none under the clouds.</summary>
    public int VisibleOwner(int p)
    {
        if (!FogEnabled || Fog == null || KnownOwner == null) return Owner[p];
        return Fog[p] switch { 2 => Owner[p], 1 => KnownOwner[p], _ => -1 };
    }

    public int ScoutSeq;            // last Scout.Id handed out

    // ---- economy readouts of the last year (top-bar tooltip: «Налоги +18 · Содержание −6») ----
    public double LastTaxes;
    public double LastUpkeep;
    public double LastIncome => LastTaxes - LastUpkeep;

    // ---- chronicle ----
    public int EventCount;          // events fired so far (drives the event deck)
    /// <summary>Current capital project (Simulation.Projects), -1 once everything is built.</summary>
    public int ProjectIndex;
    /// <summary>Bit i set: the one-off project i (no building attached) is finished.</summary>
    public int ProjectsDone;

    /// <summary>Reusable buffers for BFS passes (not game state: never serialised, rebuilt on demand).</summary>
    internal SimScratch Scratch;
}
