using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core.Flags;

namespace PaxPixelia.Core.Nations;

/// <summary>
/// «Мои народы»: saved nation designs in user://nations/&lt;id&gt;.nation (ConfigFile, section [nation]) plus the
/// working draft in user://nation_draft.cfg — the design «Новая игра» starts with. On the very first launch the draft
/// is a random nation, so «НАЧАТЬ» works right away.
/// </summary>
public static class NationStore
{
    const string Dir = "user://nations", DraftPath = "user://nation_draft.cfg", Section = "nation";
    static NationDesign _current;

    /// <summary>The design the next game starts with (the draft of the «Народ» screen).</summary>
    public static NationDesign Current
    {
        get => _current ??= Read(DraftPath) ?? Last() ?? RandomDesign(new Random());
        set { _current = value; Write(DraftPath, value); }
    }

    /// <summary>Saved designs, most recently used first.</summary>
    public static List<NationDesign> List()
    {
        var list = new List<NationDesign>();
        if (!DirAccess.DirExistsAbsolute(Dir)) return list;
        foreach (var f in DirAccess.GetFilesAt(Dir))
            if (f.EndsWith(".nation") && Read($"{Dir}/{f}") is { } d) list.Add(d);
        list.Sort((a, b) => b.LastUsedUnix.CompareTo(a.LastUsedUnix));
        return list;
    }

    public static NationDesign Last() { var l = List(); return l.Count > 0 ? l[0] : null; }

    public static bool IsSaved(string id) => id != null && FileAccess.FileExists(PathOf(id));

    public static void Save(NationDesign d)
    {
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        Write(PathOf(d.Id), d);
    }

    public static void Delete(string id) { if (IsSaved(id)) DirAccess.RemoveAbsolute(PathOf(id)); }

    /// <summary>Marks the current design as used now (a game is starting with it): it becomes the top of «Мои народы».</summary>
    public static NationDesign TouchCurrent()
    {
        var d = Current with { LastUsedUnix = (long)Time.GetUnixTimeFromSystem() };
        Current = d;
        if (IsSaved(d.Id)) Save(d);
        return d;
    }

    public static NationDesign RandomDesign(Random rng)
    {
        var name = NationNames.Random(rng);
        var c = NationPalette.Colors[rng.Next(NationPalette.Colors.Length)];
        var flag = FlagSpec.ForBot(rng.Next(), rng.Next(1, 1000));
        return new NationDesign(NewId(), name, c.R, c.G, c.B, flag, (byte)rng.Next(NationNames.Cultures.Length), 0);
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    static string PathOf(string id) => $"{Dir}/{id}.nation";

    static void Write(string path, NationDesign d)
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "id", d.Id);
        cfg.SetValue(Section, "name", d.Name);
        cfg.SetValue(Section, "color", $"#{d.R:X2}{d.G:X2}{d.B:X2}");
        cfg.SetValue(Section, "flag", d.Flag.ToHex());
        cfg.SetValue(Section, "culture", d.Culture);
        cfg.SetValue(Section, "last_used", d.LastUsedUnix);
        if (cfg.Save(path) != Error.Ok) GD.PushWarning($"NationStore: cannot write {path}");
    }

    static NationDesign Read(string path)
    {
        var cfg = new ConfigFile();
        if (!FileAccess.FileExists(path) || cfg.Load(path) != Error.Ok) return null;
        var name = NationNames.Clean(cfg.GetValue(Section, "name", "").AsString());
        var id = cfg.GetValue(Section, "id", "").AsString();
        if (name == null || string.IsNullOrEmpty(id)) return null;
        var hex = cfg.GetValue(Section, "color", "#BE483C").AsString();
        var col = Color.FromString(hex, new Color(0.745f, 0.282f, 0.235f));
        FlagSpec.TryParseHex(cfg.GetValue(Section, "flag", "").AsString(), out var flag);
        int culture = Math.Clamp(cfg.GetValue(Section, "culture", 0).AsInt32(), 0, NationNames.Cultures.Length - 1);
        return new NationDesign(id, name, (byte)col.R8, (byte)col.G8, (byte)col.B8, flag, (byte)culture, cfg.GetValue(Section, "last_used", 0L).AsInt64());
    }
}
