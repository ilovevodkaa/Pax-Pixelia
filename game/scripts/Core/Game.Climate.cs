using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// «Дышащая планета» on the Game side (Sim/Climate.cs): one chronicle line when the world enters a new climate epoch —
/// the deserts drying, the Little Ice Age, its end, the warming. A new or loaded game starts from its own epoch quietly.
/// </summary>
public partial class Game
{
    GameState _climateFor;
    int _climateStage;

    void ClimateCycle()
    {
        int stage = Climate.Stage(Climate.Year(State));
        if (!ReferenceEquals(_climateFor, State)) { _climateFor = State; _climateStage = stage; return; }
        if (stage == _climateStage) return;
        _climateStage = stage;
        if (Climate.StageText(stage) is { } text) Notify(stage switch { 1 => "droplet-off", 2 => "cloud-fog", _ => "sun" }, text);
    }
}
