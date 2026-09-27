using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// Loads the front-end's generated data textures (skyline atlas, icons). Falls back to reading the PNG from disk
/// when it has not been imported yet (a fresh checkout run without the editor), so the menu still draws.
/// </summary>
public static class FrontAssets
{
    public static Texture2D LoadTexture(string path)
    {
        if (IsImported(path) && GD.Load<Texture2D>(path) is { } tex) return tex;
        var img = FromDisk(path);
        return img == null ? null : ImageTexture.CreateFromImage(img);
    }

    public static Image LoadImage(string path) =>
        IsImported(path) && GD.Load<Texture2D>(path)?.GetImage() is { } img ? img : FromDisk(path);

    /// <summary>The .import file exists AND its imported target is on disk (only the editor writes it).</summary>
    static bool IsImported(string path)
    {
        if (!ResourceLoader.Exists(path)) return false;
        var cfg = new ConfigFile();
        if (cfg.Load(path + ".import") != Error.Ok) return true;
        var target = cfg.GetValue("remap", "path", "").AsString();
        return target.Length == 0 || FileAccess.FileExists(target);
    }

    static Image FromDisk(string path)
    {
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
        if (img == null) GD.PushWarning($"FrontAssets: {path} not found");
        return img;
    }
}
