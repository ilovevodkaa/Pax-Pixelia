using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core.Audio;

namespace PaxPixelia.Core;

/// <summary>
/// Player settings (autoload "Settings", registered first; port of «Mr. President» settings_store.gd): values apply
/// the moment they change so the player sees the result, but reach user://settings.cfg only on <see cref="Save"/>;
/// <see cref="Revert"/> returns to the saved state. Under game CLI flags (tests, screenshots) the video part is never
/// applied, so --selftest / --shot keep their 1280×720 / 1600×900 windows (MAIN_MENU.md §2.8 p.7).
/// </summary>
public partial class Settings : Node
{
    public static Settings I { get; private set; }

    const string FilePath = "user://settings.cfg";
    const int Version = 1;
    public const string Video = "video", Audio = "audio", Ui = "ui";

    public static readonly Vector2I[] Resolutions =
        { new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };
    public static readonly int[] FpsSteps = { 30, 60, 120, 144, 0 };
    public static readonly int[] ScaleSteps = { 0, 100, 150, 200 };

    static readonly (string Section, string Key, object Value)[] Defaults =
    {
        (Video, "mode", 0), (Video, "resolution", "1600x900"), (Video, "vsync", true), (Video, "fps", 0),
        (Audio, "master", 80), (Audio, "music", 70), (Audio, "sfx", 80), (Audio, "background", false),
        (Ui, "scale", 0), (Ui, "reduced_motion", false), (Ui, "discord", true), (Ui, "smart_time", true),
    };

    // int / bool / string only: plain C# values compare by value (Variant does not)
    readonly Dictionary<string, object> _values = new(), _saved = new();
    bool _focused = true;

    /// <summary>Raised with "section/key" after a value changed (and was applied).</summary>
    public event Action<string> Changed;

    /// <summary>True when the process runs a game CLI mode (any user arg other than --front* / --no-motion).</summary>
    public static bool CliMode { get; } = DetectCli();

    public override void _EnterTree()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        foreach (var (s, k, v) in Defaults) _values[Id(s, k)] = v;
        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) == Error.Ok)
            foreach (var (s, k, v) in Defaults)
            {
                var loaded = cfg.GetValue(s, k, ToVariant(v));
                _values[Id(s, k)] = v switch
                {
                    bool => loaded.VariantType == Variant.Type.Bool ? loaded.AsBool() : v,
                    int => loaded.VariantType == Variant.Type.Int ? loaded.AsInt32() : v,
                    _ => loaded.VariantType == Variant.Type.String ? loaded.AsString() : v,
                };
            }
        Snapshot();
    }

    public override void _Ready()
    {
        Sfx.EnsureBus(Sfx.Bus, -4f);
        Sfx.EnsureBus(Sfx.MusicBus, 0f);
        ApplyAll();
    }

    public override void _ExitTree() { if (I == this) I = null; }

    // ---- values ----
    public T Get<T>(string section, string key) => _values.TryGetValue(Id(section, key), out var v) && v is T t ? t : default;

    /// <summary>Changes a value and applies it right away (not saved until <see cref="Save"/>).</summary>
    public void Set<T>(string section, string key, T value)
    {
        var id = Id(section, key);
        if (_values.TryGetValue(id, out var old) && Equals(old, value)) return;
        _values[id] = value;
        Apply(section, key);
        Changed?.Invoke(id);
    }

    public bool IsDirty
    {
        get
        {
            foreach (var (id, v) in _values) if (!Equals(_saved[id], v)) return true;
            return false;
        }
    }

    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("meta", "version", Version);
        foreach (var (s, k, _) in Defaults) cfg.SetValue(s, k, ToVariant(_values[Id(s, k)]));
        if (cfg.Save(FilePath) != Error.Ok) GD.PushWarning($"Settings: cannot write {FilePath}");
        Snapshot();
    }

    /// <summary>Back to what is on disk (the «Не сохранять» answer).</summary>
    public void Revert()
    {
        foreach (var (id, v) in _saved) _values[id] = v;
        ApplyAll();
        Changed?.Invoke("*");
    }

    public void ResetSection(string section)
    {
        foreach (var (s, k, v) in Defaults) if (s == section) _values[Id(s, k)] = v;
        ApplyAll();
        Changed?.Invoke(section + "/*");
    }

    public bool ReducedMotion => Get<bool>(Ui, "reduced_motion");

    /// <summary>UI scale for CanvasLayer.Scale: 1 / 1.5 / 2 (keeps the pixel font on its grid: 22 → 33 → 44). «Авто» goes
    /// by the monitor (height &lt; 1300 → 100%, &lt; 2000 → 150%, else 200%); any choice is capped so that the window still
    /// holds a 1280×720 layout — a 1600×900 window on a 1440p monitor stays at 100%.</summary>
    public float UiScale
    {
        get
        {
            int s = Get<int>(Ui, "scale");
            float want = s > 0 ? s / 100f : AutoScale();
            var win = GetTree()?.Root?.Size ?? new Vector2I(1600, 900);
            float fit = Mathf.Min(win.X / 1280f, win.Y / 720f);
            foreach (float step in new[] { 2f, 1.5f, 1f })
                if (step <= want && step <= fit) return step;
            return 1f;
        }
    }

    /// <summary>The «Авто» step for the current monitor.</summary>
    public static float AutoScale()
    {
        int h = DisplayServer.ScreenGetSize().Y;
        return h < 1300 ? 1f : h < 2000 ? 1.5f : 2f;
    }

    public Vector2I Resolution => ParseResolution(Get<string>(Video, "resolution"));

    /// <summary>Resolutions that fit the current screen (the smallest always stays).</summary>
    public static List<Vector2I> AvailableResolutions()
    {
        var usable = DisplayServer.ScreenGetUsableRect().Size;
        var list = new List<Vector2I> { Resolutions[0] };
        for (int i = 1; i < Resolutions.Length; i++)
            if (Resolutions[i].X <= usable.X && Resolutions[i].Y <= usable.Y) list.Add(Resolutions[i]);
        return list;
    }

    public static string ResolutionText(Vector2I r) => $"{r.X}x{r.Y}";

    public static Vector2I ParseResolution(string s)
    {
        var parts = (s ?? "").Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w >= 640 && h >= 360
            ? new Vector2I(w, h) : new Vector2I(1600, 900);
    }

    // ---- applying ----
    void ApplyAll()
    {
        foreach (var (s, k, _) in Defaults) Apply(s, k);
    }

    void Apply(string section, string key)
    {
        switch (section)
        {
            case Video when !CliMode:
                if (key is "mode" or "resolution") ApplyWindow();
                else if (key == "vsync") DisplayServer.WindowSetVsyncMode(Get<bool>(Video, "vsync") ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
                else if (key == "fps") ApplyFps();
                break;
            case Audio:
                ApplyAudio();
                break;
        }
    }

    void ApplyWindow()
    {
        var want = Get<int>(Video, "mode") switch
        {
            1 => DisplayServer.WindowMode.Fullscreen,
            2 => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        var win = GetTree().Root;
        if (DisplayServer.WindowGetMode() != want) DisplayServer.WindowSetMode(want);
        if (want != DisplayServer.WindowMode.Windowed) return;
        var size = Resolution;
        var usable = DisplayServer.ScreenGetUsableRect(win.CurrentScreen);
        if (size.X > usable.Size.X || size.Y > usable.Size.Y || win.Size == size) return;   // no needless window blink
        win.Size = size;
        win.Position = usable.Position + (usable.Size - size) / 2;
    }

    void ApplyFps() => Engine.MaxFps = _focused ? Get<int>(Video, "fps") : 15;

    void ApplyAudio()
    {
        SetBus("Master", Get<int>(Audio, "master"));
        SetBus(Sfx.MusicBus, Get<int>(Audio, "music"));
        SetBus(Sfx.Bus, Get<int>(Audio, "sfx"));
        int master = AudioServer.GetBusIndex("Master");
        AudioServer.SetBusMute(master, !_focused && !Get<bool>(Audio, "background") && !CliMode);
    }

    static void SetBus(string bus, int percent)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        // the SFX bus keeps the −4 dB headroom of Mr. President under the player's volume
        float trim = bus == Sfx.Bus ? -4f : 0f;
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Max(percent / 100f, .0001f)) + trim);
    }

    public override void _Notification(int what)
    {
        if (what is not ((int)NotificationApplicationFocusOut or (int)NotificationApplicationFocusIn) || CliMode) return;
        _focused = what == NotificationApplicationFocusIn;
        ApplyFps();   // a window in the background idles at 15 fps
        ApplyAudio();
    }

    void Snapshot()
    {
        _saved.Clear();
        foreach (var (id, v) in _values) _saved[id] = v;
    }

    static string Id(string section, string key) => section + "/" + key;

    static Variant ToVariant(object v) => v switch { bool b => b, int i => i, _ => (string)v };

    static bool DetectCli()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (!a.StartsWith("--front") && a != "--no-motion") return true;
        return false;
    }
}
