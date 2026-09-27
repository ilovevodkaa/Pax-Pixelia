using System;
using System.Collections.Generic;
using System.Linq;

namespace PaxPixelia.Content;

/// <summary>
/// Validation of a loaded pack: ids, references, texts, weights, the tone rules of CONTENT.md and the thread
/// chains. Errors must be zero for the pack to ship (ContentTests); warnings are for the writer.
/// </summary>
public static class ContentCheck
{
    public const int MaxLine = 180;           // tone rule 4
    public const int MaxTitle = 40;

    public sealed class Report
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public bool Ok => Errors.Count == 0;
    }

    public static Report Run(ContentDb db)
    {
        var rep = new Report();
        rep.Errors.AddRange(db.Issues);
        Deck(db.Deck, rep);
        var read = new HashSet<int>();
        var followed = new HashSet<int>();
        foreach (var e in db.Events) Event(db, e, rep, read, followed);
        Threads(db, rep);
        foreach (var e in db.Events)
            if (e.FollowUpOnly && !followed.Contains(e.Index)) rep.Warnings.Add($"{Where(e)}: follow-up-only event nobody schedules");
        for (int f = db.SystemFlagCount; f < db.Flags.Count; f++)
            if (!read.Contains(f)) rep.Warnings.Add($"flag «{db.Flags[f]}» is set but never read (memory for later content?)");
        Tables(db, rep);
        return rep;
    }

    static string Where(EventRt e) => $"{e.Def.File}: {e.Def.Id}";

    static void Deck(DeckDef d, Report rep)
    {
        void Range(int[] r, string name)
        {
            if (r is not { Length: 2 } || r[0] <= 0 || r[0] > r[1]) rep.Errors.Add($"core/deck.json: {name} must be [min, max] with 0 < min ≤ max");
        }
        Range(d.Gap, "gap");
        Range(d.ChoiceGap, "choiceGap");
        Range(d.FirstEvent, "firstEvent");
        if (d.Retry <= 0 || d.ChoiceDeadline <= 0 || d.ChronicleCooldown <= 0 || d.ChoiceCooldown <= 0)
            rep.Errors.Add("core/deck.json: retry, choiceDeadline and cooldowns must be > 0");
        if (d.AiDefaults.Any(a => a <= 0)) rep.Errors.Add("core/deck.json: aiDefaults must be > 0");
    }

    static void Event(ContentDb db, EventRt e, Report rep, HashSet<int> read, HashSet<int> followed)
    {
        var d = e.Def;
        string w = Where(e);
        void Err(string m) => rep.Errors.Add($"{w}: {m}");

        if (d.Id == null || !d.Id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') || !char.IsLetter(d.Id[0]))
            Err("id must be lower_snake_case latin");
        if (e.Weight <= 0) Err("weight must be > 0");
        if (d.Cooldown < 0) Err("cooldown must be ≥ 0");
        if (e.EraMin < 0 || e.EraMax > 10 || e.EraMin > e.EraMax) Err($"eras [{e.EraMin}, {e.EraMax}] outside 0..10 or reversed");
        if (!db.Deck.Icons.Contains(d.Icon)) Err($"icon «{d.Icon}» is not in core/deck.json icons");
        if (d.Importance is < 0 or > 3) Err("importance must be 0..3");

        Text(d.Title, "title", MaxTitle);
        Text(d.Text, "text", MaxLine);
        if (d.Lore != null) Text(d.Lore, "lore", MaxLine);
        if (d.Result != null) Text(d.Result, "result", MaxLine);

        if (e.Tone == Tone.Joke)
        {
            if (!d.Once && !d.FollowUpOnly) Err("a joke fires once per game (tone rule 5): set once");
            if (string.IsNullOrWhiteSpace(d.Lore) && !d.FollowUpOnly) Err("a joke needs «lore» — the chronicler's note: year, place, fact (tone rule 5)");
        }
        else if (!d.Localizable) rep.Warnings.Add($"{w}: localizable=false on a serious event");

        switch (d.Kind)
        {
            case EventKind.Chronicle when e.IsChoice: Err("a chronicle event has no options (use kind choice)"); break;
            case EventKind.Choice when e.Options.Length is < 2 or > 3: Err("a choice event has 2–3 options"); break;
        }
        if (d.Trigger && !d.Once && !e.When.Flags.Any(f => Clears(e, f)))
            Err("a trigger must be once or clear a flag it requires, or it repeats every cooldown");
        if (e.Effects.FollowUp == e.Index || e.Options.Length > 0 && e.Options[0].Effects.FollowUp == e.Index)
            Err("an endless follow-up loop: make it a choice whose option 1 ends it");
        if (!e.IsChoice && d.Result == null && !e.Effects.Any) rep.Warnings.Add($"{w}: chronicle event without effects (tone rule 1: every joke has a mechanic)");

        Cond(db, e.When, read);
        if (e.BoostFlag != null) foreach (int f in e.BoostFlag) read.Add(f);
        Effects(e.Effects, followed, read);
        for (int k = 0; k < e.Options.Length; k++)
        {
            var o = d.Options[k];
            var ort = e.Options[k];
            string ow = $"option {k + 1}";
            Text(o.Text, ow + " text", MaxLine);
            if (o.Result != null) Text(o.Result, ow + " result", MaxLine);
            if (ort.Ai <= 0) Err($"{ow}: ai weight must be > 0");
            if (k == 0 && o.When != null) Err("option 1 is the safe default (advisor, deadline) and must always be offered: no «when»");
            Cond(db, ort.When, read);
            Effects(ort.Effects, followed, read);
            if (o.Roll == null) continue;
            if (o.Roll.Count < 2) Err($"{ow}: a roll needs 2+ branches");
            if (o.Roll.Any(r => r.Permille <= 0) || o.Roll.Sum(r => r.Permille) != 1000) Err($"{ow}: roll permilles must be > 0 and sum to 1000");
            for (int r = 0; r < o.Roll.Count; r++)
            {
                Text(o.Roll[r].Result, $"{ow} roll {r + 1} result", MaxLine);
                Effects(ort.RollEffects[r], followed, read);
            }
        }

        void Text(string s, string what, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) { Err($"{what} is empty"); return; }
            if (s.Length > max) Err($"{what} is {s.Length} chars (max {max}): «{s[..Math.Min(40, s.Length)]}…»");
            if (s != s.Trim() || s.Contains("  ")) Err($"{what} has stray spaces");
            foreach (var bad in db.Deck.Denylist)
                if (s.Contains(bad, StringComparison.OrdinalIgnoreCase)) Err($"{what} contains «{bad}» (tone rule 3)");
            foreach (var (name, form) in EventText.Placeholders(s))
            {
                if (Array.IndexOf(EventText.Names, name) < 0) Err($"{what}: unknown placeholder {{{name}}}");
                else if (form != null && Array.IndexOf(EventText.Forms, form) < 0) Err($"{what}: unknown form {{{name}:{form}}}");
                else if (name == "province" && e.Subject is not (Subject.Province or Subject.Capital)) Err($"{what}: {{province}} needs subject province or capital");
                else if (name == "foreign" && e.Subject != Subject.Foreign) Err($"{what}: {{foreign}} needs subject foreign");
            }
        }
    }

    static bool Clears(EventRt e, int flag) =>
        e.Effects.ClearFlags.Contains(flag) || e.Options.Any(o => o.Effects.ClearFlags.Contains(flag));

    static void Cond(ContentDb db, CondRt c, HashSet<int> read)
    {
        foreach (int f in c.Flags) read.Add(f);
        foreach (int f in c.NotFlags) read.Add(f);
        foreach (int f in c.AnyFlags) read.Add(f);
    }

    static void Effects(EffectsRt fx, HashSet<int> followed, HashSet<int> read)
    {
        if (fx.FollowUp >= 0) followed.Add(fx.FollowUp);
        foreach (int f in fx.ClearFlags) read.Add(f);
    }

    /// <summary>Stages of a thread are 1..N and each stage after the first needs a flag the previous stage sets.</summary>
    static void Threads(ContentDb db, Report rep)
    {
        for (int t = 0; t < db.Threads.Length; t++)
        {
            var stages = db.Events.Where(e => e.Thread == t).OrderBy(e => e.Def.Stage).ToList();
            string name = db.Threads[t];
            for (int k = 0; k < stages.Count; k++)
            {
                var e = stages[k];
                if (e.Def.Stage != k + 1) { rep.Errors.Add($"{Where(e)}: thread «{name}» stages must be 1..{stages.Count} without gaps"); break; }
                if (e.Tone != Tone.Joke) rep.Errors.Add($"{Where(e)}: thread stages are jokes");
                if (k == 0) continue;
                var prev = stages[k - 1];
                var prevSets = new HashSet<int>(prev.Effects.SetFlags);
                foreach (var o in prev.Options) { prevSets.UnionWith(o.Effects.SetFlags); if (o.RollEffects != null) foreach (var r in o.RollEffects) prevSets.UnionWith(r.SetFlags); }
                if (!e.When.Flags.Any(prevSets.Contains)) rep.Errors.Add($"{Where(e)}: stage {k + 1} of «{name}» must require a flag set by stage {k}");
                if (e.EraMin <= prev.EraMin) rep.Errors.Add($"{Where(e)}: stage {k + 1} of «{name}» must start in a later era than stage {k}");
            }
        }
    }

    static void Tables(ContentDb db, Report rep)
    {
        var groups = new[] { "character", "upbringing", "acquired" };
        Unique(db.Traits.Select(t => t.Id), "core/leader_traits.json", rep);
        foreach (var t in db.Traits)
        {
            if (string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Desc)) rep.Errors.Add($"core/leader_traits.json: {t.Id}: name and desc required");
            if (!groups.Contains(t.Group)) rep.Errors.Add($"core/leader_traits.json: {t.Id}: group must be {string.Join(" | ", groups)}");
        }

        var slots = new[] { "teaching", "rite", "founder", "preaching" };
        Unique(db.Dogmas.Select(d => d.Id), "core/dogmas.json", rep);
        foreach (var d in db.Dogmas)
        {
            if (string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.Desc)) rep.Errors.Add($"core/dogmas.json: {d.Id}: name and desc required");
            if (!slots.Contains(d.Slot)) rep.Errors.Add($"core/dogmas.json: {d.Id}: slot must be {string.Join(" | ", slots)}");
            if (d.Excludes != null)
                foreach (var x in d.Excludes)
                    if (!db.Dogmas.Any(o => o.Id == x)) rep.Errors.Add($"core/dogmas.json: {d.Id}: excludes unknown dogma «{x}»");
        }
        int mvp = db.Dogmas.Count(d => d.Mvp);
        if (mvp != 12) rep.Errors.Add($"core/dogmas.json: the MVP set has 12 dogmas (★), found {mvp}");

        var wonders = db.Wonders.Lines.SelectMany(l => l.Tiers).Concat(db.Wonders.Singles).Concat(db.Wonders.Parodies).ToList();
        Unique(wonders.Select(x => x.Id).Concat(db.Wonders.Lines.Select(l => l.Id)), "core/wonders.json", rep);
        foreach (var x in wonders)
            if (string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Desc) || x.Era is < 0 or > 10)
                rep.Errors.Add($"core/wonders.json: {x.Id}: name, desc and era 0..10 required");
        foreach (var l in db.Wonders.Lines)
            for (int k = 1; k < l.Tiers.Count; k++)
                if (l.Tiers[k].Era <= l.Tiers[k - 1].Era) rep.Errors.Add($"core/wonders.json: {l.Id}: tier {k + 1} must come in a later era");
    }

    static void Unique(IEnumerable<string> ids, string file, Report rep)
    {
        var seen = new HashSet<string>();
        foreach (var id in ids)
            if (string.IsNullOrEmpty(id)) rep.Errors.Add($"{file}: an entry has no id");
            else if (!seen.Add(id)) rep.Errors.Add($"{file}: duplicate id «{id}»");
    }
}
