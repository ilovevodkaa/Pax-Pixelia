using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.World;

namespace PaxPixelia.Core.Save;

/// <summary>A result file in the blitz folder: its record, or why it does not read.</summary>
public sealed record BlitzEntry(string Path, BlitzRecord Record, string Error)
{
    public bool Ok => Record != null;
}

/// <summary>
/// The blitz folder (user://blitz; user://blitz_dev for command-line runs, --blitzdir=DIR): one .pxb per finished blitz,
/// the player's own and friends' files dropped in next to them. Lists, writes, and checks results by replaying them
/// (Blitz.Check, on a worker: ≈2 s each) with the answers cached per file version.
/// </summary>
public static class BlitzStore
{
    static string _dir;
    static readonly Dictionary<string, (long Stamp, string Error)> Checked = new();
    static (int Seed, WorldData World) _world;

    public static string Dir
    {
        get
        {
            if (_dir != null) return _dir;
            var want = Cli.Str("blitzdir") ?? (OS.GetCmdlineUserArgs().Length > 0 ? "user://blitz_dev" : "user://blitz");
            _dir = want.StartsWith("user://") ? ProjectSettings.GlobalizePath(want) : want;
            return _dir;
        }
    }

    /// <summary>Write a result (the same game always gets the same file); returns its path.</summary>
    public static string Write(BlitzRecord r)
    {
        Directory.CreateDirectory(Dir);
        var path = System.IO.Path.Combine(Dir, Blitz.FileName(r));
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, System.IO.FileAccess.Write)) Blitz.Write(fs, r);
        File.Move(tmp, path, overwrite: true);
        lock (Checked) Checked.Remove(path);
        return path;
    }

    /// <summary>Every result file of a week (all weeks with null), best score first.</summary>
    public static List<BlitzEntry> List(string week = null)
    {
        var list = new List<BlitzEntry>();
        if (!Directory.Exists(Dir)) return list;
        foreach (var path in Directory.GetFiles(Dir, "*" + Blitz.Extension))
        {
            try
            {
                using var fs = File.OpenRead(path);
                var r = Blitz.Read(fs);
                if (week == null || r.Week == week) list.Add(new BlitzEntry(path, r, null));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                if (week == null) list.Add(new BlitzEntry(path, null, "Файл не читается"));
            }
        }
        return list.OrderByDescending(e => e.Record?.Score.Total ?? -1).ThenBy(e => e.Record?.SavedUnixMs ?? 0).ToList();
    }

    /// <summary>The answer of an earlier check of this file version, if any (null inside = honest).</summary>
    public static bool TryCached(BlitzEntry e, out string error)
    {
        error = null;
        long stamp = Stamp(e.Path);
        lock (Checked) if (Checked.TryGetValue(e.Path, out var c) && c.Stamp == stamp) { error = c.Error; return true; }
        return false;
    }

    /// <summary>Replay a result on a worker; null = honest, else the reason.</summary>
    public static Task<string> CheckAsync(BlitzEntry e, CancellationToken cancel = default)
    {
        if (TryCached(e, out var cached)) return Task.FromResult(cached);
        long stamp = Stamp(e.Path);
        var content = Game.Content;
        return Task.Run(() =>
        {
            var s = e.Record.Setup;
            WorldData w;
            lock (Checked) w = _world.Seed == s.Seed ? _world.World : null;
            if (w == null)
            {
                w = WorldGen.Generate(s.Seed, GameStart.WorldWidth, GameStart.WorldHeight, null, cancel);
                lock (Checked) _world = (s.Seed, w);
            }
            string error;
            try { error = Blitz.Check(e.Record, content, w, cancel); }
            catch (Exception x) when (x is not OperationCanceledException) { error = "Повтор не удался: " + x.Message; }
            lock (Checked) Checked[e.Path] = (stamp, error);
            return error;
        }, cancel);
    }

    static long Stamp(string path)
    {
        try { return File.GetLastWriteTimeUtc(path).Ticks ^ new FileInfo(path).Length; }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Open the folder in the system's file manager (friends' files go here).</summary>
    public static void OpenFolder()
    {
        Directory.CreateDirectory(Dir);
        OS.ShellOpen(Dir);
    }
}
