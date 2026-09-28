namespace PaxPixelia.Core;

/// <summary>The map's capture fills (Map/ProvinceTransitions) as the rest of the game sees them.</summary>
internal interface ICaptureFills
{
    /// <summary>Owner still shown for p while its capture fill runs (-1 = unowned), int.MinValue when none runs there.</summary>
    int HeldOwner(int p);
    /// <summary>Some capture fill is running.</summary>
    bool Running { get; }
}

/// <summary>
/// Capture fill (GDD «Захват провинции — анимация заливки»): a captured province fills with its new owner's colour over
/// ~0.9 s, and everything else on the map — nation names, city colours, the minimap — switches when the fill ends.
/// </summary>
public partial class Game
{
    /// <summary>Set by the map while it exists.</summary>
    internal ICaptureFills CaptureFills { get; set; }

    /// <summary>Owner the map shows for p: <see cref="Sim.GameState.VisibleOwner(int)"/>, except that a province whose
    /// capture fill is still running keeps its old owner until the fill ends.</summary>
    public int ShownOwner(int p)
    {
        var f = CaptureFills;
        if (f != null) { int o = f.HeldOwner(p); if (o != int.MinValue) return o; }
        return State.VisibleOwner(p);
    }

    /// <summary>A capture fill is running on the map: the minimap holds its old colours and snaps when it ends.</summary>
    public bool CaptureFillsRunning => CaptureFills?.Running ?? false;
}
