using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IOFileAccess = System.IO.FileAccess;

namespace PaxPixelia.Core.Save;

/// <summary>A save file on disk with its header (Error set when this build cannot load it).</summary>
public sealed record SaveEntry(string Path, SaveHeader Header, string Error, long ModifiedUnixMs)
{
    public bool Ok => Error == null && Header != null;
    public string ThumbPath => System.IO.Path.ChangeExtension(Path, ".png");
    public string FileName => System.IO.Path.GetFileName(Path);
    public long SortKey => Header?.SavedUnixMs ?? ModifiedUnixMs;
}

/// <summary>What a write did: the file, how long it took (ms: capture, thumbnail and file) and its size in bytes.</summary>
public readonly record struct SaveResult(bool Ok, string Path, string Error, long Ms, int Bytes)
{
    public static SaveResult Fail(string error, string path = null, long ms = 0) => new(false, path, error, ms, 0);
}

/// <summary>
/// The saves folder (<see cref="Dir"/>): three rotating autosave slots auto_1..3 (by the calendar and on leaving the
/// game), manual saves by name (the name lives in the header, the file is manual_&lt;time&gt;.pxs) and a spare quick
/// slot. A write goes to a temporary file that is then renamed over the old one, so a crash mid-write never leaves
/// half a save. Every save has its 320×180 thumbnail next to it (same name, .png). Lists read only the headers.
/// Pure file work: capturing the game is Game.CaptureSave / SaveNow / SaveAsync (Core/Save/Game.Save.cs).
/// </summary>
public static class SaveStore
{
    public const int AutoSlots = 3;
    public const string QuickFile = "quick" + SaveFile.Extension;

    static string _dir;

    /// <summary>
    /// Absolute path of the saves folder: --savedir=DIR (user:// or absolute) when given; user://saves_dev for runs with
    /// command-line flags (tests and screenshots never touch the player's saves); user://saves otherwise.
    /// </summary>
    public static string Dir
    {
        get
        {
            if (_dir != null) return _dir;
            var want = Cli.Str("savedir") ?? (OS.GetCmdlineUserArgs().Length > 0 ? "user://saves_dev" : "user://saves");
            _dir = Globalize(want);
            return _dir;
        }
        set => _dir = value == null ? null : Globalize(value);   // tests
    }

    static string Globalize(string p) => p.StartsWith("user://") || p.StartsWith("res://") ? ProjectSettings.GlobalizePath(p) : p;

    // ------------------------------------------------------------------ listing

    /// <summary>Every save in the folder, newest first (headers only; broken files are listed with their error).</summary>
    public static List<SaveEntry> List()
    {
        var list = new List<SaveEntry>();
        if (!Directory.Exists(Dir)) return list;
        foreach (var path in Directory.GetFiles(Dir, "*" + SaveFile.Extension))
        {
            long mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
            try
            {
                using var fs = new FileStream(path, FileMode.Open, IOFileAccess.Read, FileShare.Read, 4096);
                list.Add(new SaveEntry(path, SaveFile.ReadHeader(fs), null, mtime));
            }
            catch (SaveException e) { list.Add(new SaveEntry(path, null, e.Message, mtime)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { list.Add(new SaveEntry(path, null, SaveException.Text(SaveError.Io), mtime)); }
        }
        return list.OrderByDescending(e => e.SortKey).ThenBy(e => e.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>The newest save this build can load, or null («ПРОДОЛЖИТЬ»): its body is checked too.</summary>
    public static SaveEntry Latest() => List().FirstOrDefault(e => Check(e) == null);

    static readonly Dictionary<string, (long Stamp, string Error)> Checked = new();

    /// <summary>
    /// The whole file checked (checksums, unpacking, chunks — not the world, which needs regenerating): null when it
    /// reads, else the reason for the player. A list reads only headers; this is for the save about to be offered.
    /// Cached per file version.
    /// </summary>
    public static string Check(SaveEntry e)
    {
        if (!e.Ok) return e.Error;
        long stamp;
        try { stamp = File.GetLastWriteTimeUtc(e.Path).Ticks ^ new FileInfo(e.Path).Length; }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { return SaveException.Text(SaveError.Io); }
        lock (Checked) if (Checked.TryGetValue(e.Path, out var c) && c.Stamp == stamp) return c.Error;
        string error = null;
        try { ReadFile(e.Path); }
        catch (SaveException x) { error = x.Message; }
        lock (Checked) Checked[e.Path] = (stamp, error);
        return error;
    }

    /// <summary>A save that read fine but failed to load (its world comes out differently in this build, its state
    /// does not add up): «ПРОДОЛЖИТЬ» skips it and «Загрузить» shows the reason until the file changes.</summary>
    public static void MarkBroken(string path, string error)
    {
        try
        {
            long stamp = File.GetLastWriteTimeUtc(path).Ticks ^ new FileInfo(path).Length;
            lock (Checked) Checked[path] = (stamp, error);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    public static bool Any() => Directory.Exists(Dir) && Directory.EnumerateFiles(Dir, "*" + SaveFile.Extension).Any();

    /// <summary>Read a whole save (throws SaveException).</summary>
    public static (SaveHeader, SaveBody) ReadFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new SaveException(SaveError.Missing, Path.GetFileName(path ?? ""));
        try
        {
            using var fs = new FileStream(path, FileMode.Open, IOFileAccess.Read, FileShare.Read, 1 << 16);
            return SaveFile.Read(fs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new SaveException(SaveError.Io, e.Message, e); }
    }

    /// <summary>A save path given on the command line or by a test: «latest», a file name in <see cref="Dir"/> or a full path.</summary>
    public static string Resolve(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return null;
        if (arg == "latest" || arg == "1") return Latest()?.Path;
        var p = Globalize(arg);
        if (!Path.IsPathRooted(p)) p = Path.Combine(Dir, p);
        if (!p.EndsWith(SaveFile.Extension, StringComparison.OrdinalIgnoreCase) && !File.Exists(p)) p += SaveFile.Extension;
        return p;
    }

    // ------------------------------------------------------------------ slots

    /// <summary>The autosave slot to write next: a free one, else the oldest — never <paramref name="keep"/> (the save being loaded).</summary>
    public static string NextAutoSlot(string keep = null)
    {
        string best = null;
        long bestTime = long.MaxValue;
        for (int i = 1; i <= AutoSlots; i++)
        {
            var path = Path.Combine(Dir, $"auto_{i}{SaveFile.Extension}");
            if (keep != null && SamePath(path, keep)) continue;
            if (!File.Exists(path)) return path;
            long t = File.GetLastWriteTimeUtc(path).Ticks;
            if (t < bestTime) { bestTime = t; best = path; }
        }
        return best;
    }

    static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The loadable manual save called <paramref name="name"/> (case-insensitive), or null.</summary>
    public static SaveEntry FindManual(string name) =>
        List().FirstOrDefault(e => e.Header is { Kind: SaveKind.Manual } h && string.Equals(h.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A new file for a manual save: «manual_&lt;time&gt;.pxs» (the name lives in the header).</summary>
    public static string NewManualPath()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        var path = Path.Combine(Dir, $"manual_{stamp}{SaveFile.Extension}");
        for (int k = 2; File.Exists(path); k++) path = Path.Combine(Dir, $"manual_{stamp}_{k}{SaveFile.Extension}");
        return path;
    }

    public static string PathFor(SaveKind kind, string keep = null) => kind switch
    {
        SaveKind.Quick => Path.Combine(Dir, QuickFile),
        SaveKind.Manual => NewManualPath(),
        _ => NextAutoSlot(keep),
    };

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// Write a captured save to <paramref name="path"/> through a temporary file (safe on any thread: no Godot calls),
    /// then its thumbnail (PNG bytes; null removes a stale one). Returns the size in bytes; throws IO exceptions.
    /// </summary>
    public static int WriteFile(string path, SaveHeader header, SaveBody body, byte[] thumbPng)
    {
        lock (WriteLock) return WriteLocked(path, header, body, thumbPng);   // a worker's write and the Alt+F4 save never interleave
    }

    /// <summary>
    /// Write a captured save to its kind's slot (<see cref="PathFor"/>), chosen under the write lock: two saves at once (the
    /// calendar autosave and the one on leaving) never pick the same free autosave slot. Returns the path and the size.
    /// </summary>
    public static (string Path, int Bytes) WriteSlot(SaveKind kind, string keep, SaveHeader header, SaveBody body, byte[] thumbPng)
    {
        lock (WriteLock)
        {
            var path = PathFor(kind, keep);
            return (path, WriteLocked(path, header, body, thumbPng));
        }
    }

    static readonly object WriteLock = new();

    static int WriteLocked(string path, SaveHeader header, SaveBody body, byte[] thumbPng)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        int bytes;
        using (var fs = new FileStream(tmp, FileMode.Create, IOFileAccess.Write, FileShare.None, 1 << 16))
        {
            SaveFile.Write(fs, header, body);
            fs.Flush(true);
            bytes = (int)fs.Length;
        }
        File.Move(tmp, path, overwrite: true);
        var png = Path.ChangeExtension(path, ".png");
        if (thumbPng == null || thumbPng.Length == 0) { if (File.Exists(png)) File.Delete(png); }
        else
        {
            File.WriteAllBytes(png + ".tmp", thumbPng);
            File.Move(png + ".tmp", png, overwrite: true);
        }
        lock (Thumbs) Thumbs.Remove(png);
        return bytes;
    }

    /// <summary>Delete a save and its thumbnail.</summary>
    public static bool Delete(SaveEntry e)
    {
        try
        {
            File.Delete(e.Path);
            if (File.Exists(e.ThumbPath)) File.Delete(e.ThumbPath);
            lock (Thumbs) Thumbs.Remove(e.ThumbPath);
            return true;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"save: cannot delete {e.Path}: {x.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------ thumbnails

    static readonly Dictionary<string, (long Stamp, ImageTexture Tex)> Thumbs = new();

    /// <summary>The 320×180 thumbnail of a save, or null (cached while the file is unchanged).</summary>
    public static ImageTexture Thumb(SaveEntry e)
    {
        var p = e.ThumbPath;
        if (!File.Exists(p)) return null;
        long stamp = File.GetLastWriteTimeUtc(p).Ticks;
        lock (Thumbs) if (Thumbs.TryGetValue(p, out var c) && c.Stamp == stamp && GodotObject.IsInstanceValid(c.Tex)) return c.Tex;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(p); }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { return null; }
        var img = new Image();
        if (img.LoadPngFromBuffer(bytes) != Error.Ok || img.IsEmpty()) return null;
        var tex = ImageTexture.CreateFromImage(img);
        lock (Thumbs) Thumbs[p] = (stamp, tex);
        return tex;
    }

    /// <summary>Drop the cached thumbnail textures (the load screen closes).</summary>
    public static void ReleaseThumbs() { lock (Thumbs) Thumbs.Clear(); }
}
