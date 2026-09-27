using System.Collections.Generic;

namespace PaxPixelia.Sim;

/// <summary>
/// Where the pure simulation reports what happened. The Game autoload implements it (forwarding to its events);
/// the console tests implement it with a recorder. Method names match Game's so the implementation is implicit.
/// </summary>
public interface ISimSink
{
    /// <summary>A chronicle entry. icon = Tabler icon name without «ti-» (flag, hammer, map-2, affiliate, bulb …).</summary>
    void Notify(string icon, string text);
    /// <summary>Ownership / buildings / religion changed for these provinces.</summary>
    void RaiseProvincesChanged(IReadOnlyList<int> provinces);
    /// <summary>Fog state changed for these provinces (null = all).</summary>
    void RaiseFogChanged(IReadOnlyList<int> provinces);
    /// <summary>A human nation's event choice opened or closed (SimEvents; read GameState.Events.Pending).</summary>
    void EventChoiceChanged(int nation) { }
}
