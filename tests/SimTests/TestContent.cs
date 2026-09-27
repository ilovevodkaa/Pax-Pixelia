using System;
using System.IO;
using PaxPixelia.Content;

namespace PaxPixelia.Tests;

/// <summary>The game's content pack (game/data), read once from disk as ContentLoader does in the game.</summary>
public static class TestContent
{
    static readonly Lazy<ContentDb> Pack = new(() => ContentDb.LoadDirectory(Path.Combine(LintTests.GameDir(), "data")));
    public static ContentDb Db => Pack.Value;
}
