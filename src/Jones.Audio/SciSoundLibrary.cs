namespace Jones.Audio;

/// <summary>
/// The 34 sound resources plus the instrument bank, decoded once and kept.
///
/// Reading the bytes is left to the caller so no head is tied to a particular asset
/// layout: the Windows head hands it files from <c>assets/raw</c>, and an Android head can
/// hand it the same bytes out of its APK without this file changing.
/// </summary>
public sealed class SciSoundLibrary
{
    private readonly Func<int, byte[]?> _load;
    private readonly Dictionary<int, SciSoundResource?> _cache = [];

    public AdLibBank Bank { get; }

    /// <param name="patchBytes">The 1344 bytes of <c>3.patch</c>.</param>
    /// <param name="loadSound">Returns the bytes of <c>&lt;n&gt;.sound</c>, or null.</param>
    public SciSoundLibrary(byte[] patchBytes, Func<int, byte[]?> loadSound)
    {
        Bank = AdLibBank.Load(patchBytes);
        _load = loadSound;
    }

    /// <summary>
    /// Reads sound resources from a directory of <c>&lt;n&gt;.sound</c> files with
    /// <c>3.patch</c> beside it in <c>../patch</c> — the layout under <c>assets/raw</c>.
    /// Returns null if either is missing, so a head with no assets stays silent instead
    /// of failing.
    /// </summary>
    public static SciSoundLibrary? FromAssetRoot(string assetRoot)
    {
        var patch = Path.Combine(assetRoot, "raw", "patch", "3.patch");
        var soundDir = Path.Combine(assetRoot, "raw", "sound");
        if (!File.Exists(patch) || !Directory.Exists(soundDir)) return null;

        try
        {
            return new SciSoundLibrary(File.ReadAllBytes(patch), n =>
            {
                var p = Path.Combine(soundDir, $"{n}.sound");
                return File.Exists(p) ? File.ReadAllBytes(p) : null;
            });
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>The decoded resource, or null when it does not exist or will not parse.</summary>
    public SciSoundResource? Get(int number)
    {
        if (_cache.TryGetValue(number, out var cached)) return cached;

        SciSoundResource? parsed = null;
        try
        {
            var bytes = _load(number);
            if (bytes is not null) parsed = SciSoundResource.Load(bytes);
        }
        catch (InvalidDataException)
        {
            // A resource that will not parse is one sound missing, not a dead game.
        }

        _cache[number] = parsed;
        return parsed;
    }
}
