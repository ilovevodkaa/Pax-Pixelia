using System;
using System.Collections.Generic;
using System.Text;

namespace PaxPixelia.Content;

/// <summary>
/// Texts of an EventRecord for the UI and the chronicle. Templates carry placeholders {name} or {name:form}:
/// province, capital, foreign, ruler, nation, year; forms: gen (родительный), adj (прилагательное).
/// The caller resolves them (it knows the roster and Ru grammar); the era voice is applied on top by the UI.
/// </summary>
public static class EventText
{
    public static readonly string[] Names = { "province", "capital", "foreign", "ruler", "nation", "year" };
    public static readonly string[] Forms = { "gen", "adj" };

    public static EventDef Def(ContentDb db, in EventRecord r) => db.Events[r.Event].Def;

    /// <summary>The chronicle line of a resolved record: the roll branch's, else the option's, else the event's own.</summary>
    public static string Result(ContentDb db, in EventRecord r)
    {
        var d = Def(db, r);
        if (r.Option < 0 || d.Options == null) return d.Result ?? d.Text;
        var o = d.Options[r.Option];
        if (r.Branch >= 0 && o.Roll?[r.Branch].Result is { } b) return b;
        return o.Result ?? o.Text;
    }

    /// <summary>Replace placeholders through resolve(name, form); form is null when absent.</summary>
    public static string Format(string template, Func<string, string, string> resolve)
    {
        if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template;
        var sb = new StringBuilder(template.Length + 16);
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            int close = open < 0 ? -1 : template.IndexOf('}', open);
            if (close < 0) { sb.Append(template, i, template.Length - i); break; }
            sb.Append(template, i, open - i);
            var (name, form) = Split(template.AsSpan(open + 1, close - open - 1));
            sb.Append(resolve(name, form));
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>Placeholders of a template in order (validation).</summary>
    public static List<(string Name, string Form)> Placeholders(string template)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(template)) return list;
        int i = 0;
        while (true)
        {
            int open = template.IndexOf('{', i);
            if (open < 0) return list;
            int close = template.IndexOf('}', open);
            if (close < 0) { list.Add((template[open..], null)); return list; }
            list.Add(Split(template.AsSpan(open + 1, close - open - 1)));
            i = close + 1;
        }
    }

    static (string, string) Split(ReadOnlySpan<char> token)
    {
        int colon = token.IndexOf(':');
        return colon < 0 ? (token.ToString(), null) : (token[..colon].ToString(), token[(colon + 1)..].ToString());
    }
}
