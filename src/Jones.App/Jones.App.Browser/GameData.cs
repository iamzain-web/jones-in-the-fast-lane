using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Jones.App.Browser;

/// <summary>
/// THE GAME'S TEXT, SUBTITLES AND MUSIC, FETCHED AND PUT WHERE THE EXISTING LOADERS LOOK.
///
/// <para>
/// WHAT WAS WRONG. The desktop head calls <c>SciText.Load</c>, <c>Subtitles.Load</c> and
/// <c>LipSync.SetDirectory</c> before it opens a window; the Android head does the same.
/// This head called NONE of them, because every one takes a PATH and reads it with
/// <c>System.IO.File</c>, and a browser has no disk to point them at. So the web build
/// shipped with its art and nothing else, and the holes were exactly where you would
/// expect if you knew: the Statistics page drew its border and its DONE button around an
/// empty white sheet, no speech balloon ever appeared, and the game was silent.
/// </para>
///
/// <para>
/// THE TRICK, AND IT IS THE SAME FACT THAT BREAKS SAVED GAMES. WebAssembly DOES have a
/// filesystem — Emscripten's, held in memory — and <c>System.IO</c> works against it
/// perfectly well. It is useless for SAVES because it dies with the tab; it is ideal for
/// RESOURCES, which are the same every time and are re-fetched on the next load anyway.
/// So this downloads the files into it and then calls the ordinary loaders with an
/// ordinary path. Not one line of Jones.Core or Jones.App changes, and the three heads
/// keep loading their resources the same way.
/// </para>
///
/// <para>
/// NOTHING HERE IS AWAITED BY STARTUP, for the reason written at length in Program: this
/// head has one thread and the browser's event loop is on it. The game opens on its 1x art
/// with no text, exactly as it does today, and fills in when the bytes land — which is the
/// same bargain <see cref="HttpArtStore"/> already makes for cels.
/// </para>
/// </summary>
internal static class GameData
{
    /// <summary>
    /// Where the files are put, and therefore the `assetRoot` the loaders are given. An
    /// absolute path in Emscripten's filesystem; nothing outside this process can see it.
    /// </summary>
    public const string Root = "/jones";

    /// <summary>The manifest the csproj writes beside the files: one relative path a line.</summary>
    private const string ManifestPath = "res/data-manifest.txt";

    /// <summary>What happened, for the startup line. Null until it has finished.</summary>
    public static string? Report { get; private set; }

    /// <summary>
    /// Fetches everything the manifest lists, writes it into the in-memory filesystem, and
    /// points the loaders at it.
    ///
    /// <para>
    /// SURVIVABLE AT EVERY STEP, like the art. No manifest, a failed request, a file that
    /// will not parse — each one leaves the game running with less in it rather than not
    /// running. A game with no subtitles is a game; a page that will not open is not.
    /// </para>
    /// </summary>
    public static async Task LoadAsync(HttpClient http, Action onLoaded)
    {
        try
        {
            var manifest = await http.GetStringAsync(ManifestPath);
            var names = manifest
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().Replace('\\', '/'))
                .Where(l => l.Length > 0)
                .ToArray();

            if (names.Length == 0)
            {
                Report = "data manifest was empty";
                return;
            }

            // EIGHT AT A TIME. Seventy-odd files of a couple of kilobytes each: one at a
            // time wastes the round trips, all at once queues seventy requests ahead of the
            // CELS the player is waiting to see. The art and the text compete for the same
            // connection, and the art is what is on screen.
            var failed = 0;
            foreach (var batch in names.Chunk(8))
            {
                await Task.WhenAll(batch.Select(async name =>
                {
                    try
                    {
                        var bytes = await http.GetByteArrayAsync($"res/{name}");
                        var path = $"{Root}/{name}";
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                    catch (Exception)
                    {
                        // One missing resource is one missing screen, not a dead game.
                        // A plain increment because this head has ONE THREAD: the tasks
                        // above interleave at their awaits, they never run side by side.
                        failed++;
                    }
                }));
            }

            // NOW the loaders, with everything in place. SciText only records the root and
            // reads each resource on demand, so it must not be pointed here until the files
            // are actually under it - a miss is cached as "this resource is empty".
            SciText.Load(Root);
            Audio.Subtitles.Load(Root);
            Audio.LipSync.SetDirectory(Path.Combine(Root, "audio", "sync"));

            Report = failed == 0
                ? $"{names.Length} resources"
                : $"{names.Length - failed} of {names.Length} resources ({failed} failed)";

            onLoaded();
        }
        catch (Exception e)
        {
            // The whole bootstrap failing leaves the game exactly as it was before this
            // existed: playable, wordless, silent.
            Report = $"not loaded: {e.Message}";
        }
    }
}
