namespace Jones.Audio;

/// <summary>One MIDI message from a sound resource, at an absolute tick.</summary>
/// <param name="Tick">Ticks from the start of the resource. SCI runs at 60 ticks/second.</param>
/// <param name="Status">The MIDI status byte, channel included.</param>
public readonly record struct SciEvent(int Tick, byte Status, byte Data1, byte Data2);

/// <summary>
/// One of the game's 34 sound resources, decoded to the AdLib arrangement it carries.
///
/// The container format was already established for this project by tools/sci2midi.py and
/// checked against the real bytes; this is the same parse in C#:
///
///     track list:
///         repeat { u8 trackType, repeat { u8, u8, u16 offset, u16 size } until 0xFF }
///         until 0xFF
///     one pad byte (absent in 100.sound)
///     the channel data blobs
///
///     each blob: u8 channel (low nibble), u8 poly (low nibble) / priority (high nibble),
///                then SCI-MIDI: a delta time (0xF8 means "+240, read another byte"),
///                then a status byte or running status; 0xFC ends the channel.
///
/// The device track we want is type 0x00, AdLib. Every resource has one. Types 0x06 and
/// 0x16 are stubs pointing at another track's byte range and are ignored.
///
/// All 34 resources actually carry FIVE device tracks, one per sound card Sierra shipped a
/// driver for, and the track type is that card's id. Identified from the game's own files
/// rather than from a table someone remembered — see <see cref="TrackAdLib"/> and friends.
/// <see cref="Load"/> takes the wanted type so the other four can be read as well; the
/// default is unchanged, so every existing caller still gets the AdLib arrangement.
///
/// The channel blobs are merged into ONE time-ordered stream, which is what the original
/// parser feeds the driver. Several resources list the SAME MIDI channel twice — the
/// setup (program, volume, pan, voice count) in one blob and the notes in another — so
/// merging rather than playing blobs independently is required, not a convenience.
/// </summary>
public sealed class SciSoundResource
{
    /// <summary>
    /// AdLib / Sound Blaster FM. Nine melodic voices, instruments from <c>patch.003</c>
    /// (1344 bytes, 48 x 28 — the bank <see cref="AdLibBank"/> decodes). This is the track
    /// the port plays and the default everywhere.
    /// </summary>
    public const byte TrackAdLib = 0x00;

    /// <summary>
    /// Creative Music System / Game Blaster, the card <c>CMS.DRV</c> ("Game Blaster Card")
    /// drives, with its patch data in <c>patch.101</c> (610 bytes). Its arrangement is a
    /// subset of <see cref="TrackMt32"/> and differs from the AdLib one in both directions.
    /// </summary>
    public const byte TrackGameBlaster = 0x09;

    /// <summary>
    /// Roland MT-32 — <c>MT32.DRV</c> ("Roland MT-32, MT-100, LAPC-I, CM-…"), timbres in
    /// <c>patch.001</c>, whose first bytes are the 20-character display message
    /// <c>Jones-InThe-FastLane</c> followed by the 10-character custom timbre names
    /// (<c>Blip</c>, <c>B-3byKen</c>, <c>Elec Gtr 2</c>, <c>JonesDrm</c>, <c>SlapBassKA</c>,
    /// <c>CrashCymb</c> …).
    ///
    /// This is the richest arrangement in the file: in every one of the 34 resources its
    /// channel set is a superset of the AdLib one, and in six of them it carries an extra
    /// part the AdLib track drops (resources 5, 7, 35, 39, 41 and 46).
    /// </summary>
    public const byte TrackMt32 = 0x0C;

    /// <summary>
    /// The PC's internal speaker — <c>STD.DRV</c> ("IBM PC or Compatible Internal Speaker").
    /// Strictly one note channel in all 34 resources, which is what identifies it.
    /// </summary>
    public const byte TrackPcSpeaker = 0x12;

    /// <summary>
    /// Tandy/PCjr three-voice — <c>TANDY3V.DRV</c> ("Tandy Three-voice Only"). Never more
    /// than three note channels in any of the 34.
    /// </summary>
    public const byte TrackTandy = 0x13;

    private const byte EndOfChannel = 0xFC;
    private const byte DeltaExtend = 0xF8;
    private const int DeltaExtendTicks = 240;

    /// <summary>SCI's sound clock. `mididata contains delta in 1/60th second`.</summary>
    public const int TicksPerSecond = 60;

    /// <summary>The control channel: cues and loop markers, never notes.</summary>
    public const int ControlChannel = 15;

    /// <summary>
    /// Program change 0x7F on the control channel. The parser reads it as "loop back to
    /// here", not as an instrument.
    /// </summary>
    private const byte SetSignalLoop = 0x7F;

    public IReadOnlyList<SciEvent> Events { get; }

    /// <summary>Length of the written arrangement in ticks.</summary>
    public int DurationTicks { get; }

    /// <summary>
    /// Where a looping playback restarts. Zero unless the resource marked a loop point,
    /// which 11 of the 34 do.
    /// </summary>
    public int LoopTick { get; }

    private SciSoundResource(SciEvent[] events, int durationTicks, int loopTick)
    {
        Events = events;
        DurationTicks = durationTicks;
        LoopTick = loopTick;
    }

    /// <param name="trackType">
    /// Which device's arrangement to decode. Defaults to <see cref="TrackAdLib"/>, which is
    /// what the port plays.
    /// </param>
    public static SciSoundResource Load(byte[] data, byte trackType = TrackAdLib)
    {
        ArgumentNullException.ThrowIfNull(data);

        var tracks = ParseTrackTable(data);
        var adlib = tracks.FirstOrDefault(t => t.Type == trackType && t.Channels.Count > 0)
            ?? throw new InvalidDataException(
                $"no device track of type 0x{trackType:X2} in this sound resource");

        // (tick, blobIndex, event) so the sort can stay stable in blob order for events
        // that land on the same tick — the order the original parser merges them in.
        var merged = new List<(int Tick, int Blob, SciEvent Ev)>();
        var loopTick = 0;

        for (var b = 0; b < adlib.Channels.Count; b++)
        {
            var (offset, size) = adlib.Channels[b];
            if (offset + 2 > data.Length) continue;
            var end = Math.Min(offset + size, data.Length);

            var i = offset + 2;              // skip the channel/polyphony header
            var tick = 0;
            byte status = 0;

            while (i < end)
            {
                // ---- delta time ----
                var delta = 0;
                while (i < end && data[i] == DeltaExtend) { delta += DeltaExtendTicks; i++; }
                if (i >= end || data[i] >= 0xF8) break;
                delta += data[i++];
                tick += delta;
                if (i >= end) break;

                // ---- status byte, or running status ----
                var by = data[i];
                if ((by & 0x80) != 0) { status = by; i++; }
                else if (status == 0) break;

                if (status == EndOfChannel) break;

                var high = status & 0xF0;
                var channel = status & 0x0F;

                if (high is 0x80 or 0x90 or 0xA0 or 0xB0 or 0xE0)
                {
                    if (i + 2 > end) break;
                    var d1 = data[i];
                    var d2 = data[i + 1];
                    i += 2;
                    merged.Add((tick, b, new SciEvent(tick, status, d1, d2)));
                }
                else if (high is 0xC0 or 0xD0)
                {
                    if (i + 1 > end) break;
                    var d1 = data[i];
                    i += 1;

                    // The loop marker is consumed here rather than passed on, exactly as
                    // the original parser does with it.
                    if (high == 0xC0 && channel == ControlChannel && d1 == SetSignalLoop)
                    {
                        loopTick = tick;
                        continue;
                    }

                    merged.Add((tick, b, new SciEvent(tick, status, d1, 0)));
                }
                else if (status == 0xF0)
                {
                    // SysEx: skip to the 0xF7 terminator. None of the 34 resources uses
                    // one on the AdLib track, and the AdLib driver has nothing to do with
                    // it if they did.
                    while (i < end && data[i] != 0xF7) i++;
                    if (i < end) i++;
                    status = 0;
                }
                else
                {
                    break;
                }
            }
        }

        var ordered = merged
            .Select((e, idx) => (e.Tick, e.Blob, idx, e.Ev))
            .OrderBy(e => e.Tick).ThenBy(e => e.Blob).ThenBy(e => e.idx)
            .Select(e => e.Ev)
            .ToArray();

        var duration = ordered.Length == 0 ? 0 : ordered[^1].Tick;
        if (loopTick > duration) loopTick = 0;

        return new SciSoundResource(ordered, duration, loopTick);
    }

    private sealed record Track(byte Type, List<(int Offset, int Size)> Channels);

    private static List<Track> ParseTrackTable(byte[] d)
    {
        var tracks = new List<Track>();
        var off = 0;

        while (true)
        {
            if (off >= d.Length)
                throw new InvalidDataException("ran off the end looking for the track terminator");
            if (d[off] == 0xFF) break;

            var type = d[off++];
            var channels = new List<(int, int)>();

            while (true)
            {
                if (off >= d.Length)
                    throw new InvalidDataException("ran off the end inside a channel list");
                if (d[off] == 0xFF) { off++; break; }
                if (off + 6 > d.Length)
                    throw new InvalidDataException("truncated channel entry");

                var dataOffset = d[off + 2] | (d[off + 3] << 8);
                var dataSize = d[off + 4] | (d[off + 5] << 8);
                off += 6;
                channels.Add((dataOffset, dataSize));
            }

            tracks.Add(new Track(type, channels));
        }

        return tracks;
    }
}
