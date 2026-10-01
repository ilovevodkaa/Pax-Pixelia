using System;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Godot;

namespace PaxPixelia.Core.Discord;

/// <summary>
/// Autoload "DiscordPresence": the «Играет в Pax Pixelia» card in Discord. Every couple of seconds asks
/// <see cref="PresenceCard"/> what to show and hands it to <see cref="DiscordIpc"/>, which sends only what changed.
/// Off under CLI modes (tests, screenshots; --discord switches it on and prints each card) and with «Статус в Discord»
/// switched off; a missing Discord is silent.
/// </summary>
public partial class DiscordPresence : Node
{
    /// <summary>Application ID of «Pax Pixelia» in the Discord Developer Portal.</summary>
    public const string ClientId = "1555160698739499028";

    const double PollSeconds = 2;

    DiscordIpc _ipc;
    readonly ConcurrentQueue<string> _log = new();   // the IPC task's messages, printed on the main thread
    double _wait;
    object _game;              // the GameState the card's clock started with (a new game or a load restarts it)
    long _since = Now();
    string _printed;           // --discord: the last card printed
    string _making;            // the world generation's status text while one runs

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;   // keeps updating in pause menus
        if (Settings.I != null) Settings.I.Changed += OnSetting;
        if (Game.I != null)
        {
            Game.I.GenerationProgress += OnGenerating;
            Game.I.WorldReady += OnSettled;
            Game.I.GameEnded += OnSettled;
        }
        Toggle();
    }

    public override void _ExitTree()
    {
        if (Settings.I != null) Settings.I.Changed -= OnSetting;
        if (Game.I != null)
        {
            Game.I.GenerationProgress -= OnGenerating;
            Game.I.WorldReady -= OnSettled;
            Game.I.GameEnded -= OnSettled;
        }
        Stop();
    }

    void OnSetting(string id)
    {
        if (id is "ui/discord" or "*" or "ui/*") Toggle();
    }

    void Toggle()
    {
        bool want = ClientId.Length > 0 && (!Settings.CliMode || Cli.Has("discord")) && (Settings.I?.Get<bool>(Settings.Ui, "discord") ?? true);
        if (want == (_ipc != null)) return;
        if (!want) { Stop(); return; }
        _ipc = new DiscordIpc(ClientId);
        _ipc.Log += _log.Enqueue;   // never call Godot from the IPC task
        _wait = 0;
    }

    void Stop()
    {
        _ipc?.Dispose();   // closing the connection takes the card off Discord at once
        _ipc = null;
    }

    public override void _Process(double delta)
    {
        while (_log.TryDequeue(out var m)) GD.Print(m);
        if (_ipc == null || (_wait -= delta) > 0) return;
        _wait = PollSeconds;
        var card = Activity();
        _ipc.Post(card);
        if (Cli.Has("discord") && card.ToJsonString() is var json && json != _printed)
        {
            _printed = json;
            GD.Print($"discord card: {card["details"]} | {card["state"]} | {card["assets"]?["large_image"]} + {card["assets"]?["small_image"]} «{card["assets"]?["small_text"]}»");
        }
    }

    JsonObject Activity()
    {
        var g = Game.I;
        var state = g is { IsReady: true } ? g.State : null;
        if (!ReferenceEquals(state, _game)) { _game = state; _since = Now(); }
        return PresenceCard.Build(g, _since, state == null ? _making : null);
    }

    void OnGenerating(string text) => _making = text;
    void OnSettled() => _making = null;

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
