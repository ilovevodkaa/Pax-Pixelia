using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using PaxPixelia.Content;
using PaxPixelia.Core.Flags;
using PaxPixelia.Core.Nations;
using PaxPixelia.Sim;

namespace PaxPixelia.Core.Save;

/// <summary>What wrote a save: the rotating autosaves (by the calendar, and on leaving the game), Ctrl+S and «Сохранить».</summary>
public enum SaveKind : byte { Auto = 0, Exit = 1, Quick = 2, Manual = 3 }

public enum SaveError { None, Missing, NotASave, TooNew, TooOld, Corrupt, WorldMismatch, Io }

/// <summary>A save that cannot be read; <see cref="Exception.Message"/> is Russian text for the player.</summary>
public sealed class SaveException : Exception
{
    public readonly SaveError Code;

    public SaveException(SaveError code, string detail = null, Exception inner = null)
        : base(Text(code) + (string.IsNullOrEmpty(detail) ? "" : $" ({detail})"), inner) => Code = code;

    public static string Text(SaveError e) => e switch
    {
        SaveError.Missing => "Файл сохранения не найден",
        SaveError.NotASave => "Это не сохранение Pax Pixelia",
        SaveError.TooNew => "Сохранение сделано в более новой версии игры",
        SaveError.TooOld => "Сохранение слишком старое для этой версии",
        SaveError.Corrupt => "Файл сохранения повреждён",
        SaveError.WorldMismatch => "Мир этого сохранения в этой версии получается другим — загрузить его нельзя",
        SaveError.Io => "Не удалось прочитать или записать файл",
        _ => "Ошибка сохранения",
    };
}

/// <summary>
/// The save's metadata, read without touching the body: what the title («ПРОДОЛЖИТЬ») and the «Загрузить» list show,
/// the setup needed to regenerate the world, and the hashes that check the result.
/// </summary>
public sealed class SaveHeader
{
    public int Format = SaveFile.FormatVersion;
    public string GameVersion = "";
    public SaveKind Kind;
    /// <summary>Shown in the list: the player's own name for a manual save, else «Ардания · 880 до н. э.».</summary>
    public string Name = "";
    public long SavedUnixMs;
    /// <summary>Real time spent in this game (all sessions, pauses included).</summary>
    public long PlaytimeMs;

    // ---- the setup (the world is regenerated from Seed; the roster itself is in the state) ----
    public int Seed;
    public string SeedText = "";
    public int NationCount;
    public bool Fog = true;
    public int PacePermille = GameSetup.PaceNormal;
    public int JokePercent = 100;
    public NationDesign Player;

    // ---- what the lists show ----
    public string DateText = "";
    public int Year;
    public int Era;
    public string NationName = "";
    public byte R, G, B;
    public FlagSpec Flag;

    // ---- checks ----
    public long Tick;
    public int WorldW, WorldH, Provinces;
    public ulong WorldHash;
    /// <summary>GameState.Hash().All at save time: the loaded state must hash the same (unless the content pack changed).</summary>
    public ulong StateHash;
    public ulong ContentHash;
    public int SnapshotVersion = GameState.SnapshotVersion;

    /// <summary>The setup of the saved game (a loaded game starts paused).</summary>
    public GameSetup ToSetup() => new GameSetup(Seed, SeedText, NationCount, Fog, PacePermille, true, Player) { JokePercent = JokePercent };

    public void Write(BinaryWriter w)
    {
        w.Write(GameVersion ?? ""); w.Write((byte)Kind); w.Write(Name ?? ""); w.Write(SavedUnixMs); w.Write(PlaytimeMs);
        w.Write(Seed); w.Write(SeedText ?? ""); w.Write(NationCount); w.Write(Fog); w.Write(PacePermille); w.Write(JokePercent);
        w.Write(Player != null);
        if (Player is { } d)
        {
            w.Write(d.Id ?? ""); w.Write(d.Name ?? ""); w.Write(d.R); w.Write(d.G); w.Write(d.B);
            GameState.WriteFlag(w, d.Flag); w.Write(d.Culture); w.Write(d.LastUsedUnix);
        }
        w.Write(DateText ?? ""); w.Write(Year); w.Write(Era); w.Write(NationName ?? ""); w.Write(R); w.Write(G); w.Write(B);
        GameState.WriteFlag(w, Flag);
        w.Write(Tick); w.Write(WorldW); w.Write(WorldH); w.Write(Provinces);
        w.Write(WorldHash); w.Write(StateHash); w.Write(ContentHash); w.Write(SnapshotVersion);
    }

    public static SaveHeader Read(BinaryReader r, int format)
    {
        var h = new SaveHeader { Format = format };
        h.GameVersion = r.ReadString(); h.Kind = (SaveKind)r.ReadByte(); h.Name = r.ReadString(); h.SavedUnixMs = r.ReadInt64(); h.PlaytimeMs = r.ReadInt64();
        h.Seed = r.ReadInt32(); h.SeedText = r.ReadString(); h.NationCount = r.ReadInt32(); h.Fog = r.ReadBoolean(); h.PacePermille = r.ReadInt32(); h.JokePercent = r.ReadInt32();
        if (r.ReadBoolean())
        {
            string id = r.ReadString(), name = r.ReadString();
            byte red = r.ReadByte(), green = r.ReadByte(), blue = r.ReadByte();
            var flag = GameState.ReadFlag(r);
            byte culture = r.ReadByte();
            long used = r.ReadInt64();
            h.Player = new NationDesign(id, name, red, green, blue, flag, culture, used);
        }
        h.DateText = r.ReadString(); h.Year = r.ReadInt32(); h.Era = r.ReadInt32(); h.NationName = r.ReadString();
        h.R = r.ReadByte(); h.G = r.ReadByte(); h.B = r.ReadByte();
        h.Flag = GameState.ReadFlag(r);
        h.Tick = r.ReadInt64(); h.WorldW = r.ReadInt32(); h.WorldH = r.ReadInt32(); h.Provinces = r.ReadInt32();
        h.WorldHash = r.ReadUInt64(); h.StateHash = r.ReadUInt64(); h.ContentHash = r.ReadUInt64(); h.SnapshotVersion = r.ReadInt32();
        if (h.NationCount < 1 || h.NationCount > 255 || h.WorldW <= 0 || h.WorldH <= 0 || h.Tick < 0) throw new InvalidDataException("header values");
        return h;
    }
}

/// <summary>Camera, map mode and the map's memory of stale provinces: UI state that makes a loaded game look as it was left.</summary>
public sealed class SaveView
{
    public float CamX, CamY;
    /// <summary>MapCamera level as Game.ZoomLevel publishes it (0 = the ×½ atlas, 1..8).</summary>
    public int Zoom = 3;
    public byte Mode = 1;   // Core.MapMode.Political (the enum lives in the Godot-side Game.cs)
    public int Selected = -1;
    /// <summary>Opaque blob of Map/MapMemory (what stale provinces showed when last seen); null = rebuild from the state.</summary>
    public byte[] MapMemory;
    public bool HasCamera;

    public void Write(BinaryWriter w)
    {
        w.Write(HasCamera); w.Write(CamX); w.Write(CamY); w.Write(Zoom); w.Write(Mode); w.Write(Selected);
        w.Write(MapMemory?.Length ?? -1);
        if (MapMemory != null) w.Write(MapMemory);
    }

    public static SaveView Read(BinaryReader r)
    {
        var v = new SaveView { HasCamera = r.ReadBoolean(), CamX = r.ReadSingle(), CamY = r.ReadSingle(), Zoom = r.ReadInt32(), Mode = r.ReadByte(), Selected = r.ReadInt32() };
        int n = r.ReadInt32();
        if (n > 64 << 20) throw new InvalidDataException("map memory");
        if (n >= 0) { v.MapMemory = r.ReadBytes(n); if (v.MapMemory.Length != n) throw new EndOfStreamException(); }
        if (!float.IsFinite(v.CamX) || !float.IsFinite(v.CamY)) v.HasCamera = false;
        return v;
    }
}

/// <summary>The body: the state snapshot (parsed once the world is regenerated), the command journal and the view.</summary>
public sealed class SaveBody
{
    public byte[] State;
    public List<Cmd> Journal = new();
    /// <summary>CommandQueue's per-nation sequence counters (256).</summary>
    public ushort[] Seq = new ushort[256];
    public SaveView View = new();
}

/// <summary>
/// The binary save format (pure C#: the game and SimTests use it). Layout:
/// <code>
///   "PXSV" · u16 format · u16 reserved · i32 header length · u64 header checksum · header
///   u8 packing (1 = deflate) · i32 raw length · i32 packed length · u64 raw checksum · body
///   body = chunks: 4-byte tag · i32 length · payload   (STAT state · JRNL journal · VIEW camera/mode/map memory)
/// </code>
/// The header sits uncompressed in front so lists read it without unpacking the body. Checksums are FNV-1a 64;
/// unknown chunks are skipped (newer builds may add some). Readers accept formats MinFormat..FormatVersion.
/// </summary>
public static class SaveFile
{
    public const int FormatVersion = 1, MinFormat = 1;
    public const string Extension = ".pxs";
    static readonly byte[] Magic = { (byte)'P', (byte)'X', (byte)'S', (byte)'V' };
    const int MaxHeader = 1 << 20, MaxBody = 256 << 20;

    // ------------------------------------------------------------------ write

    public static void Write(Stream output, SaveHeader header, SaveBody body)
    {
        header.Format = FormatVersion;
        var headerBytes = ToBytes(header.Write);
        var raw = ToBytes(w =>
        {
            Chunk(w, "STAT", x => x.Write(body.State));
            Chunk(w, "JRNL", x => WriteJournal(x, body.Journal, body.Seq));
            Chunk(w, "VIEW", x => body.View.Write(x));
        });
        byte[] packed;
        using (var ms = new MemoryStream(raw.Length / 3 + 64))
        {
            using (var z = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw, 0, raw.Length);
            packed = ms.ToArray();
        }
        var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic); w.Write((ushort)FormatVersion); w.Write((ushort)0);
        w.Write(headerBytes.Length); w.Write(Checksum(headerBytes)); w.Write(headerBytes);
        w.Write((byte)1); w.Write(raw.Length); w.Write(packed.Length); w.Write(Checksum(packed)); w.Write(packed);   // over the stored bytes: a flip in deflate padding still counts
        w.Flush();
    }

    /// <summary>The state as the STAT chunk holds it.</summary>
    public static byte[] Snapshot(GameState s) => ToBytes(s.WriteSnapshot);

    /// <summary>Parse a STAT chunk onto its regenerated world (throws SaveException(Corrupt) on bad data).</summary>
    public static GameState Restore(byte[] state, World.WorldData world, ContentDb content, int jokePercent)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(state), Encoding.UTF8);
            return GameState.ReadSnapshot(r, world, content, jokePercent);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or IndexOutOfRangeException or OverflowException or FormatException)
        {
            throw new SaveException(SaveError.Corrupt, e.Message, e);
        }
    }

    // ------------------------------------------------------------------ read

    /// <summary>Only the header (fast: the list of saves and «ПРОДОЛЖИТЬ»).</summary>
    public static SaveHeader ReadHeader(Stream input) => Read(input, headerOnly: true).Header;

    public static (SaveHeader Header, SaveBody Body) Read(Stream input, bool headerOnly = false)
    {
        try
        {
            var r = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            var magic = r.ReadBytes(4);
            if (magic.Length < 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                throw new SaveException(SaveError.NotASave);
            int format = r.ReadUInt16();
            r.ReadUInt16();
            if (format > FormatVersion) throw new SaveException(SaveError.TooNew, $"формат {format}");
            if (format < MinFormat) throw new SaveException(SaveError.TooOld, $"формат {format}");
            int hl = r.ReadInt32();
            if (hl <= 0 || hl > MaxHeader) throw new SaveException(SaveError.Corrupt, "заголовок");
            ulong hsum = r.ReadUInt64();
            var hb = ReadExactly(r, hl);
            if (Checksum(hb) != hsum) throw new SaveException(SaveError.Corrupt, "заголовок");
            SaveHeader header;
            using (var hr = new BinaryReader(new MemoryStream(hb), Encoding.UTF8)) header = SaveHeader.Read(hr, format);
            if (header.SnapshotVersion > GameState.SnapshotVersion) throw new SaveException(SaveError.TooNew, $"состояние {header.SnapshotVersion}");
            if (headerOnly) return (header, null);

            byte packing = r.ReadByte();
            int rawLen = r.ReadInt32(), packedLen = r.ReadInt32();
            ulong sum = r.ReadUInt64();
            if (packing > 1 || rawLen < 0 || rawLen > MaxBody || packedLen < 0 || packedLen > MaxBody) throw new SaveException(SaveError.Corrupt, "тело");
            var packed = ReadExactly(r, packedLen);
            if (Checksum(packed) != sum) throw new SaveException(SaveError.Corrupt, "контрольная сумма");
            byte[] raw;
            if (packing == 0) raw = packed;
            else
            {
                raw = new byte[rawLen];
                using var z = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress);
                int got = 0;
                while (got < rawLen)
                {
                    int k = z.Read(raw, got, rawLen - got);
                    if (k <= 0) break;
                    got += k;
                }
                if (got != rawLen) throw new SaveException(SaveError.Corrupt, "тело обрезано");
            }
            if (raw.Length != rawLen) throw new SaveException(SaveError.Corrupt, "тело");
            return (header, ReadBody(raw));
        }
        catch (SaveException) { throw; }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException or ArgumentException or OverflowException or DecoderFallbackException or FormatException)
        {
            throw new SaveException(e is EndOfStreamException ? SaveError.Corrupt : e is IOException ? SaveError.Io : SaveError.Corrupt, e.Message, e);
        }
    }

    static SaveBody ReadBody(byte[] raw)
    {
        var body = new SaveBody();
        using var r = new BinaryReader(new MemoryStream(raw), Encoding.UTF8);
        while (r.BaseStream.Position < raw.Length)
        {
            var tag = Encoding.ASCII.GetString(ReadExactly(r, 4));
            int len = r.ReadInt32();
            if (len < 0 || r.BaseStream.Position + len > raw.Length) throw new InvalidDataException("chunk " + tag);
            var payload = ReadExactly(r, len);
            using var cr = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
            switch (tag)
            {
                case "STAT": body.State = payload; break;
                case "JRNL": ReadJournal(cr, body); break;
                case "VIEW": body.View = SaveView.Read(cr); break;
                default: break;   // a newer build's chunk: skip
            }
        }
        if (body.State == null) throw new InvalidDataException("no state");
        return body;
    }

    // ------------------------------------------------------------------ journal

    static void WriteJournal(BinaryWriter w, IReadOnlyList<Cmd> journal, ushort[] seq)
    {
        w.Write(seq.Length);
        foreach (var s in seq) w.Write(s);
        w.Write(journal.Count);
        foreach (var c in journal)
        {
            w.Write(c.Tick); w.Write(c.Nation); w.Write(c.Seq); w.Write((byte)c.Type); w.Write(c.A); w.Write(c.B); w.Write(c.C);
        }
    }

    static void ReadJournal(BinaryReader r, SaveBody body)
    {
        int n = r.ReadInt32();
        if (n < 0 || n > 4096) throw new InvalidDataException("sequence counters");
        body.Seq = new ushort[Math.Max(256, n)];
        for (int i = 0; i < n; i++) body.Seq[i] = r.ReadUInt16();
        int count = r.ReadInt32();
        if (count < 0 || count > 50_000_000) throw new InvalidDataException("journal");
        body.Journal = new List<Cmd>(count);
        for (int i = 0; i < count; i++)
            body.Journal.Add(new Cmd(r.ReadInt32(), r.ReadByte(), r.ReadUInt16(), (CmdType)r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Signature of the content pack (event, flag and thread ids in order): a changed pack remaps the deck.</summary>
    public static ulong ContentSignature(ContentDb db)
    {
        if (db == null) return 0;
        var h = new Fnv64();
        foreach (var e in db.Events) h.Add(e.Def?.Id);
        h.Add("|");
        foreach (var f in db.Flags) h.Add(f);
        h.Add("|");
        foreach (var t in db.Threads) h.Add(t);
        return h.Value;
    }

    static void Chunk(BinaryWriter w, string tag, Action<BinaryWriter> write)
    {
        var payload = ToBytes(write);
        w.Write(Encoding.ASCII.GetBytes(tag));
        w.Write(payload.Length);
        w.Write(payload);
    }

    static byte[] ToBytes(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(w);
        return ms.ToArray();
    }

    static byte[] ReadExactly(BinaryReader r, int n)
    {
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException();
        return b;
    }

    public static ulong Checksum(ReadOnlySpan<byte> data)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in data) h = (h ^ b) * 1099511628211UL;
        return h;
    }

    /// <summary>FNV-1a 64 over strings and numbers (content signature, world hash).</summary>
    public struct Fnv64
    {
        ulong _h;
        public Fnv64() { _h = 14695981039346656037UL; }
        public void Add(byte b) => _h = (_h ^ b) * 1099511628211UL;
        public void Add(int v) { Add((byte)v); Add((byte)(v >> 8)); Add((byte)(v >> 16)); Add((byte)(v >> 24)); }
        public void Add(string s)
        {
            if (s == null) { Add(-1); return; }
            Add(s.Length);
            foreach (char c in s) { Add((byte)c); Add((byte)(c >> 8)); }
        }
        public readonly ulong Value => _h;
    }
}
