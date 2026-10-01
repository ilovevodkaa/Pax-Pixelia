using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// Watching a friend's blitz: the game starts from the result's own setup (GameStart.Create, as the friend's did) and
/// feeds the friend's journal at its very ticks instead of taking orders — the viewer only pauses and sets the speed,
/// and sees the world through the friend's eyes (nation 0, its fog). Nothing is saved and no result is written; at the
/// end the card says whether the replay landed on the file's hash, i.e. the score is honest.
/// </summary>
public partial class Game
{
    BlitzRecord _replay, _pendingReplay;
    int _replayAt;

    /// <summary>The result being watched (null in a game of one's own).</summary>
    public BlitzRecord Replay => IsReady ? _replay : null;
    public bool IsReplay => IsReady && _replay != null;

    /// <summary>The next NewGame watches this result instead of being played (Main, from the blitz table).</summary>
    public void PrepareReplay(BlitzRecord r) => _pendingReplay = r;

    void TakeReplay() { _replay = _pendingReplay; _pendingReplay = null; _replayAt = 0; }

    void DropReplay() { _replay = null; _pendingReplay = null; _replayAt = 0; }

    /// <summary>The journal's game commands due at each tick boundary (session ones are the viewer's), then the tick.</summary>
    TickReport RunReplay(int ticks)
    {
        var r = new TickReport();
        var j = _replay.Journal;
        for (int t = 0; t < ticks; t++)
        {
            while (_replayAt < j.Count && j[_replayAt].Tick <= State.Tick)
            {
                var c = j[_replayAt++];
                if (!c.IsSession) Commands.Apply(World, State, c, this);
            }
            r.Add(Simulation.Step(World, State, this));
        }
        return r;
    }

    /// <summary>The replay ended on the file's own state: the score is honest.</summary>
    public bool ReplayMatches => IsReplay && BlitzOver && State.Hash().All == _replay.StateHash;

    void AnnounceReplay()
    {
        if (!IsReplay) return;
        Notify("eye", $"Повтор партии «{_replay.NationName}» ({Blitz.WeekTitle(_replay.Week)}, {_replay.Score.Total} очков): приказы идут из файла, вы только смотрите. Пробел — пауза, 1–5 — скорость");
    }
}
