using System;
using System.Collections.Generic;
using System.IO;

namespace Jones.App.Audio;

/// <summary>
/// Lip-sync for the shopkeeper portraits, from the game's own `.sync` resources.
///
/// Format, verified against the raw bytes: a flat list of (u16 tick, u16 cue) pairs
/// terminated by 0xFFFF, where the tick is in 60ths of a second and the talker's cel is
/// `cue &amp; 0x0F`. Every talker view has exactly 11 cels — the mouth positions — which
/// matches the cue range observed in the data (0..10).
///
/// This is the original's own timing data, not an approximation: `MouthSync`
/// (`Sync.sc:81`) does exactly `(client cel: (&amp; $000f (global558 prevCue:)))`.
/// </summary>
public static class LipSync
{
    /// <param name="AtMs">Milliseconds from the start of the clip.</param>
    public readonly record struct Frame(int AtMs, int Cel);

    private static readonly Dictionary<int, Frame[]> Cache = [];
    private static string? _dir;

    /// <summary>Points the loader at the extracted sync resources.</summary>
    public static void SetDirectory(string dir) => _dir = dir;

    /// <summary>
    /// Mouth frames for a speech clip, or empty if that clip has no sync resource.
    /// Ten of the 543 sync resources have no matching audio, and some audio has no sync,
    /// so a caller must cope with an empty result.
    /// </summary>
    public static Frame[] For(int audioId)
    {
        if (Cache.TryGetValue(audioId, out var cached)) return cached;

        var frames = Array.Empty<Frame>();
        try
        {
            if (_dir is not null)
            {
                var path = Path.Combine(_dir, $"{audioId}.sync");
                if (File.Exists(path)) frames = Parse(File.ReadAllBytes(path));
            }
        }
        catch
        {
            // A malformed resource must never stop the game.
        }

        Cache[audioId] = frames;
        return frames;
    }

    private static Frame[] Parse(byte[] data)
    {
        var frames = new List<Frame>();

        for (var i = 0; i + 3 < data.Length; i += 4)
        {
            var tick = BitConverter.ToUInt16(data, i);
            if (tick == 0xFFFF) break;

            var cue = BitConverter.ToUInt16(data, i + 2);

            // Ticks are 60ths of a second.
            frames.Add(new Frame(tick * 1000 / 60, cue & 0x0F));
        }

        return frames.ToArray();
    }
}
