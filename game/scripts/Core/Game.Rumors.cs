using System.Collections.Generic;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// Rumors of unmet nations (Sim/Rumors.cs) on the Game side: the viewer's list, recomputed when the fog or a rules cycle
/// changes it, and the chronicle line for each nation heard of the first time. A loaded game takes what it hears at
/// once as already told (no burst of old news); a new game tells its first rumors on the first cycle.
/// </summary>
public partial class Game
{
    readonly List<Rumor> _rumors = new();
    readonly List<int> _heard = new();
    GameState _rumorsFor;
    bool _rumorsDirty = true, _rumorsQuiet;

    /// <summary>What the player hears now (empty without fog).</summary>
    public IReadOnlyList<Rumor> HeardRumors
    {
        get
        {
            if (!IsReady) return System.Array.Empty<Rumor>();
            if (!ReferenceEquals(_rumorsFor, State))
            {
                _rumorsFor = State; _heard.Clear(); _rumorsDirty = true;
                _rumorsQuiet = SavePath != null;   // a loaded game: what it hears now is old news
            }
            if (_rumorsDirty) { Rumors.Of(World, State, Viewer, _rumors); _rumorsDirty = false; }
            return _rumors;
        }
    }

    /// <summary>The rumor pointing at province p, or null.</summary>
    public Rumor? RumorAt(int p) => IsReady && State.FogEnabled ? Rumors.At(HeardRumors, p) : null;

    /// <summary>«Ночью на северо-западе видели дым чужих костров».</summary>
    public string RumorText(in Rumor r) => Rumors.Text(World, State, Viewer, r);

    void MarkRumorsDirty() => _rumorsDirty = true;

    /// <summary>Every rules cycle: refresh, and tell the chronicle about nations heard of for the first time.</summary>
    void RumorsCycle()
    {
        _rumorsDirty = true;
        var list = HeardRumors;
        foreach (var r in list)
        {
            if (_heard.Contains(r.Nation)) continue;
            _heard.Add(r.Nation);
            if (!_rumorsQuiet && State.FogEnabled) Notify("smoke", RumorText(r));
        }
        _rumorsQuiet = false;
    }
}
