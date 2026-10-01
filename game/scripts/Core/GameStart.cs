using PaxPixelia.Content;
using PaxPixelia.Core.Nations;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Core;

/// <summary>
/// The one way a game's first state is made from its setup (pure C#): Game.NewGame runs it, and so does the blitz check
/// that replays a friend's journal — so a replay starts from exactly the state the player started from.
/// </summary>
public static class GameStart
{
    /// <summary>Size of every game world, px.</summary>
    public const int WorldWidth = 2560, WorldHeight = 1440;

    /// <summary>Roster, nations, pace, fog, the nomad start, the rules' first pass and the event deck (null = none).</summary>
    public static GameState Create(WorldData w, GameSetup setup, ContentDb content)
    {
        var st = NationGen.CreateInitialState(w, NationRoster.Build(setup));
        st.Pace = Eras.ClampPace(setup.PacePermille);
        st.FogEnabled = setup.Fog;
        st.Paused = setup.StartPaused;
        if (setup.Nomad) Nomads.Start(w, st);
        Simulation.Begin(w, st);
        if (content != null) st.Events = new SimEvents(content, w, st, setup.JokePercent);
        return st;
    }
}
