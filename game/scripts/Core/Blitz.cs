using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PaxPixelia.Content;
using PaxPixelia.Core.Flags;
using PaxPixelia.Core.Nations;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Core;

/// <summary>A blitz score by its parts (the result card and the table show them).</summary>
public readonly record struct BlitzScore(int People, int Lands, int Eras, int Knowledge, int Cities, int Contacts)
{
    public int Total => People + Lands + Eras + Knowledge + Cities + Contacts;
}

/// <summary>A finished blitz as a file (.pxb): the week, the setup, the player's journal, the score and the final hash —
/// everything another player needs to replay the game and see the score is honest.</summary>
public sealed class BlitzRecord
{
    public string Week = "";
    public string GameVersion = "";
    public GameSetup Setup;
    public ulong ContentHash;
    public long EndTick;
    public BlitzScore Score;
    public ulong StateHash;
    public string NationName = "";
    public byte R, G, B;
    public FlagSpec Flag;
    public long SavedUnixMs;
    public List<Cmd> Journal = new();
}

/// <summary>
/// «Блиц недели» (pure C#): every week of the year has one world — the same seed, rules and length for everybody. A
/// blitz is a fixed span of game time (<see cref="Ticks"/>: 40 minutes at speed 3, 8 at speed 5), then the game stops and
/// is scored. The deterministic sim makes the table honest for free: the result file carries the player's journal, and
/// <see cref="Check"/> replays it from the very first state (<see cref="GameStart.Create"/>) to the end and compares the
/// hash and the score. Friends swap files (a few KB) — no server.
/// </summary>
public static class Blitz
{
    public const int Nations = 8;
    /// <summary>Game time of a blitz: 40 min at speed 3.</summary>
    public const long Ticks = 40L * 60 * 8;
    public const string Extension = ".pxb";
    const int FileVersion = 1;
    static readonly byte[] Magic = { (byte)'P', (byte)'X', (byte)'B', (byte)'Z' };

    // ------------------------------------------------------------------ the week

    /// <summary>«2026-40»: the ISO year and week of a UTC moment (a week starts on Monday everywhere).</summary>
    public static string WeekId(DateTime utc) => $"{ISOWeek.GetYear(utc)}-{ISOWeek.GetWeekOfYear(utc):00}";

    /// <summary>«Неделя 40 · 2026» for the screens.</summary>
    public static string WeekTitle(string week) => week.Length == 7 ? $"Неделя {week[5..].TrimStart('0')} · {week[..4]}" : week;

    public static string SeedTextOf(string week) => "блиц " + week;

    /// <summary>The setup of a week's blitz: its seed, 8 nations, fog, the quick pace, a nomad start, the player's nation.</summary>
    public static GameSetup WeekSetup(string week, NationDesign player)
    {
        var text = SeedTextOf(week);
        return new GameSetup(SeedText.Parse(text), text, Nations, true, GameSetup.PaceQuick, true, player) { Nomad = true, BlitzTicks = Ticks };
    }

    // ------------------------------------------------------------------ the score

    /// <summary>People per thousand, 10 per province, 150 per era, 10 per technology past the fire, 30 per city, 15 per
    /// nation met. Gold does not count (it piles up), nor does anything a replay could not see.</summary>
    public static BlitzScore Score(GameState s, int n)
    {
        long people = 0; int lands = 0, cities = 0;
        for (int p = 0; p < s.Owner.Length; p++)
        {
            if (s.Owner[p] != n) continue;
            people += s.Pop[p]; lands++;
            if (Cities.IsCity(s, p)) cities++;
        }
        var nat = s.Nat[n];
        int met = 0;
        if (nat.Fog != null) for (int m = 0; m < nat.Fog.Met.Length; m++) if (m != n && nat.Fog.Met[m]) met++;
        return new BlitzScore((int)(people / 1000), lands * 10, nat.Era * 150, Math.Max(0, Techs.KnownCount(nat) - 1) * 10, cities * 30, met * 15);
    }

    // ------------------------------------------------------------------ the record

    /// <summary>The record of a finished blitz of nation 0 (the player).</summary>
    public static BlitzRecord Record(string week, string gameVersion, GameSetup setup, GameState s, IReadOnlyList<Cmd> journal, long unixMs)
    {
        var me = s.Nations[GameState.LocalPlayer];
        return new BlitzRecord
        {
            Week = week, GameVersion = gameVersion ?? "", Setup = setup, ContentHash = SaveFile.ContentSignature(s.Events?.Db),
            EndTick = s.Tick, Score = Score(s, GameState.LocalPlayer), StateHash = s.Hash().All,
            NationName = me.Name ?? "", R = me.R, G = me.G, B = me.B, Flag = me.Flag, SavedUnixMs = unixMs,
            Journal = new List<Cmd>(journal),
        };
    }

    /// <summary>«2026-40_Ардания_1234_9f3a2c11.pxb»: the same game always gets the same name (a rewrite replaces it).</summary>
    public static string FileName(BlitzRecord r)
    {
        var sb = new StringBuilder();
        foreach (char c in r.NationName) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        if (sb.Length == 0) sb.Append("народ");
        return $"{r.Week}_{sb}_{r.Score.Total}_{(uint)r.StateHash:x8}{Extension}";
    }

    public static void Write(Stream output, BlitzRecord r)
    {
        byte[] payload;
        using (var ms = new MemoryStream())
        {
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(r.Week ?? ""); w.Write(r.GameVersion ?? "");
                var s = r.Setup;
                w.Write(s.Seed); w.Write(s.SeedText ?? ""); w.Write(s.NationCount); w.Write(s.Fog); w.Write(s.PacePermille); w.Write(s.StartPaused);
                w.Write(s.JokePercent); w.Write(s.Nomad); w.Write(s.BlitzTicks);
                w.Write(s.Player != null);
                if (s.Player is { } d)
                {
                    w.Write(d.Id ?? ""); w.Write(d.Name ?? ""); w.Write(d.R); w.Write(d.G); w.Write(d.B);
                    GameState.WriteFlag(w, d.Flag); w.Write(d.Culture); w.Write(d.LastUsedUnix);
                }
                w.Write(r.ContentHash); w.Write(r.EndTick);
                var sc = r.Score;
                w.Write(sc.People); w.Write(sc.Lands); w.Write(sc.Eras); w.Write(sc.Knowledge); w.Write(sc.Cities); w.Write(sc.Contacts);
                w.Write(r.StateHash);
                w.Write(r.NationName ?? ""); w.Write(r.R); w.Write(r.G); w.Write(r.B); GameState.WriteFlag(w, r.Flag);
                w.Write(r.SavedUnixMs);
                w.Write(r.Journal.Count);
                foreach (var c in r.Journal) { w.Write(c.Tick); w.Write(c.Nation); w.Write(c.Seq); w.Write((byte)c.Type); w.Write(c.A); w.Write(c.B); w.Write(c.C); }
            }
            payload = ms.ToArray();
        }
        var o = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        o.Write(Magic); o.Write((ushort)FileVersion); o.Write(payload.Length); o.Write(SaveFile.Checksum(payload)); o.Write(payload);
        o.Flush();
    }

    /// <summary>Read a result file; throws InvalidDataException (or EndOfStreamException) on anything else.</summary>
    public static BlitzRecord Read(Stream input)
    {
        var i = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var magic = i.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("not a blitz result");
        int version = i.ReadUInt16();
        if (version != FileVersion) throw new InvalidDataException($"blitz file version {version}");
        int len = i.ReadInt32();
        ulong sum = i.ReadUInt64();
        if (len <= 0 || len > 64 << 20) throw new InvalidDataException("blitz length");
        var payload = i.ReadBytes(len);
        if (payload.Length != len || SaveFile.Checksum(payload) != sum) throw new InvalidDataException("blitz checksum");
        using var r = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
        var x = new BlitzRecord { Week = r.ReadString(), GameVersion = r.ReadString() };
        int seed = r.ReadInt32(); string seedText = r.ReadString(); int nations = r.ReadInt32(); bool fog = r.ReadBoolean();
        int pace = r.ReadInt32(); bool paused = r.ReadBoolean(); int joke = r.ReadInt32(); bool nomad = r.ReadBoolean(); long ticks = r.ReadInt64();
        NationDesign player = null;
        if (r.ReadBoolean())
        {
            string id = r.ReadString(), name = r.ReadString();
            byte red = r.ReadByte(), green = r.ReadByte(), blue = r.ReadByte();
            var flag = GameState.ReadFlag(r);
            byte culture = r.ReadByte();
            long used = r.ReadInt64();
            player = new NationDesign(id, name, red, green, blue, flag, culture, used);
        }
        if (nations < 2 || nations > 16 || ticks <= 0) throw new InvalidDataException("blitz setup");
        x.Setup = new GameSetup(seed, seedText, nations, fog, pace, paused, player) { JokePercent = joke, Nomad = nomad, BlitzTicks = ticks };
        x.ContentHash = r.ReadUInt64(); x.EndTick = r.ReadInt64();
        x.Score = new BlitzScore(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        x.StateHash = r.ReadUInt64();
        x.NationName = r.ReadString(); x.R = r.ReadByte(); x.G = r.ReadByte(); x.B = r.ReadByte(); x.Flag = GameState.ReadFlag(r);
        x.SavedUnixMs = r.ReadInt64();
        int n = r.ReadInt32();
        if (n < 0 || n > 1 << 22) throw new InvalidDataException("blitz journal");
        for (int k = 0; k < n; k++)
            x.Journal.Add(new Cmd(r.ReadInt32(), r.ReadByte(), r.ReadUInt16(), (CmdType)r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
        return x;
    }

    // ------------------------------------------------------------------ the check

    /// <summary>
    /// Replay a result and say whether it is honest: null when the replayed game ends with the same hash and score, else
    /// the reason (Russian). The week's own rules, only the player's own commands and no debug commands are accepted.
    /// <paramref name="world"/> may be the week's world when the caller has it (it is regenerated otherwise).
    /// </summary>
    public static string Check(BlitzRecord r, ContentDb content, WorldData world = null, CancellationToken cancel = default)
    {
        var s = r.Setup;
        if (s == null || !s.IsBlitz) return "Это не блиц";
        if (r.EndTick != s.BlitzTicks) return "Партия не доиграна";
        // the start on pause is a matter of the menu (the clock waits for the player), not of the rules
        if (r.Week.Length > 0 && WeekSetup(r.Week, s.Player) with { StartPaused = s.StartPaused } != s) return "Правила не совпадают с правилами недели";
        if (r.ContentHash != SaveFile.ContentSignature(content)) return "Сыграно в другой версии событий";
        long last = 0;
        foreach (var c in r.Journal)
        {
            if (c.Nation != GameState.LocalPlayer) return "В журнале чужие приказы";
            if (c.Type is CmdType.CheatGold or CmdType.CheatEra or CmdType.CheatTech) return "В журнале отладочные команды";
            if (c.Tick < last || c.Tick > r.EndTick) return "Журнал перепутан";
            last = c.Tick;
        }
        var w = world != null && world.Seed == s.Seed && world.W == GameStart.WorldWidth ? world
            : WorldGen.Generate(s.Seed, GameStart.WorldWidth, GameStart.WorldHeight, null, cancel);
        var st = GameStart.Create(w, s, content);
        Replay.Run(w, st, r.Journal, r.EndTick, null);
        if (st.Hash().All != r.StateHash) return "Повтор партии разошёлся с результатом (другая версия игры?)";
        if (Score(st, GameState.LocalPlayer) != r.Score) return "Очки не сходятся";
        return null;
    }
}
