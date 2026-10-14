using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Jones.App;

namespace Jones.App.Browser;

/// <summary>
/// THE UPSCALED ART, FETCHED OVER HTTP, ONE CEL AT A TIME.
///
/// The desktop head walks up from the executable to find <c>assets/</c>; the Android head
/// reads cels out of its own package. A web page has neither. What it has is <c>fetch</c>,
/// which is ASYNCHRONOUS, on a thread that must not block — and every art call in the
/// shared code is synchronous, because on the two heads that came first it could be.
///
/// <para>
/// FETCHING THE LOT UP FRONT WAS THE OTHER OPTION AND IT IS NOT VIABLE: the 4x set is 57MB.
/// Nobody waits for that to look at a title screen, and most of it is cels for screens a
/// given player never opens. So the first ask for a cel starts a request and answers "not
/// yet"; <see cref="SciArt"/> draws Sierra's 1x pixels for that one frame and does not
/// write the miss down (see <see cref="ArtStore.Pending"/>), and when the bytes land
/// <see cref="ArtStore.Arrived"/> redraws. The opening board costs a megabyte or two rather
/// than fifty-seven.
/// </para>
///
/// <para>
/// WHICH CELS EXIST IS NOT A QUESTION HTTP CAN ANSWER. There is no directory listing, and
/// probing with a request per cel would mean a round trip to learn that an art set does not
/// cover something — which is the NORMAL case, since a set is silent about nine cels in
/// ten. So the build writes a manifest and this reads it once, before Avalonia starts.
/// That makes <see cref="Locate"/> and <see cref="Exists"/> honestly synchronous.
/// </para>
/// </summary>
internal sealed class HttpArtStore : ArtStore
{
    /// <summary>Where the build puts the art under <c>wwwroot</c>. See the csproj.</summary>
    public const string Root = "art";

    /// <summary>The manifest the csproj writes beside it: one line per file, dir/name.</summary>
    public const string ManifestPath = "art/art-manifest.txt";

    private readonly HttpClient _http = new();
    private readonly Dictionary<string, HashSet<string>> _listing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);

    private HttpArtStore() { }

    /// <summary>
    /// Reads the manifest and hands back a store, or null if there is no art published
    /// beside the page — in which case the head keeps the 1x art compiled into the
    /// assembly, which is exactly what it did before this class existed.
    /// </summary>
    /// <summary>Why the last <see cref="LoadAsync"/> gave up, for the startup log.</summary>
    public static string? LastFailure { get; private set; }

    /// <param name="pageUri">
    /// The page's own address, which the head gets as <c>args[0]</c> — <c>main.js</c> calls
    /// <c>runMain</c> with <c>globalThis.location.href</c>.
    ///
    /// <para>
    /// IT IS REQUIRED, AND LEAVING IT OUT IS WHY THE FIRST DEPLOYMENT SHOWED SIERRA'S ART.
    /// In WebAssembly <c>HttpClient</c> has NO BaseAddress — Blazor sets one in its host
    /// builder and a plain Avalonia browser app has no host builder — so a relative request
    /// like <c>art/art-manifest.txt</c> throws <i>before any request is made</i>. The art was
    /// published, served and reachable; nothing ever asked for it.
    /// </para>
    /// </param>
    public static async Task<HttpArtStore?> LoadAsync(Uri? pageUri)
    {
        LastFailure = null;
        var store = new HttpArtStore();
        if (pageUri is not null) store._http.BaseAddress = pageUri;

        try
        {
            var text = await store._http.GetStringAsync(ManifestPath);
            foreach (var line in text.Split('\n'))
            {
                var entry = line.Trim();
                if (entry.Length == 0) continue;

                var slash = entry.LastIndexOf('/');
                if (slash <= 0) continue;

                var dir = $"{Root}/{entry[..slash]}";
                var name = entry[(slash + 1)..];
                if (!store._listing.TryGetValue(dir, out var set))
                    store._listing[dir] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(name);
            }
        }
        catch (Exception e)
        {
            // No manifest is a legitimate deployment: the page still plays, in Sierra's own
            // pixels, exactly as it did before there was any art to fetch.
            //
            // BUT THE REASON IS KEPT. This catch once swallowed "HttpClient has no
            // BaseAddress" and reported it as "no art published", which sent the search to
            // the deployment - where the art was present, served and reachable - instead of
            // to the four lines above it. A fallback that cannot say why it fell back turns
            // a fixable error into a property of the build.
            LastFailure = e.Message;
            return null;
        }

        if (store._listing.Count > 0) return store;
        LastFailure = "the manifest downloaded but listed no files";
        return null;
    }

    /// <summary>The art directories the manifest listed, for the startup log.</summary>
    public string[] Inventory() => [.. _listing.Keys];

    public override string? Locate(string name)
    {
        var path = $"{Root}/{name}";
        return _listing.ContainsKey(path) ? path : null;
    }

    public override bool Exists(string directory, string file)
        => _listing.TryGetValue(directory, out var set) && set.Contains(file);

    /// <summary>
    /// The cel's bytes if they are already here; otherwise null, having started the fetch.
    /// </summary>
    public override Stream? Open(string directory, string file)
    {
        var key = $"{directory}/{file}";
        if (_bytes.TryGetValue(key, out var cached)) return new MemoryStream(cached, writable: false);

        if (!Exists(directory, file)) return null;     // genuinely absent, not pending
        Begin(key);
        return null;
    }

    /// <summary>
    /// True while a cel we know exists has not arrived. This is the whole reason the base
    /// class has the method: without it the first draw's null is cached forever.
    /// </summary>
    public override bool Pending(string directory, string file)
    {
        var key = $"{directory}/{file}";
        return !_bytes.ContainsKey(key) && Exists(directory, file);
    }

    /// <summary>
    /// Starts one request, at most once per cel.
    ///
    /// <para>
    /// ONE NOTIFICATION PER ARRIVAL, not one per batch, and that is deliberate: a screen
    /// asks for a handful of cels in the same frame, so its fetches land within a few
    /// milliseconds of each other and each redraw picks up whatever else has arrived by
    /// then. Debouncing would be the optimisation to reach for if that ever shows, and it
    /// does not at this size.
    /// </para>
    /// </summary>
    private void Begin(string key)
    {
        if (!_inFlight.Add(key)) return;

        _ = Fetch(key);

        async Task Fetch(string path)
        {
            try
            {
                var data = await _http.GetByteArrayAsync(path);
                _bytes[path] = data;
                NotifyArrived();
            }
            catch (Exception)
            {
                // A cel that will not download is a cel the screen omits, same as a missing
                // one - but it stays OUT of _bytes and IN _inFlight, so it is not retried
                // every frame for the life of the page.
            }
        }
    }
}
