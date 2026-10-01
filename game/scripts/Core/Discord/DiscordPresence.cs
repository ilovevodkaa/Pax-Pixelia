using System;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Godot;
using PaxPixelia.Sim;

namespace PaxPixelia.Core.Discord;

/// <summary>
/// Autoload "DiscordPresence": the «Играет в Pax Pixelia» card in Discord — the nation, its era and the date, or
/// «Блиц недели». Looks at <see cref="Game.I"/> every couple of seconds and hands the card to <see cref="DiscordIpc"/>,
/// which sends only what changed. Off under CLI modes (tests, screenshots) and with «Статус в Discord» switched off;
/// a missing Discord is silent. Pictures are the art assets of the Discord application (docs/discord/README.md):
/// «logo» and «era0»…«era10».
/// </summary>
public partial class DiscordPresence : Node
{
    /// <summary>Application ID of «Pax Pixelia» in the Discord Developer Portal.</summary>
    public const string ClientId = "1555160698739499028";

    const double PollSeconds = 2;
    const int MaxText = 128;   // Discord refuses longer details / state

    DiscordIpc _ipc;
    readonly ConcurrentQueue<string> _log = new();   // the IPC task's messages, printed on the main thread
    double _wait;
    object _game;              // the GameState the card's clock started with (a new game or a load restarts it)
    long _since = Now();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;   // keeps updating in pause menus
        if (Settings.I != null) Settings.I.Changed += OnSetting;
        Toggle();
    }

    public override void _ExitTree()
    {
        if (Settings.I != null) Settings.I.Changed -= OnSetting;
        Stop();
    }

    void OnSetting(string id)
    {
        if (id is "ui/discord" or "*" or "ui/*") Toggle();
    }

    void Toggle()
    {
        bool want = ClientId.Length > 0 && !Settings.CliMode && (Settings.I?.Get<bool>(Settings.Ui, "discord") ?? true);
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
        _ipc.Post(Activity());
    }

    JsonObject Activity()
    {
        var g = Game.I;
        var state = g is { IsReady: true } ? g.State : null;
        if (!ReferenceEquals(state, _game)) { _game = state; _since = Now(); }

        var assets = new JsonObject { ["large_image"] = "logo", ["large_text"] = "Pax Pixelia" };
        string details, line;
        if (state == null)
            (details, line) = ("В главном меню", null);
        else
        {
            string nation = g.Nations[g.Viewer].Name;
            assets["small_image"] = $"era{Math.Clamp(g.EraIndex, 0, Eras.Last)}";
            assets["small_text"] = g.EraName;
            if (g.IsBlitz)
                (details, line) = (g.BlitzOver ? "Блиц недели · итог" : "Блиц недели", $"{nation} · {g.EraName}");
            else
                (details, line) = ($"{nation} · {g.EraName}", g.DateText);
        }

        var activity = new JsonObject
        {
            ["details"] = Fit(details),
            ["timestamps"] = new JsonObject { ["start"] = _since },
            ["assets"] = assets,
        };
        if (Fit(line) is { } l) activity["state"] = l;
        return activity;
    }

    /// <summary>Discord takes 2…128 characters.</summary>
    static string Fit(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.Length < 2) s += " ";
        return s.Length <= MaxText ? s : s[..(MaxText - 1)] + "…";
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
