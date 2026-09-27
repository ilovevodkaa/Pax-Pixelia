using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.World;

namespace PaxPixelia.UI.Front;

/// <summary>
/// The world of the next game, generated in the background as soon as the title opens. The title planet renders
/// it, and «Новая игра» previews and starts the SAME world (Session.Pending / <see cref="FrontShell.StartGame"/>).
/// Exactly one WorldData (~70 MB) is kept (MAIN_MENU.md §2.8): a new <see cref="Regenerate"/> cancels and drops
/// the previous one. Events are raised on the main thread.
/// </summary>
public static class NextWorld
{
    public const int PlanetMapW = 320, PlanetMapH = 180;   // equirectangular thumbnail, 8×8 world px per texel
    const int MaxRetries = 3;

    public static int Seed { get; private set; }
    /// <summary>The generated world, or null while generating / before the first start.</summary>
    public static WorldData World { get; private set; }
    /// <summary>Same as <see cref="World"/>.</summary>
    public static WorldData WorldData => World;
    public static bool IsReady => World != null;
    public static bool IsGenerating { get; private set; }
    /// <summary>Last progress line of the generator («Рельеф…», «Климат…»…).</summary>
    public static string Status { get; private set; } = "";
    /// <summary>Planet thumbnail of <see cref="WorldData"/> (RGBA8, wraps horizontally).</summary>
    public static ImageTexture PlanetMap { get; private set; }

    /// <summary>A (re)generation started; the previous world is gone.</summary>
    public static event Action Started;
    public static event Action<string> Progress;
    /// <summary>The world for <see cref="Seed"/> is ready.</summary>
    public static event Action Ready;

    static int _generation;
    static CancellationTokenSource _cancel;

    public static int RandomSeed() => Random.Shared.Next(10000, 100000);

    /// <summary>Start generating unless a world is ready or on its way.</summary>
    public static void EnsureStarted(int seed)
    {
        if (!IsReady && !IsGenerating) Regenerate(seed);
    }

    public static void Regenerate(int seed) => Run(seed, 0);

    /// <summary>Forget the world (handed over to a game, or the front-end closed): cancels a running generation.</summary>
    public static void Release()
    {
        ++_generation;
        _cancel?.Cancel();
        _cancel = null;
        World = null;
        PlanetMap = null;
        IsGenerating = false;
        Status = "";
    }

    static async void Run(int seed, int attempt)
    {
        int gen = ++_generation;
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        Seed = seed;
        World = null;
        PlanetMap = null;
        IsGenerating = true;
        Status = "";
        Started?.Invoke();
        // Progress<T> captures Godot's synchronisation context: reports arrive on the main thread
        IProgress<string> progress = new Progress<string>(s =>
        {
            if (gen != _generation) return;
            Status = s;
            Progress?.Invoke(s);
        });
        try
        {
            var (world, thumb) = await Task.Run(() =>
            {
                var w = WorldGen.Generate(seed, Game.WorldWidth, Game.WorldHeight, progress.Report, cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
                return (w, Thumbnail(w));
            }, cancel.Token);
            if (gen != _generation) return;
            World = world;
            PlanetMap = ImageTexture.CreateFromImage(Image.CreateFromData(PlanetMapW, PlanetMapH, false, Image.Format.Rgba8, thumb));
            IsGenerating = false;
            Ready?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (gen != _generation) return;
            GD.PushError($"NextWorld: seed {seed} failed: {e.Message}");
            IsGenerating = false;
            if (attempt < MaxRetries) Run(seed + 1, attempt + 1);   // same rule as Game.NewWorld: take the next seed
        }
    }

    /// <summary>Box-averaged BaseColor, one texel per 8×8 world pixels (smooth coasts at planet scale).</summary>
    static byte[] Thumbnail(WorldData w)
    {
        int sx = w.W / PlanetMapW, sy = w.H / PlanetMapH, n = sx * sy;
        var src = w.BaseColor;
        var dst = new byte[PlanetMapW * PlanetMapH * 4];
        for (int ty = 0; ty < PlanetMapH; ty++)
        for (int tx = 0; tx < PlanetMapW; tx++)
        {
            int r = 0, g = 0, b = 0;
            for (int y = ty * sy; y < ty * sy + sy; y++)
            {
                int i = (y * w.W + tx * sx) * 4;
                for (int x = 0; x < sx; x++, i += 4) { r += src[i]; g += src[i + 1]; b += src[i + 2]; }
            }
            int o = (ty * PlanetMapW + tx) * 4;
            dst[o] = (byte)(r / n); dst[o + 1] = (byte)(g / n); dst[o + 2] = (byte)(b / n); dst[o + 3] = 255;
        }
        return dst;
    }
}
