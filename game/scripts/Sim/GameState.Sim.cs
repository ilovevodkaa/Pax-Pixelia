namespace PaxPixelia.Sim;

/// <summary>
/// The local player's view of the state, kept source-compatible for the UI and the map (they read Gold, Fog, Year…).
/// Rules never use these members: they take the nation explicitly and read Nat[n].
/// </summary>
public sealed partial class GameState
{
    /// <summary>The nation this client plays (UI/map view only — the rules never ask who is local).</summary>
    public const int LocalPlayer = 0;

    NationState Me => Nat[LocalPlayer];

    // ---- calendar ----
    public CalendarDate Date => Calendar.DateOf(Day256);
    /// <summary>Calendar year: negative = до н. э., no year 0.</summary>
    public int Year => Date.Year;
    public static string YearText(int y) => Calendar.YearText(y);

    // ---- treasury in gold (the rules keep hundredths in Nat[n].Treasury) ----
    public double Gold // pax-allow: UI view of Treasury
    {
        get => Me.Treasury / 100.0; // pax-allow
        set => Me.Treasury = (long)System.Math.Round(value * 100); // pax-allow
    }
    /// <summary>Taxes, upkeep and their difference in the last rules cycle, in gold.</summary>
    public double LastTaxes => Me.LastTaxes / 100.0; // pax-allow
    public double LastUpkeep => Me.LastUpkeep / 100.0; // pax-allow
    public double LastIncome => (Me.LastTaxes - Me.LastUpkeep) / 100.0; // pax-allow

    // ---- capital construction queue of the local player ----
    public int ProjectIndex { get => Me.ProjectIndex; set => Me.ProjectIndex = value; }
    public int ProjectsDone { get => Me.ProjectsDone; set => Me.ProjectsDone = value; }
    public int QueuePct { get => Me.QueuePct; set => Me.QueuePct = value; }
    public string QueueName => Me.ProjectIndex >= 0 ? Simulation.Projects[Me.ProjectIndex].Name : null;
    public int EventCount => Me.EventCount;

    // ---- fog of the local player ----
    public byte[] Fog => Me.Fog?.Fog;
    public bool[] Explored => Me.Fog?.Explored;
    public short[] KnownOwner => Me.Fog?.KnownOwner;
    public bool[] Met => Me.Fog?.Met;

    /// <summary>The owner the local player's map shows for p: the real one when visible or in observer mode,
    /// the remembered one when stale, none under the clouds.</summary>
    public int VisibleOwner(int p) => VisibleOwner(LocalPlayer, p);

    /// <summary>The owner nation n's map shows for p.</summary>
    public int VisibleOwner(int n, int p)
    {
        var f = Nat[n].Fog;
        if (!FogEnabled || f == null) return Owner[p];
        return f.Fog[p] switch { 2 => Owner[p], 1 => f.KnownOwner[p], _ => -1 };
    }
}
