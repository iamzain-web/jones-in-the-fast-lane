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
/// FETCHING THE LOT UP FRONT IS NOT VIABLE: the 4x set is 57MB, and most of it is cels for
/// screens a given player never opens. So the first ask for a cel starts a request and
/// answers "not yet"; <see cref="SciArt"/> draws Sierra's 1x pixels for that frame and does
/// not write the miss down (see <see cref="ArtStore.Pending"/>), and when the bytes land
/// <see cref="ArtStore.Arrived"/> redraws.
/// </para>
///
/// <para>
/// NOTHING HERE IS AWAITED BEFORE THE GAME STARTS, AND THAT IS A SCAR. The first version
/// downloaded a manifest in <c>Main</c> before handing control to Avalonia. On the deployed
/// site that await never came back: the page loaded the runtime, showed the splash and sat
/// there with the renderer frozen — the tab so wedged that the devtools could not evaluate
/// an expression in it. A WebAssembly app has ONE thread and the browser's event loop is on
/// it; a startup that waits for the network can hang the whole page, and a hang is worse
/// than a failure because it has no error to read. So the store is constructed
/// synchronously, is usable immediately, and learns what it has in the background.
/// </para>
///
/// <para>
/// WHICH MEANS IT STARTS OPTIMISTIC. Until the manifest arrives it assumes a cel exists and
/// lets the request answer; a 404 is recorded and never asked for twice. The manifest is an
/// OPTIMISATION — it turns nine misses in ten into nothing at all, because an art set is
/// silent about most cels — and no longer a dependency.
/// </para>
/// </summary>
internal sealed class HttpArtStore : ArtStore
{
    /// <summary>Where the build puts the art under <c>wwwroot</c>. See the csproj.</summary>
    public const string Root = "art";

    /// <summary>The manifest the csproj writes beside it: one line per file, dir/name.</summary>
    public const string ManifestPath = "art/art-manifest.txt";

    /// <summary>
    /// The art directories this head may draw from, in the order <see cref="RenderScale"/>
    /// asks for them. Hard-coded because they are what the csproj publishes, and because
    /// <see cref="Locate"/> has to answer before any manifest could have arrived.
    /// </summary>
    private static readonly string[] Published = ["png4x", "png4x-cast0"];

    private readonly HttpClient _http = new();
    private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> _absent = new(StringComparer.Ordinal);
    private Dictionary<string, HashSet<string>>? _listing;

    /// <summary>Why the manifest did not load, if it did not. For the startup log.</summary>
    public string? ManifestFailure { get; private set; }

    /// <summary>How many cels the manifest listed, or 0 while it has not arrived.</summary>
    public int Listed { get; private set; }

    public HttpArtStore(Uri? pageUri)
    {
        // THE BASE ADDRESS IS NOT OPTIONAL. In WebAssembly HttpClient has none - Blazor sets
        // one in its host builder and a plain Avalonia browser app has no host builder - so
        // every relative request throws before it reaches the network. The art was published,
        // served and reachable for a whole deployment while nothing ever asked for it.
        if (pageUri is not null) _http.BaseAddress = pageUri;

        _ = LoadManifest();     // deliberately not awaited; see the class remarks
    }

    private async Task LoadManifest()
    {
        try
        {
            var text = await _http.GetStringAsync(ManifestPath);
            var listing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var line in text.Split('\n'))
            {
                var entry = line.Trim();
                var slash = entry.LastIndexOf('/');
                if (slash <= 0) continue;

                var dir = $"{Root}/{entry[..slash]}";
                if (!listing.TryGetValue(dir, out var set))
                    listing[dir] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(entry[(slash + 1)..]);
                Listed++;
            }
            _listing = listing;
        }
        catch (Exception e)
        {
            // Not fatal and not even important: without it the store simply asks the server
            // about every cel instead of knowing in advance. Recorded because a fallback
            // that cannot say why it fell back turns a fixable error into a property of the
            // build - which is exactly what happened to the base address above.
            ManifestFailure = e.Message;
        }
    }

    /// <summary>The art directories this head offers, for the startup log.</summary>
    public string[] Inventory() => [.. Published];

    public override string? Locate(string name)
        => Array.IndexOf(Published, name) >= 0 ? $"{Root}/{name}" : null;

    /// <summary>
    /// Whether the cel is there. OPTIMISTIC until the manifest lands: an unknown cel is
    /// assumed to exist so the request can answer, and a 404 is remembered.
    /// </summary>
    public override bool Exists(string directory, string file)
    {
        if (_absent.Contains($"{directory}/{file}")) return false;
        if (_listing is { } listing)
            return listing.TryGetValue(directory, out var set) && set.Contains(file);
        return true;
    }

    public override Stream? Open(string directory, string file)
    {
        var key = $"{directory}/{file}";
        if (_bytes.TryGetValue(key, out var cached)) return new MemoryStream(cached, writable: false);

        if (!Exists(directory, file)) return null;
        Begin(key);
        return null;
    }

    public override bool Pending(string directory, string file)
    {
        var key = $"{directory}/{file}";
        return !_bytes.ContainsKey(key) && !_absent.Contains(key) && Exists(directory, file);
    }

    private void Begin(string key)
    {
        if (!_inFlight.Add(key)) return;
        _ = Fetch(key);

        async Task Fetch(string path)
        {
            try
            {
                var response = await _http.GetAsync(path);
                if (!response.IsSuccessStatusCode) { _absent.Add(path); return; }

                _bytes[path] = await response.Content.ReadAsByteArrayAsync();
                NotifyArrived();
            }
            catch (Exception)
            {
                // Treated as absent rather than retried: a cel that will not download is a
                // cel the screen omits, and retrying every frame would be a request storm.
                _absent.Add(path);
            }
        }
    }
}
