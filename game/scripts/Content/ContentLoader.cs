using System.Collections.Generic;
using Godot;

namespace PaxPixelia.Content;

/// <summary>The only Godot-aware file of Content: reads res://data/**/*.json (editor and exported pck alike) into ContentDb.</summary>
public static class ContentLoader
{
    public const string Root = "res://data";

    public static ContentDb Load(string root = Root)
    {
        var sources = new List<ContentSource>();
        Collect(root, "", sources);
        var db = ContentDb.Load(sources);
        foreach (var issue in db.Issues) GD.PushWarning("content: " + issue);
        return db;
    }

    static void Collect(string root, string rel, List<ContentSource> into)
    {
        using var dir = DirAccess.Open(rel.Length == 0 ? root : $"{root}/{rel}");
        if (dir == null) { GD.PushError($"content: cannot open {root}/{rel}"); return; }
        foreach (var sub in dir.GetDirectories())
            Collect(root, rel.Length == 0 ? sub : $"{rel}/{sub}", into);
        foreach (var file in dir.GetFiles())
        {
            if (!file.EndsWith(".json")) continue;
            string path = rel.Length == 0 ? file : $"{rel}/{file}";
            into.Add(new ContentSource(path, FileAccess.GetFileAsString($"{root}/{path}")));
        }
    }
}
