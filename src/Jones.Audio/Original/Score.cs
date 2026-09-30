using System.Globalization;
using System.Text;

namespace Jones.Audio.Original;

/// <summary>
/// The note data, as plain text a person can read and edit.
///
/// This is deliberately NOT a binary blob and not MIDI. The user is going to want to change
/// a chord, move a snare, try a different bass line — and the format has to make that a
/// matter of editing one character in a line that already looks like what you hear.
///
/// A score is a grid. Each part gets one row of steps per bar; a step is a sixteenth note
/// by default. A token is:
///
///     C3            a note (letter, optional # or b, octave; middle C is C4 = MIDI 60)
///     C3+Eb3+G3     a chord — as many notes as you like, joined with +
///     C3:70         a note at velocity 70 out of 127
///     -             hold whatever is sounding on this part
///     .             silence — release whatever is sounding
///     bd sd hh oh cp    in a `kit` part, the drum instead of a pitch
///
/// and the header lines are:
///
///     tempo 112             beats per minute
///     steps 16              steps per bar (16 = sixteenths in 4/4)
///     swing 0.14            0 straight, 0.33 a full triplet shuffle
///     loop 1                whether the cue is a bed that repeats
///     loop-from 64          the step a repeat jumps back to — everything before it is an
///                           intro, heard once. Default 0, the top.
///     space room|dry        the effect setting the whole cue renders through
///     part bass patch=bass gain=0.9 pan=-0.1
///     part drums kit gain=0.8
///
/// Everything after # is a comment, blank lines are ignored, and a part's rows are simply
/// appended in the order they appear, so a 16-bar cue is 16 lines per part and you can see
/// the shape of the arrangement down the page.
/// </summary>
public sealed class Score
{
    public double Tempo { get; private set; } = 110;
    public int StepsPerBar { get; private set; } = 16;
    public double Swing { get; private set; }
    public bool Loop { get; private set; }

    /// <summary>
    /// The step a repeat jumps back to. Zero means the top.
    ///
    /// The game's own cues do this — eleven of the 34 mark a loop point and several mark it
    /// well inside the arrangement, so the opening bars are heard once and the repeat is
    /// shorter than the whole. That is a musical decision Sierra made about each location
    /// and it is honoured here: the ORIGINAL'S STRUCTURE, not its notes.
    /// </summary>
    public int LoopFromStep { get; private set; }

    public EffectSettings Space { get; private set; } = EffectSettings.Room;
    public IReadOnlyList<Part> Parts => _parts;

    private readonly List<Part> _parts = [];

    public sealed class Part
    {
        public required string Name { get; init; }
        public required SynthPatch Patch { get; init; }
        public bool IsKit { get; init; }
        public double Gain { get; init; } = 1.0;
        public double Pan { get; init; }
        public List<string> Steps { get; } = [];
    }

    /// <summary>Length in steps — the longest part decides.</summary>
    public int LengthInSteps
    {
        get
        {
            var n = 0;
            foreach (var p in _parts) n = Math.Max(n, p.Steps.Count);
            return n;
        }
    }

    public double StepSeconds => 60.0 / Tempo / (StepsPerBar / 4.0);
    public double LengthSeconds => LengthInSteps * StepSeconds;

    // ------------------------------------------------------------------ parsing

    public static Score Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var score = new Score();
        var byName = new Dictionary<string, Part>(StringComparer.OrdinalIgnoreCase);
        var lineNo = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNo++;

            // A '#' begins a comment only at the start of a line or after whitespace.
            //
            // IT IS ALSO THE SHARP SIGN. Stripping from the first '#' anywhere turned
            // `clav: . . F#5 . . A5` into `clav: . . F`, which is not an error — it is a
            // row three steps long instead of sixteen, and the arrangement simply slid out
            // of time from there. It went unnoticed until the first cue in a sharp key was
            // written, and it was the grid test that caught it rather than anyone's ear.
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            // A part row: "bass: C2 - - . ..."
            var colon = line.IndexOf(':');
            if (colon > 0 && byName.TryGetValue(line[..colon].Trim(), out var part))
            {
                foreach (var token in line[(colon + 1)..]
                             .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    part.Steps.Add(token);
                continue;
            }

            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (words[0].ToLowerInvariant())
            {
                case "tempo":
                    score.Tempo = Num(words, 1, lineNo);
                    break;
                case "steps":
                    score.StepsPerBar = (int)Num(words, 1, lineNo);
                    break;
                case "swing":
                    score.Swing = Num(words, 1, lineNo);
                    break;
                case "loop":
                    score.Loop = words.Length > 1 && words[1] is "1" or "yes" or "true";
                    break;
                case "loop-from":
                    score.LoopFromStep = (int)Num(words, 1, lineNo);
                    break;
                case "space":
                    score.Space = words.Length > 1 && words[1].Equals("dry", StringComparison.OrdinalIgnoreCase)
                        ? EffectSettings.Dry
                        : EffectSettings.Room;
                    break;
                case "part":
                {
                    if (words.Length < 2) throw Bad(lineNo, "part needs a name");
                    var name = words[1];
                    var patchName = "keys";
                    var kit = false;
                    double gain = 1.0, pan = 0;

                    for (var i = 2; i < words.Length; i++)
                    {
                        var w = words[i];
                        if (w.Equals("kit", StringComparison.OrdinalIgnoreCase)) { kit = true; continue; }
                        var eq = w.IndexOf('=');
                        if (eq < 0) throw Bad(lineNo, $"don't understand '{w}'");
                        var key = w[..eq].ToLowerInvariant();
                        var value = w[(eq + 1)..];
                        switch (key)
                        {
                            case "patch": patchName = value; break;
                            case "gain": gain = double.Parse(value, CultureInfo.InvariantCulture); break;
                            case "pan": pan = double.Parse(value, CultureInfo.InvariantCulture); break;
                            default: throw Bad(lineNo, $"unknown part setting '{key}'");
                        }
                    }

                    if (!kit && !Palette.ByName.TryGetValue(patchName, out _))
                        throw Bad(lineNo, $"no patch called '{patchName}'");

                    var p = new Part
                    {
                        Name = name,
                        Patch = kit ? SynthPatch.Silent : Palette.ByName[patchName],
                        IsKit = kit,
                        Gain = gain,
                        Pan = pan,
                    };
                    score._parts.Add(p);
                    byName[name] = p;
                    break;
                }
                default:
                    throw Bad(lineNo, $"unknown line '{words[0]}'");
            }
        }

        return score;
    }

    /// <summary>
    /// Everything from a '#' that starts a line or follows whitespace. A '#' attached to a
    /// letter is a sharp, not a comment.
    /// </summary>
    private static string StripComment(string raw)
    {
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '#') continue;
            if (i == 0 || char.IsWhiteSpace(raw[i - 1])) return raw[..i];
        }
        return raw;
    }

    private static double Num(string[] words, int i, int lineNo)
    {
        if (words.Length <= i || !double.TryParse(words[i], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v))
            throw Bad(lineNo, $"{words[0]} needs a number");
        return v;
    }

    private static FormatException Bad(int line, string why) =>
        new($"score line {line}: {why}");

    // ------------------------------------------------------------------ note names

    private static readonly int[] Letters = [9, 11, 0, 2, 4, 5, 7];   // A B C D E F G

    /// <summary>
    /// "C4" is middle C, MIDI 60. Returns false rather than throwing so a kit part can try
    /// a drum name first.
    /// </summary>
    public static bool TryParseNote(string token, out double note)
    {
        note = 0;
        if (token.Length < 2) return false;

        var letter = char.ToUpperInvariant(token[0]);
        if (letter is < 'A' or > 'G') return false;

        var semis = Letters[letter - 'A'];
        var i = 1;
        while (i < token.Length && (token[i] == '#' || token[i] == 'b' || token[i] == 's'))
        {
            semis += token[i] == 'b' ? -1 : 1;
            i++;
        }

        if (i >= token.Length) return false;
        var sign = 1;
        if (token[i] == '-') { sign = -1; i++; }
        if (i >= token.Length || !char.IsDigit(token[i])) return false;

        var octave = 0;
        while (i < token.Length && char.IsDigit(token[i])) { octave = octave * 10 + (token[i] - '0'); i++; }
        if (i != token.Length) return false;

        note = (sign * octave + 1) * 12 + semis;
        return note is >= 0 and <= 127;
    }

    // ------------------------------------------------------------------ rendering

    /// <summary>
    /// Plays the score through <paramref name="synth"/> and returns interleaved stereo.
    ///
    /// <paramref name="tailSeconds"/> keeps rendering after the last step so release tails,
    /// the delay and the reverb ring out instead of being cut off.
    /// </summary>
    /// <param name="passes">
    /// How many times to play it. Two or more takes <see cref="LoopFromStep"/> each time
    /// round, which is the only way to HEAR whether a loop is seamless — the join is the
    /// thing that goes wrong, and it cannot be judged from a single pass.
    /// </param>
    public float[] Render(OriginalSynth synth, int sampleRate = OriginalSynth.SampleRate,
                          double tailSeconds = 2.5, int passes = 1)
    {
        ArgumentNullException.ThrowIfNull(synth);
        if (passes < 1) throw new ArgumentOutOfRangeException(nameof(passes));

        synth.Silence();
        synth.Reverb = Space;

        var stepSeconds = StepSeconds;

        // The played step order. One pass is 0..length; each extra pass appends the loop
        // section again, so a cue with an intro plays intro-body-body-body and not
        // intro-body-intro-body.
        var length = LengthInSteps;
        var from = Math.Clamp(LoopFromStep, 0, Math.Max(0, length - 1));
        var order = new List<int>(length + (passes - 1) * (length - from));
        for (var s = 0; s < length; s++) order.Add(s);
        for (var p = 1; p < passes; p++)
            for (var s = from; s < length; s++) order.Add(s);

        var totalSteps = order.Count;
        var totalFrames = (int)((totalSteps * stepSeconds + tailSeconds) * sampleRate);
        var output = new float[totalFrames * 2];

        // Every note-on and note-off, at its sample position.
        var events = new List<(int Frame, bool On, Score.Part Part, SynthPatch Patch,
                               double Note, double Velocity)>();
        var sounding = new Dictionary<(string Part, double Note), int>();

        foreach (var part in _parts)
        {
            var open = new List<double>();

            for (var step = 0; step < order.Count; step++)
            {
                var source = order[step];
                if (source >= part.Steps.Count) continue;

                var token = part.Steps[source];
                if (token == "-") continue;

                var frame = StepFrame(step, stepSeconds, sampleRate);

                // Anything sounding on this part stops, unless the token held it.
                foreach (var n in open)
                    events.Add((frame, false, part, part.Patch, n, 0));
                open.Clear();

                if (token == ".") continue;

                foreach (var piece in token.Split('+', StringSplitOptions.RemoveEmptyEntries))
                {
                    var velocity = 0.8;
                    var body = piece;
                    var colon = piece.IndexOf(':');
                    if (colon > 0)
                    {
                        body = piece[..colon];
                        if (int.TryParse(piece[(colon + 1)..], out var v))
                            velocity = Math.Clamp(v / 127.0, 0, 1);
                    }

                    if (part.IsKit)
                    {
                        if (!Palette.Kit.TryGetValue(body, out var drum))
                            throw new FormatException($"'{body}' is not a drum in part {part.Name}");
                        events.Add((frame, true, part, drum.Patch, drum.Note, velocity));
                        // Drums are one-shots: their envelope ends them, no note-off.
                        continue;
                    }

                    if (!TryParseNote(body, out var note))
                        throw new FormatException($"'{body}' is not a note in part {part.Name}");

                    events.Add((frame, true, part, part.Patch, note, velocity));
                    open.Add(note);
                }
            }

            // Release whatever is still held at the end of the last pass.
            var last = StepFrame(order.Count, stepSeconds, sampleRate);
            foreach (var n in open) events.Add((last, false, part, part.Patch, n, 0));
        }

        events.Sort((x, y) =>
        {
            var c = x.Frame.CompareTo(y.Frame);
            // Note-offs before note-ons at the same instant, so a repeated note retriggers
            // rather than stacking a second voice on top of itself.
            return c != 0 ? c : x.On.CompareTo(y.On);
        });

        var cursor = 0;
        var next = 0;
        while (cursor < totalFrames)
        {
            while (next < events.Count && events[next].Frame <= cursor)
            {
                var e = events[next++];
                if (e.On)
                {
                    var id = synth.NoteOn(e.Patch, e.Note,
                        Math.Clamp(e.Velocity * e.Part.Gain, 0, 1), e.Part.Pan);
                    sounding[(e.Part.Name, e.Note)] = id;
                }
                else if (sounding.Remove((e.Part.Name, e.Note), out var id))
                {
                    synth.NoteOff(id);
                }
            }

            var until = next < events.Count ? Math.Min(events[next].Frame, totalFrames) : totalFrames;
            var chunk = Math.Max(1, until - cursor);
            synth.RenderStereo(output.AsSpan(cursor * 2, chunk * 2));
            cursor += chunk;
        }

        return output;
    }

    private int StepFrame(int step, double stepSeconds, int sampleRate)
    {
        // Swing pushes every other step later. It is the single biggest difference between
        // music that sounds programmed and music that sounds played.
        var offset = step % 2 == 1 ? Swing * stepSeconds : 0;
        return (int)((step * stepSeconds + offset) * sampleRate);
    }

    // ------------------------------------------------------------------ WAV out

    /// <summary>
    /// 16-bit stereo RIFF/WAVE. <see cref="WavFile"/> writes mono, which is what the game's
    /// audio seam takes; this exists so a cue can be auditioned in the stereo it was
    /// written in, without changing that file.
    /// </summary>
    public static byte[] BuildStereoWav(ReadOnlySpan<float> interleaved, int sampleRate)
    {
        var frames = interleaved.Length / 2;
        var dataBytes = frames * 4;
        var buffer = new byte[44 + dataBytes];
        using var bw = new BinaryWriter(new MemoryStream(buffer));

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataBytes);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)2);
        bw.Write(sampleRate);
        bw.Write(sampleRate * 4);
        bw.Write((short)4);
        bw.Write((short)16);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);

        for (var i = 0; i < interleaved.Length; i++)
            bw.Write((short)Math.Clamp(Math.Round(interleaved[i] * 32767.0), -32768, 32767));

        return buffer;
    }
}
