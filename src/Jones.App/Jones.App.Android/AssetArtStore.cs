using System;
using System.Collections.Generic;
using System.IO;
using ARes = global::Android.Content.Res;

namespace Jones.App.Android;

/// <summary>
/// THE UPSCALED ART, READ STRAIGHT OUT OF THE APK.
///
/// The desktop head finds <c>assets/png4x</c> by walking up from the executable. An APK
/// has no tree to walk, so the art rides inside it under <c>art/</c> and this store hands
/// <see cref="RenderScale"/> and <see cref="SciArt"/> the three answers they need.
///
/// WHY NOT UNPACK IT, like <see cref="GameAssets"/> does for the 300KB of text and sync
/// tables? Those have to become real files because the code that reads them is shared and
/// calls <c>System.IO.File</c> with a path. The art does not: every cel goes through
/// <c>SciArt.LoadCel</c>, which takes a stream. Unpacking would cost the phone a second
/// copy of 57MB and a visible wait on first run, to arrive at bytes the AssetManager was
/// already holding.
///
/// AND THEY ARE NOT COMPRESSED IN THERE. <c>.png</c> is on aapt's no-compress list, so a
/// cel is a mapped read out of the package rather than an inflate — which is the whole
/// reason this is cheap enough to do per cel, lazily, at the moment one is first drawn.
/// </summary>
internal sealed class AssetArtStore : ArtStore
{
    /// <summary>
    /// Where the art sits inside the APK's assets. Matches the <c>Link</c> metadata on the
    /// AndroidAsset items in the csproj; the two have to agree and there is no build-time
    /// check that they do, so the startup log prints what was actually found.
    /// </summary>
    public const string AssetRoot = "art";

    private readonly ARes.AssetManager _assets;

    /// <summary>
    /// One listing per art directory, taken once and kept. The alternative — asking the
    /// AssetManager to open a cel and treating the throw as "no" — costs an exception per
    /// miss, and a miss is the NORMAL case: an art set is silent about nine cels in ten
    /// (see SciArt.NoteFallback), so every one of those would throw before falling through
    /// to the ported art.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _listings = new(StringComparer.Ordinal);

    public AssetArtStore(ARes.AssetManager assets) => _assets = assets;

    /// <summary>
    /// The art directories actually present in the APK, for the startup log. Reported
    /// rather than assumed: shipping the art and forgetting to ship it look identical from
    /// inside the game — the cels just come out at 1x — and that is exactly the failure
    /// this head shipped with before.
    /// </summary>
    public string[] Inventory()
    {
        try
        {
            return _assets.List(AssetRoot) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// <paramref name="name"/> is a plain directory name such as <c>png4x-cast0</c>; the
    /// token handed back is its asset path. A directory with no entries counts as absent,
    /// which is what sends RenderScale down to the next factor rather than selecting a
    /// factor whose art turns out to be empty.
    /// </summary>
    public override string? Locate(string name)
    {
        var path = $"{AssetRoot}/{name}";
        return Listing(path).Count > 0 ? path : null;
    }

    public override bool Exists(string directory, string file) => Listing(directory).Contains(file);

    public override Stream? Open(string directory, string file)
    {
        if (!Exists(directory, file)) return null;

        try
        {
            // SEEKABLE, because Avalonia's PNG decoder seeks and an AssetManager stream
            // cannot — it is a forward-only reader over the package. A 4x cel is a few
            // hundred KB and the biggest (the board) is 1.2MB, held only until the decode
            // returns.
            var buffer = new MemoryStream();
            using (var asset = _assets.Open($"{directory}/{file}"))
                asset.CopyTo(buffer);
            buffer.Position = 0;
            return buffer;
        }
        catch (Exception e)
        {
            AndroidLog.Error($"art: {directory}/{file}", e);
            return null;
        }
    }

    private HashSet<string> Listing(string directory)
    {
        if (_listings.TryGetValue(directory, out var cached)) return cached;

        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in _assets.List(directory) ?? [])
                names.Add(entry);
        }
        catch (Exception)
        {
            // An absent directory is a legitimate answer - it is how a head with no art set
            // falls through to the ported art, and how one with no art at all falls to 1x.
        }

        _listings[directory] = names;
        return names;
    }
}
