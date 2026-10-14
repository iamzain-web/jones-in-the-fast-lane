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
    private static async Task Main(string[] args)
    {
        // BEFORE the first cel is drawn, because RenderScale settles its factor and search
        // path on first use and nothing invalidates them. Installed late, this would be a
        // store nobody ever asks.
        // args[0] is the page's own href: main.js calls runMain with globalThis.location.href.
        // It is the store's base address, and without it every art request is a relative URI
        // that WebAssembly's HttpClient refuses before it reaches the network.
        var pageUri = args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var u) ? u : null;
        var art = await HttpArtStore.LoadAsync(pageUri);
        if (art is not null)
        {
            RenderScale.UseArtStore(art);

            // Reported rather than assumed. Art present and art missing look identical from
            // inside the game - the cels simply come out at factor 1 - so the only way to
            // tell "found png4x-cast0" from "found nothing and fell back" is to say which.
            System.Console.WriteLine(
                $"Jones: art factor {RenderScale.Factor}, " +
                $"{RenderScale.ArtDirectories.Length} director(ies) of " +
                $"[{string.Join(", ", art.Inventory())}]");
        }
        else
        {
            System.Console.WriteLine(
                $"Jones: no art beside the page - drawing Sierra's 1990 pixels. " +
                $"Base {pageUri?.ToString() ?? "<unknown>"}; " +
                $"reason: {HttpArtStore.LastFailure ?? "<none given>"}");
        }

        // A phone has no keyboard and no right mouse button, exactly as Android does not -
        // and this page is opened on one far more often than on a desktop. The switches and
        // the menu bar size themselves from this; see TouchUi.
        TouchUi.Enabled = true;

        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
