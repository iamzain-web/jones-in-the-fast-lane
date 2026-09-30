using System;
using Avalonia;

namespace Jones.App.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // HOUSE RULE, applied here rather than in the core so that Jones.Core stays a
        // faithful port and its tests keep measuring Sierra's game rather than ours.
        //
        // The original gives a new player no food, and the turn-start chain runs on turn
        // one regardless, so a fresh game opens by taking 20 of the week's 60 hours for
        // hunger — and a further 10 a quarter of the time, because starving carries its own
        // doctor roll. See Player.StartingFoodWeeks for the full citation trail. One week
        // of food clears it; the weekly tick eats it immediately, so the player still has
        // to go shopping in week one.
        //
        // Set this to false to play Sierra's version exactly.
        //
        // Handing out food instead does NOT work: a new player has no refrigerator either,
        // so state 10 spoils it, costs 2 happiness, and arms the 1-in-2 doctor roll because
        // the meal was already chosen at state 8. That swaps one punishment for another.
        // The hardship itself has to be skipped, and only in week 1 — from week 2 the player
        // has had a turn to buy food and a fridge, and every rule applies in full.
        Jones.Core.Model.TurnStart.SkipWeekOneHardship = true;

        // Supply the Windows sound implementation. The shared app is silent by default,
        // so the game still runs if the audio assets are missing.
        var assets = FindAssetRoot();

        // The game's own text resources are not audio and not Windows-specific, so they load
        // outside the guard below: every head needs the strings the game PRINTS.
        if (assets is not null)
        {
            SciText.Load(assets);

            // The upscaled art sits beside the speech, under the same root, and is loaded
            // from disk for the same reason: 23MB is too much to embed. Handing the root
            // over here saves RenderScale repeating the walk, and means the art is found
            // wherever the speech was. It still falls back to its own search (and then to
            // the embedded 1x decode) if this is never called.
            RenderScale.UseAssetRoot(assets);
        }

        if (OperatingSystem.IsWindows())
        {
            // The braces matter: without them only the first line was conditional and the
            // other two ran with a null path when the assets were not found, throwing on
            // startup. WindowsAudioPlayer now also loads the sound bank from here, so all
            // three belong behind the same test.
            if (assets is not null)
            {
                ViewModels.MainViewModel.Sound = new WindowsAudioPlayer(assets);
                Audio.LipSync.SetDirectory(System.IO.Path.Combine(assets, "audio", "sync"));
                Audio.Subtitles.Load(assets);
            }
        }

        // The remembered sound switches. AFTER the player is in place, because the master
        // switch lives on it, and BEFORE the UI is built, so the first binding already reads
        // the remembered value and a muted game never makes a sound on its way up.
        //
        // A DEVIATION: the original resets its volume to 12 on every launch — see
        // Jones.Core.Save.SettingsStore for why `Main.sc:1198-1200` is not what it looks like.
        ViewModels.MainViewModel.LoadSoundSettings();

        // Network play: `--host [port]` or `--join address[:port]`. See Jones.Net/Protocol.cs.
        Net.NetLaunch.Parse(args);

        // The network reports only to the log (it has no screens of its own — CLAUDE.md §1),
        // and this head otherwise has no log at all. So a networked copy writes one, named
        // for its role so a host and a joiner on the same machine do not share it:
        // %TEMP%\jones-host.log or %TEMP%\jones-join.log.
        if (Net.NetLaunch.Host || Net.NetLaunch.JoinAddress is not null)
        {
            var log = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                Net.NetLaunch.Host ? "jones-host.log" : "jones-join.log");
            StartupLog.Info = m => System.IO.File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} {m}\n");
            StartupLog.Error = (m, e) => System.IO.File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} {m}: {e}\n");
        }

        // THE INTERFACE-FONT PROOF SHEET, and it is not a screenshot tool. `JONES_FONT_SHEET`
        // names a directory; the process sets Avalonia up WITHOUT opening a window, draws a
        // handful of real screens through `UiFont` — the same class, the same metrics and the
        // same rasteriser the game uses — writes them as PNGs and exits.
        //
        // That is the difference between a sample and a proof: `tools/make_font_sheets.py`
        // draws candidate faces with Pillow, which is a preview of a design, while this draws
        // what the running game will actually put on screen. If the two ever disagree, this
        // one is right.
        if (Environment.GetEnvironmentVariable("JONES_FONT_SHEET") is { Length: > 0 } sheetDir)
        {
            // A BARE Application, not this project's App: the sheet needs the rendering and
            // font platforms and nothing else, and going through `App` drags in its compiled
            // XAML, its Fluent theme and its lifetime for no benefit.
            AppBuilder.Configure<Avalonia.Application>()
                .UsePlatformDetect()
                .LogToTrace()
                .SetupWithoutStarting();

            FontSheet.Write(sheetDir);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // Hand the waveOut device and the MCI alias back on the way out. The synth's pump
        // is a background thread so the process would exit regardless, but closing the
        // device properly beats leaving winmm to find out.
        (ViewModels.MainViewModel.Sound as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Walks up from the executable looking for the extracted audio. Speech is 21MB of
    /// WAV, which is too much to embed in the app, so it is loaded from disk.
    /// </summary>
    private static string? FindAssetRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = System.IO.Path.Combine(dir, "assets");
            if (System.IO.Directory.Exists(System.IO.Path.Combine(candidate, "audio", "speech")))
                return candidate;
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
