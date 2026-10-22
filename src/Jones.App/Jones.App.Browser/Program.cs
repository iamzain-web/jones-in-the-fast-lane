using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Jones.App;
using Jones.App.Browser;

internal sealed partial class Program
{
    /// <summary>
    /// The browser head's equivalent of the desktop's <c>Program.Main</c> and the Android
    /// head's <c>JonesRuntime.Start</c>: everything that has to be true before Avalonia
    /// builds a window.
    ///
    /// <para>
    /// IT USED TO BE ONE LINE, and the consequence was a page that ran Sierra's 1990 game.
    /// A head that finds no art falls back to the 838 cels compiled into the shared
    /// assembly, and falling back is indistinguishable from working - which is how this
    /// shipped. The art is now fetched from beside the page; see HttpArtStore.
    /// </para>
    ///
    /// <para>
    /// EVERY STEP IS SURVIVABLE, for the same reason the Android head's are: a game with no
    /// upscaled art is a game, and a page that will not open is not. No manifest, a failed
    /// request, a deployment with no art published - each one leaves the game running in
    /// Sierra's own pixels rather than leaving it blank.
    /// </para>
    /// </summary>
    /// <summary>
    /// NOT <c>async</c>, AND THAT IS NOT A STYLE CHOICE - IT IS THE DIFFERENCE BETWEEN A
    /// PAGE THAT RUNS AND A PAGE THAT DIES BEFORE ITS FIRST LINE.
    ///
    /// <para>
    /// With <c>async Task Main</c> the compiler keeps the body as a state machine and
    /// SYNTHESISES an entry point of its own - a real <c>Main(string[])</c> whose whole
    /// body is <c>&lt;Main&gt;(args).GetAwaiter().GetResult()</c>. That is a BLOCKING WAIT,
    /// and the Mono interpreter this head runs on has no implementation for it:
    /// </para>
    /// <code>
    /// MONO interpreter: NIY encountered in method Program:Main (string[])
    /// [MONO] * Assertion: should not be reached at interp.c:4135
    /// Uncaught {name: ExitStatus, message: Program terminated with exit(1), status: 1}
    /// </code>
    /// <para>
    /// Not one statement below ran. The page showed the Avalonia splash and stopped, with
    /// everything it needed already downloaded - and the only account of it was in the
    /// browser console, which is why this sat deployed and broken: the build is clean, the
    /// publish is clean, the server returns 200 for all 1,318 files, and the game is dead.
    /// </para>
    ///
    /// <para>
    /// Returning the Task instead hands it to the runtime, which is what the Avalonia
    /// browser template does and what <c>runMain</c> is built to receive. Nothing here ever
    /// needed to await: every step before the last is synchronous by design (see the note
    /// about the manifest below), so there was nothing for the state machine to do but
    /// break the entry point.
    /// </para>
    /// </summary>
    private static Task Main(string[] args)
    {
        // BEFORE the first cel is drawn, because RenderScale settles its factor and search
        // path on first use and nothing invalidates them. Installed late, this would be a
        // store nobody ever asks.
        // args[0] is the page's own href: main.js calls runMain with globalThis.location.href.
        // It is the store's base address, and without it every art request is a relative URI
        // that WebAssembly's HttpClient refuses before it reaches the network.
        var pageUri = args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var u) ? u : null;

        // NOTHING IS AWAITED HERE, AND THAT IS THE POINT. The first version downloaded a
        // manifest before handing control to Avalonia, and on the deployed site that await
        // never returned: splash on screen, renderer frozen, the tab too wedged for devtools
        // to evaluate an expression in it. One thread, and the browser's event loop is on it.
        // The store is usable the moment it is constructed and learns what it has in the
        // background - a hang is worse than a failure, because a failure has an error to read.
        // SAVED GAMES AND SETTINGS, MADE DURABLE. Must run before anything asks whether a
        // save exists: localStorage is synchronous, so by the time this returns the save is
        // back on the in-memory "disk" and SaveStore.Exists() is already telling the truth.
        DurableSaves.Install();

        // HOUSE RULE, applied per head rather than in the core so Jones.Core stays a
        // faithful port and its tests keep measuring Sierra's game rather than ours. Both
        // other heads set this; THIS ONE NEVER DID, so the web build alone opened on
        // Sierra's brutal version of week one - the turn-start chain runs on turn one
        // regardless, a new player has no food, and the starvation notice takes 20 of the
        // week's 60 hours and arms a doctor roll on top.
        //
        // Zain, reading it on the deployed page: "it starts out at less time due to hunger
        // and a doctors bill", and took it for an old build. It was not an old build; it
        // was this head missing something the other two were given, which is now the third
        // time that sentence has been written about this file.
        //
        // Set it to false to play Sierra's version exactly. See Player.StartingFoodWeeks
        // for why handing out food instead does not work.
        Jones.Core.Model.TurnStart.SkipWeekOneHardship = true;

        // SOUND. Without this the head falls through to SilentAudioPlayer, which is what
        // it has always done: no music, no effects, no speech, and nothing saying why.
        Jones.App.ViewModels.MainViewModel.Sound = new BrowserAudioPlayer();

        var art = new HttpArtStore(pageUri);
        RenderScale.UseArtStore(art);

        // THE OTHER HALF OF THE HEAD, and it was missing entirely until now. The desktop
        // and Android heads both call SciText.Load, Subtitles.Load and LipSync.SetDirectory
        // before the first frame; this one called none of them, because each takes a path
        // and a browser has no disk. GameData fetches the files into the in-memory
        // filesystem WebAssembly does have and then calls the ordinary loaders.
        //
        // NOT AWAITED, for the same reason the manifest is not: one thread, and the
        // browser's event loop is on it. The game opens without its words and fills them in,
        // which is the bargain the art already makes.
        _ = GameData.LoadAsync(new System.Net.Http.HttpClient { BaseAddress = pageUri },
                               art.NotifyRebuildNeeded);

        // Reported rather than assumed. Art present and art missing look identical from
        // inside the game - the cels simply come out at factor 1 - so the only way to tell
        // "found png4x-cast0" from "found nothing and fell back" is to say which.
        System.Console.WriteLine(
            $"Jones: art factor {RenderScale.Factor}, " +
            $"{RenderScale.ArtDirectories.Length} director(ies) of " +
            $"[{string.Join(", ", art.Inventory())}], base {pageUri?.ToString() ?? "<unknown>"}");

        // A phone has no keyboard and no right mouse button, exactly as Android does not -
        // and this page is opened on one far more often than on a desktop. The switches and
        // the menu bar size themselves from this; see TouchUi.
        TouchUi.Enabled = true;

        // SMOOTH MOTION OFF HERE, AND IT IS A PERFORMANCE DECISION WITH A MEASURED CAUSE.
        //
        // `BuildScreen` clears nine collections and rebuilds every sprite, control, hotspot
        // and text on screen. While the marble is walking it is called from TWO timers:
        // WalkTick at 40Hz and SmoothTick at ~60Hz. That is about a hundred full screen
        // teardowns a second, each destroying and recreating every visual.
        //
        // The desktop absorbs it. This head does not: it has one thread, the browser's event
        // loop is on it, and when those ticks slip the WALK ITSELF SLOWS DOWN - the marble
        // advances one path step per WalkTick, so a tick that arrives late is a journey that
        // takes longer. Zain: "the mans walk is very slow, and it takes a very long time for
        // him to get to where he is going." That is the mechanism, not a frame rate.
        //
        // Turning this off removes roughly 60 of those 100 rebuilds immediately. The cost is
        // the original's own stepping between path nodes rather than tweened movement - which
        // is exactly what Sierra's game does, and what `Jones Original` runs with.
        //
        // THE REAL FIX IS THAT BuildScreen SHOULD NOT CLEAR. Reconciling the collections
        // instead of tearing them down would help every head, and it is a change to shared
        // code that deserves its own run at it rather than being smuggled in here.
        Jones.App.ViewModels.MainViewModel.SmoothMotion = false;

        // NO RenderingMode OVERRIDE HERE, AND THAT WAS CHECKED RATHER THAN ASSUMED. The
        // obvious first move on a slow browser build is "turn the GPU on" - but Avalonia
        // 12's BrowserRenderingMode has no software member at all (WebGL2 and WebGL1
        // compile; Software and Canvas2D do not exist), so this head is ALREADY on WebGL
        // and setting it would be a no-op dressed as an optimisation. Whatever is slow
        // here, it is not the rasteriser being on the CPU.
        // THE LOADING SCREEN COMES DOWN WHEN THE BOARD DOES, not when the renderer is ready.
        // Started here and not awaited: it outlives this method by design, watching the art
        // store while the game is already running behind the screen.
        _ = LoadingScreen.HoldUntilBoardArrives(art);

        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
