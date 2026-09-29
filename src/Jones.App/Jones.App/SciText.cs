using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Jones.App;

/// <summary>
/// The game's own text resources, read from `assets/raw/text/&lt;n&gt;.text`.
///
/// A text resource is a run of NUL-terminated ASCII strings with no header, index 0 first,
/// which is exactly what `Format`'s `(Format @buf &lt;res&gt; &lt;index&gt; …)` indexes into. Resource
/// 207 is 19 bytes and holds two entries: `%d` and `Enroll for $%d?`.
///
/// This exists because the port had no way to read them at runtime, so printed strings were
/// being transcribed into the source — or, worse, taken from the SPOKEN line's subtitle,
/// which is a different sentence. `university.sc:668-682` is the clearest case: the clerk
/// SPEAKS clip 406 ("The enrollment fee is $%d. Would you like to enroll?") and the game
/// separately PRINTS text 207[1] ("Enroll for $%d?") in the Yes/No box. Two strings, two
/// jobs.
///
/// Per `CLAUDE.md` rule 2, this reads the RAW BYTES rather than the decompiled listing,
/// which renders a space inside a string literal as `_` and repeats comments across
/// different `Format` calls.
/// </summary>
public static class SciText
{
    private static string? _root;
    private static readonly Dictionary<int, string[]> Cache = [];

    /// <summary>
    /// Points the loader at the asset root — the same directory `Audio.Subtitles.Load`
    /// takes. Resources live under `&lt;root&gt;/raw/text`.
    /// </summary>
    public static void Load(string assetRoot)
    {
        _root = assetRoot;
        Cache.Clear();
    }

    /// <summary>Whether the text resources were found.</summary>
    public static bool Available =>
        _root is not null && Directory.Exists(Path.Combine(_root, "raw", "text"));

    /// <summary>
    /// Every string in a resource, in order. Empty when the resource is missing — a missing
    /// asset must never stop the game, and showing nothing is a valid port (rule 1) where
    /// inventing a replacement sentence is not.
    /// </summary>
    public static string[] All(int resource)
    {
        if (Cache.TryGetValue(resource, out var cached)) return cached;

        var strings = Array.Empty<string>();
        try
        {
            if (_root is not null)
            {
                var path = Path.Combine(_root, "raw", "text", $"{resource}.text");
                if (File.Exists(path)) strings = Split(File.ReadAllBytes(path));
            }
        }
        catch
        {
            // Missing or malformed resources fall back to nothing.
        }

        Cache[resource] = strings;
        return strings;
    }

    /// <summary>
    /// One string from a resource, or empty when it is not there. This is
    /// `(Format @buf resource index)` with no arguments.
    /// </summary>
    public static string Get(int resource, int index)
    {
        var all = All(resource);
        return index >= 0 && index < all.Length ? all[index] : "";
    }

    /// <summary>
    /// One string with its printf placeholders filled in, which is what nearly every call
    /// site actually wants: `Get(207, 1, fee)` gives `Enroll for $51?`.
    ///
    /// The formatter is <see cref="Audio.Subtitles.Format"/> — the same SCI `Format`, shared
    /// rather than reimplemented, so `%3d`, `%-16s` and the centring `%=25s` behave
    /// identically here and in the subtitles.
    /// </summary>
    public static string Get(int resource, int index, params object[] args)
    {
        var template = Get(resource, index);
        return template.Length == 0 ? template : Audio.Subtitles.Format(template, args);
    }

    /// <summary>
    /// Splits the resource bytes on NUL. The final NUL is a terminator, not a separator, so
    /// it does not produce a trailing empty entry — but an EMPTY STRING IN THE MIDDLE is
    /// real data and is kept, because indices have to stay aligned with the script's.
    /// </summary>
    internal static string[] Split(byte[] bytes)
    {
        var strings = new List<string>();
        var start = 0;

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0) continue;
            strings.Add(Encoding.ASCII.GetString(bytes, start, i - start));
            start = i + 1;
        }

        // Anything after the last NUL is an unterminated tail; the shipped resources do not
        // have one, but reading it is better than dropping it silently.
        if (start < bytes.Length)
            strings.Add(Encoding.ASCII.GetString(bytes, start, bytes.Length - start));

        return [.. strings];
    }
}
