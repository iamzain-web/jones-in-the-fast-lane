using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jones.App.Audio;

/// <summary>
/// Subtitles for the spoken lines, so the game is playable with the sound off and by
/// deaf players.
///
/// The CD release replaced the floppy release's printed text with recorded speech, so
/// most lines have no text resource in the CD build at all. The subtitles therefore come
/// from the FLOPPY build's text resources, matched to CD audio ids by finding the same
/// call site in both decompiled builds.
///
/// Coverage is partial and that is deliberate: a wrong subtitle is worse than a missing
/// one, so only lines whose mapping was established are included. Everything else shows
/// nothing rather than a guess.
/// </summary>
public static class Subtitles
{
    private static Dictionary<string, string>? _lines;

    /// <summary>Loads subtitles.json, if it has been generated.</summary>
    public static void Load(string assetRoot)
    {
        try
        {
            var path = Path.Combine(assetRoot, "audio", "subtitles.json");
            if (!File.Exists(path)) return;

            _lines = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(path));
        }
        catch
        {
            // Missing or malformed subtitles must never stop the game.
        }
    }

    /// <summary>The line for a speech clip, or empty if it has no known text.</summary>
    public static string For(int audioId) =>
        _lines is not null && _lines.TryGetValue(audioId.ToString(), out var line) ? line : "";

    /// <summary>
    /// The line for a speech clip, with its printf placeholders filled in.
    ///
    /// 17 of the 533 subtitles are FORMAT STRINGS, because the line they came from was
    /// built with `Format` — the enrollment line is text 207[1], `Enroll for $%d?`, fed
    /// `(enrollmentFee price:)` at `university.sc:672`. Showing the raw string put a
    /// literal "$%d" in the speech balloon.
    ///
    /// SCI's Format is C's printf, so this handles `%d` and `%s` with an optional width
    /// (`%3d`, `%2d` both appear in the resources). Anything it cannot fill is left alone
    /// rather than guessed at.
    /// </summary>
    public static string For(int audioId, params object[] args)
    {
        var line = For(audioId);
        if (line.Length == 0 || args.Length == 0) return line;

        return Format(line, args);
    }

    /// <summary>
    /// SCI's `Format`, which is C's printf plus one addition: `=` means CENTRE the field.
    /// The stats screen is built almost entirely out of these — `%=25s` for every heading,
    /// `Works at %-16s`, `Hourly wage: $%-11d` (text resource 231) — so the columns line up
    /// by padding rather than by positioning.
    ///
    /// Shared deliberately with the subtitles: both are the same kernel call, and writing a
    /// second formatter for the stats screen would be two places to get it wrong.
    /// </summary>
    public static string Format(string template, params object[] args)
    {
        if (template.Length == 0 || args.Length == 0) return template;

        var next = 0;
        return Regex.Replace(template, @"%([-=]?)(\d*)([ds])", m =>
        {
            if (next >= args.Length) return m.Value;

            var text = args[next++]?.ToString() ?? "";
            if (!int.TryParse(m.Groups[2].Value, out var width) || width <= text.Length)
                return text;

            var pad = width - text.Length;
            return m.Groups[1].Value switch
            {
                "-" => text.PadRight(width),
                "=" => new string(' ', pad / 2) + text + new string(' ', pad - pad / 2),
                _   => text.PadLeft(width),
            };
        });
    }

    public static bool Available => _lines is { Count: > 0 };
}
