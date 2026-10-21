using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Jones.App.Browser;

/// <summary>
/// HOLDS THE LOADING SCREEN UNTIL THE TOWN IS OURS.
///
/// <para>
/// THE SYMPTOM, IN ZAIN'S WORDS: "the old Sierra assets appear for a short while before the
/// proper ones appear."
/// </para>
///
/// <para>
/// THE CAUSE IS THE BARGAIN THIS HEAD MAKES. The art is 58 MB and arrives over the network,
/// so a cel that has not landed falls back to the 838 1x cels compiled into Jones.App -
/// Sierra's. That is the right behaviour while the game is RUNNING, because a game drawn in
/// 1990 pixels is a game and a blank screen is not. It is the wrong thing to SHOW SOMEONE
/// FIRST, and the old splash guaranteed they would see it: Avalonia empties the element the
/// splash lives in the moment the RENDERER is ready, which is well before the first cel has
/// been fetched.
/// </para>
///
/// <para>
/// So the screen now comes down when the BOARD has arrived rather than when the renderer
/// has. Both files, because the title screen draws <c>pic_11</c> and the game draws
/// <c>board.png</c>, and whichever is slower is the one that would have flashed.
/// </para>
///
/// <para>
/// WITH A DEADLINE, because a loading screen that never lifts is worse than a flash. If the
/// art has not settled in fifteen seconds the game is shown regardless - in Sierra's cels if
/// that is all there is, which is exactly what the fallback is for.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class LoadingScreen
{
    [JSImport("globalThis.jonesReady")]
    private static partial void Dismiss();

    /// <summary>The two files whose absence would show Sierra's board.</summary>
    private static readonly (string Dir, string File)[] TheBoard =
    [
        ("art/png4x-cast0", "board.png"),
        ("art/png4x-cast0", "pic_11.png"),
    ];

    /// <summary>
    /// Watches the store and takes the screen down when the board has settled - fetched, or
    /// known to be absent, which for this purpose are the same answer: there is nothing
    /// better coming.
    /// </summary>
    public static async Task HoldUntilBoardArrives(HttpArtStore art)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                var settled = true;
                foreach (var (dir, file) in TheBoard)
                    if (art.Pending(dir, file)) settled = false;

                // `Pending` is also false before anything has ASKED for the cel, so the
                // first pass would come up settled on a store that has not started. One
                // short wait is enough for the first screen to be built and to ask.
                await Task.Delay(250);

                if (settled && art.Listed > 0) break;
            }
        }
        catch (Exception)
        {
            // Never leave the screen up because the watcher broke.
        }

        try { Dismiss(); } catch (Exception) { /* the page will still be usable */ }
    }
}
