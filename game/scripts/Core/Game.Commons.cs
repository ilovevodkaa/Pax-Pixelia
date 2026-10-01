using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// «Последствия общие для всех» on the Game side (Sim/Commons.cs): the land's harm and fertility for the UI (cached
/// until the next tick or command — the rules compute their own), and a chronicle line when the player's provinces
/// start to flood or wear out. A loaded game starts quietly from what it has.
/// </summary>
public partial class Game
{
    CommonsHit[] _landHits;
    int[] _landFert;
    (GameState State, long Tick, int Commands) _landKey;
    bool[] _floodTold, _grazeTold;
    GameState _commonsFor;

    void RefreshLand()
    {
        var key = (State, State.Tick, Journal.Count);
        if (_landHits != null && _landKey == key) return;
        _landKey = key;
        _landHits = Commons.Hits(World, State);
        _landFert = Commons.FertNow(World, State, _landHits);
    }

    /// <summary>The harm of floods and overgrazing to every province now (UI).</summary>
    public CommonsHit[] LandHits { get { if (!IsReady) return null; RefreshLand(); return _landHits; } }

    /// <summary>What the land gives now, climate and shared harm included, ‰ per province (UI).</summary>
    public int[] LandFert { get { if (!IsReady) return null; RefreshLand(); return _landFert; } }

    void CommonsCycle()
    {
        var hits = LandHits;
        bool quiet = !ReferenceEquals(_commonsFor, State);
        if (quiet) { _commonsFor = State; _floodTold = new bool[World.P]; _grazeTold = new bool[World.P]; }
        int floods = 0, grazes = 0, floodAt = -1, grazeAt = -1;
        for (int p = 0; p < World.P; p++)
        {
            bool mine = State.Owner[p] == Viewer;
            bool fl = hits[p].Flood > 0, gr = hits[p].Grazing > 0;
            if (fl && !_floodTold[p] && mine) { floods++; floodAt = p; }
            if (gr && !_grazeTold[p] && mine) { grazes++; grazeAt = p; }
            _floodTold[p] = fl && (mine || _floodTold[p]);
            _grazeTold[p] = gr && (mine || _grazeTold[p]);
        }
        if (quiet || State.Nat[Viewer].Camp >= 0) return;
        if (floods > 0)
            Notify("droplet-off", floods == 1
                ? $"Паводок в {World.PName[floodAt]}: выше по реке вырубили леса, и вода больше не держится"
                : $"Паводки в {floods} провинциях: выше по реке вырубили леса");
        if (grazes > 0)
            Notify("paw", grazes == 1
                ? $"Степь у {World.PName[grazeAt]} вытоптана: слишком много пастбищ вокруг"
                : $"Степь вытоптана в {grazes} провинциях: слишком много пастбищ");
    }
}
