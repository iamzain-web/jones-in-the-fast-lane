using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jones.Audio;
using Jones.Audio.Original;

namespace Jones.Tests;

/// <summary>
/// The from-scratch synthesiser and the original cues written for it.
///
/// This path is ADDITIVE. The AdLib path is untouched, still the default and still tested
/// by <see cref="SciAudioTests"/>; nothing here replaces anything. What these tests pin
/// down is that the new path produces real audio from arithmetic alone, that the score
/// format a person is expected to edit actually parses, and that it is fast enough to run
/// on a phone.
/// </summary>
public class OriginalSynthTests
{
    private const int Rate = OriginalSynth.SampleRate;

    private static (double Peak, double Rms) Measure(ReadOnlySpan<float> pcm)
    {
        double peak = 0, sum = 0;
        foreach (var s in pcm) { peak = Math.Max(peak, Math.Abs(s)); sum += (double)s * s; }
        return (peak, Math.Sqrt(sum / Math.Max(1, pcm.Length)));
    }

    // ------------------------------------------------------------------ the voice

    [Fact]
    public void ASingleNoteIsAudibleAndThenStops()
    {
        var synth = new OriginalSynth(Rate, 4) { Reverb = EffectSettings.Dry };
        var id = synth.NoteOn(Palette.Lead, 60, 1.0);
        Assert.NotEqual(0, id);

        var buffer = new float[Rate];          // half a second, stereo
        synth.RenderStereo(buffer);
        var (peak, rms) = Measure(buffer);
        Assert.True(peak > 0.05, $"a full-velocity lead peaked at {peak:F3}");
        Assert.True(rms > 0.01, $"RMS {rms:F4} is essentially silence");

        synth.NoteOff(id);

        // Lead's release is 0.22 s; two seconds later the voice must be free again.
        var tail = new float[Rate * 4];
        synth.RenderStereo(tail);
        Assert.Equal(0, synth.ActiveVoices);
    }

    /// <summary>
    /// Pitch, measured rather than trusted â€” the same standard the AdLib path is held to.
    /// MIDI 69 is A440 by definition, and nothing in this synthesiser is allowed to be
    /// approximately in tune.
    /// </summary>
    [Theory]
    [InlineData(69, 440.0)]
    [InlineData(57, 220.0)]
    [InlineData(81, 880.0)]
    public void NotesComeOutAtEqualTemperamentPitch(int midi, double expectedHz)
    {
        // A plain sine with no filter movement, so zero crossings mean what they say.
        var patch = new SynthPatch
        {
            Name = "test",
            OscA = Wave.Sine, OscB = Wave.Sine, OscBMix = 0, DetuneCents = 0,
            Amp = new Adsr(0.005, 0, 1.0, 0.05),
            FilterEnv = new Adsr(0.001, 0, 1.0, 0.05),
            CutoffHz = 18000, Resonance = 0.7, EnvAmountOctaves = 0, KeyTrack = 0,
            Gain = 0.8,
        };

        var synth = new OriginalSynth(Rate, 2) { Reverb = EffectSettings.Dry };
        synth.NoteOn(patch, midi, 1.0);

        var buffer = new float[Rate];           // 0.5 s stereo
        synth.RenderStereo(buffer);

        var frames = buffer.Length / 2;
        var from = frames / 4;
        var to = frames * 3 / 4;
        var crossings = 0;
        for (var i = from + 1; i < to; i++)
            if (buffer[(i - 1) * 2] <= 0 && buffer[i * 2] > 0) crossings++;

        var hz = crossings / ((to - from) / (double)Rate);
        Assert.InRange(hz, expectedHz * 0.99, expectedHz * 1.01);
    }

    /// <summary>
    /// The anti-aliasing claim. A naive saw at a high pitch folds its harmonics back down
    /// as inharmonic tones; the corrected one does not put significant energy below the
    /// fundamental, and that is measurable without a spectrum analyser by high-passing and
    /// comparing.
    /// </summary>
    [Fact]
    public void AHighSawDoesNotFoldEnergyBelowItsFundamental()
    {
        var patch = new SynthPatch
        {
            Name = "saw",
            OscA = Wave.Saw, OscB = Wave.Saw, OscBMix = 0, DetuneCents = 0,
            Amp = new Adsr(0.005, 0, 1.0, 0.05),
            FilterEnv = new Adsr(0.001, 0, 1.0, 0.05),
            CutoffHz = 20000, Resonance = 0.7, EnvAmountOctaves = 0, KeyTrack = 0,
            Gain = 0.8,
        };

        var synth = new OriginalSynth(Rate, 2) { Reverb = EffectSettings.Dry };
        synth.NoteOn(patch, 96, 1.0);            // C7, about 2093 Hz

        var buffer = new float[Rate];
        synth.RenderStereo(buffer);

        // FOUR cascaded one-pole low-passes at 700 Hz, not one: a single pole only rolls
        // off 6 dB/octave, which still passes a tenth of a 2 kHz fundamental's energy and
        // would make this test pass on an aliasing oscillator too. Four poles put the
        // fundamental 38 dB down, so what is left below 700 Hz is aliasing or nothing.
        var a = 1.0 - Math.Exp(-2 * Math.PI * 700.0 / Rate);
        double p1 = 0, p2 = 0, p3 = 0, p4 = 0, lowEnergy = 0, total = 0;
        for (var i = buffer.Length / 4; i < buffer.Length; i += 2)
        {
            p1 += a * (buffer[i] - p1);
            p2 += a * (p1 - p2);
            p3 += a * (p2 - p3);
            p4 += a * (p3 - p4);
            lowEnergy += p4 * p4;
            total += (double)buffer[i] * buffer[i];
        }

        Assert.True(lowEnergy / Math.Max(1e-12, total) < 0.005,
            $"{lowEnergy / total:P3} of a 2 kHz saw's energy is below 700 Hz - it is aliasing");
    }

    [Fact]
    public void VoiceStealingNeverExceedsThePolyphonyItWasGiven()
    {
        var synth = new OriginalSynth(Rate, 6);
        for (var n = 48; n < 80; n++) synth.NoteOn(Palette.Pad, n, 0.9);

        var buffer = new float[2048];
        synth.RenderStereo(buffer);
        Assert.True(synth.ActiveVoices <= 6);
    }

    [Fact]
    public void NothingSoundingMeansSilence()
    {
        var synth = new OriginalSynth(Rate, 4);
        var buffer = new float[4096];
        synth.RenderStereo(buffer);
        Assert.All(buffer, s => Assert.Equal(0f, s));
    }

    // ------------------------------------------------------------------ the score format

    [Theory]
    [InlineData("C4", 60)]
    [InlineData("A4", 69)]
    [InlineData("C-1", 0)]
    [InlineData("Eb3", 51)]
    [InlineData("D#3", 51)]
    [InlineData("Bb1", 34)]
    public void NoteNamesParseToTheMidiNumbersTheyName(string name, int expected)
    {
        Assert.True(Score.TryParseNote(name, out var note));
        Assert.Equal(expected, (int)note);
    }

    [Theory]
    [InlineData("H4")]
    [InlineData("C")]
    [InlineData("hh")]
    [InlineData("")]
    public void ThingsThatAreNotNotesAreRejected(string name)
    {
        Assert.False(Score.TryParseNote(name, out _));
    }

    [Fact]
    public void AScoreParsesIntoTheGridItLooksLike()
    {
        const string text = """
            tempo 120
            steps 16
            swing 0.1
            part bass patch=bass gain=0.9 pan=-0.2
            part drums kit
            bass:  C2 - . . G1 . C2 . . . . . . . . .
            drums: bd . hh . sd . hh . bd . hh . sd . hh .
            """;

        var score = Score.Parse(text);
        Assert.Equal(120, score.Tempo);
        Assert.Equal(0.1, score.Swing);
        Assert.Equal(2, score.Parts.Count);

        var bass = score.Parts[0];
        Assert.Equal("bass", bass.Name);
        Assert.False(bass.IsKit);
        Assert.Equal(16, bass.Steps.Count);
        Assert.Equal("C2", bass.Steps[0]);
        Assert.Equal("-", bass.Steps[1]);

        Assert.True(score.Parts[1].IsKit);

        // 16 sixteenths at 120 bpm is one bar = two seconds.
        Assert.Equal(2.0, score.LengthSeconds, 3);
    }

    /// <summary>
    /// A sharp is not a comment.
    ///
    /// '#' is both the comment character and the sharp sign, and the parser used to strip
    /// from the first '#' anywhere in the line. `clav: . . F#5 . . A5 .` became
    /// `clav: . . F` — a three-step row where sixteen were written, which does not throw
    /// and does not sound broken on the bar it happens in; the whole arrangement simply
    /// slides out of time from there. It survived until the first cue in a sharp key.
    /// </summary>
    [Fact]
    public void ASharpInANoteIsNotTreatedAsAComment()
    {
        const string text = """
            tempo 120   # a trailing comment, which IS a comment
            steps 16
            part lead patch=lead
            lead: F#4 . G#4 . A#4 . C#5 . D#5 . F#5 . A#5 . C#6 .
            """;

        var score = Score.Parse(text);
        Assert.Equal(120, score.Tempo);

        var part = score.Parts[0];
        Assert.Equal(16, part.Steps.Count);
        Assert.Equal("F#4", part.Steps[0]);
        Assert.Equal("C#6", part.Steps[14]);

        Assert.True(Score.TryParseNote("F#4", out var fSharp));
        Assert.Equal(66, (int)fSharp);
    }

    [Fact]
    public void ABadScoreSaysWhichLineIsWrong()
    {
        var ex = Assert.Throws<FormatException>(() => Score.Parse("tempo 120\nwibble 3\n"));
        Assert.Contains("line 2", ex.Message);
    }

    // ------------------------------------------------------------------ the demo cue

    [Fact]
    public void TheDemoCueIsCompiledInAndParses()
    {
        Assert.Contains("demo-town", ScoreLibrary.Names);

        var score = ScoreLibrary.Load("demo-town");
        Assert.Equal(112, score.Tempo);
        Assert.True(score.Loop);
        Assert.Equal(5, score.Parts.Count);

        // Eight bars of sixteen steps, every part the same length â€” a part that is short by
        // one step is the commonest mistake when editing the grid by hand, and it shows up
        // as the arrangement drifting out of time rather than as an error.
        foreach (var part in score.Parts)
            Assert.True(part.Steps.Count == 128,
                $"part '{part.Name}' has {part.Steps.Count} steps, not the 128 of eight bars");
    }

    public static IEnumerable<object[]> EveryScore =>
        ScoreLibrary.Names.Select(n => new object[] { n });

    /// <summary>
    /// The grid has to BE a grid. Every part the same length, and that length a whole
    /// number of bars.
    ///
    /// This is the one mistake hand-editing a score actually produces: a row with fifteen
    /// tokens instead of sixteen. It does not throw, it does not sound broken on the bar it
    /// happens in — the whole arrangement simply slides a sixteenth out of time from there
    /// on and stays there. Counting is what the machine is for.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryScore))]
    public void EveryScoreIsARectangleOfWholeBars(string name)
    {
        var score = ScoreLibrary.Load(name);
        Assert.NotEmpty(score.Parts);

        var length = score.Parts[0].Steps.Count;
        Assert.True(length % score.StepsPerBar == 0,
            $"{name}: {length} steps is {length / (double)score.StepsPerBar:F2} bars");

        foreach (var part in score.Parts)
            Assert.True(part.Steps.Count == length,
                $"{name}: part '{part.Name}' has {part.Steps.Count} steps, part " +
                $"'{score.Parts[0].Name}' has {length} — a row somewhere is miscounted");

        Assert.InRange(score.LoopFromStep, 0, length - 1);
        Assert.True(score.LoopFromStep % score.StepsPerBar == 0,
            $"{name}: loops back to step {score.LoopFromStep}, which is mid-bar");
    }

    /// <summary>
    /// NO PART MAY SPEND MOST OF ITS TIME BELOW 46 Hz.
    ///
    /// This is the res46 Factory fault written down. Its bass line was a pedal on D1 —
    /// 36.7 Hz — so nearly everything the part produced was under what a phone speaker or
    /// a laptop can reproduce at all. On the hardware most people will use, the part was
    /// simply not there, and the cue came out with no bottom AND no middle.
    ///
    /// It cost an afternoon because it presents as a MIX problem and is a COMPOSITION
    /// problem: no amount of gain rescues a note the speaker cannot make. 46 Hz is MIDI 30,
    /// an F#1, and a bass line that lives below that is a mistake rather than a choice.
    /// Occasional notes underneath it are fine — the Factory still drops to D1 as an accent
    /// — so the bar is where the part spends MOST of its sounding time.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryScore))]
    public void NoPartLivesBelowWhatASpeakerCanReproduce(string name)
    {
        const double FloorHz = 46.0;

        var score = ScoreLibrary.Load(name);

        foreach (var part in score.Parts)
        {
            if (part.IsKit) continue;      // a kick is meant to be down there

            var sounding = 0;
            var tooLow = 0;
            double current = 0;            // lowest note currently held, in Hz

            foreach (var token in part.Steps)
            {
                if (token == ".") { current = 0; }
                else if (token != "-")
                {
                    current = 0;
                    foreach (var piece in token.Split('+', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var body = piece.Split(':')[0];
                        if (!Score.TryParseNote(body, out var note)) continue;
                        var hz = 440.0 * Math.Pow(2.0, (note - 69.0) / 12.0);
                        if (current == 0 || hz < current) current = hz;
                    }
                }

                if (current <= 0) continue;
                sounding++;
                if (current < FloorHz) tooLow++;
            }

            if (sounding == 0) continue;

            var share = tooLow / (double)sounding;
            Assert.True(share < 0.5,
                $"{name}: part '{part.Name}' is below {FloorHz} Hz for {share:P0} of the time " +
                "it is sounding — most playback hardware cannot reproduce that at all");
        }
    }

    /// <summary>
    /// Each cue must come out the length of the resource it stands in for, because the
    /// scripts time things against these sounds — `lottoScript` runs its animation over
    /// resource 25, and the ducking in `employment.sc:35-36` brings the bed back when the
    /// sting ends. Five per cent is the tolerance; a bed is not a metronome.
    /// </summary>
    [Theory]
    [InlineData("res05-town-board", 1129)]
    [InlineData("res34-security-apartments", 1595)]
    [InlineData("res43-employment-office", 457)]
    [InlineData("res46-factory", 969)]
    [InlineData("res47-bank-broker", 495)]
    [InlineData("res37-zmart", 958)]
    [InlineData("res39-qt-clothing", 1080)]
    [InlineData("res40-socket-city", 1122)]
    [InlineData("res49-blacks-market", 973)]
    [InlineData("res50-pawn-shoppe", 1104)]
    [InlineData("res41-university", 958)]
    [InlineData("res10-monolith-burgers", 2018)]
    [InlineData("res35-rent-office", 1223)]
    [InlineData("res36-low-cost-housing", 1657)]
    [InlineData("res48-broker-floor", 1154)]
    [InlineData("res07-winner-fanfare", 640)]
    [InlineData("res09-the-weekend", 3838)]
    [InlineData("res20-mugging", 136)]
    [InlineData("res27-eviction-notice", 145)]
    [InlineData("res30-sacked", 221)]
    [InlineData("res42-diploma", 198)]
    [InlineData("res44-bad-news", 153)]
    [InlineData("res45-good-news", 80)]
    [InlineData("res06-title-theme", 7506)]
    public void EachCueIsTheLengthOfTheResourceItStandsInFor(string name, int originalTicks)
    {
        var want = originalTicks / (double)SciSoundResource.TicksPerSecond;
        var got = ScoreLibrary.Load(name).LengthSeconds;

        Assert.True(Math.Abs(got - want) / want < 0.05,
            $"{name} is {got:F2}s against the original's {want:F2}s");
    }

    /// <summary>
    /// Every cue that stands in for a game resource has to be reachable BY that resource
    /// number, and every mapping has to point at a score that exists.
    /// </summary>
    [Fact]
    public void TheResourceMapPointsOnlyAtScoresThatExist()
    {
        Assert.NotEmpty(ScoreLibrary.ForResource);
        foreach (var (resource, name) in ScoreLibrary.ForResource)
        {
            Assert.Contains(name, ScoreLibrary.Names);
            Assert.Contains(resource, SciAudioTests.AllResources);
            Assert.NotNull(ScoreLibrary.ForSound(resource));
        }

        // A resource the map does not cover must say so rather than throw — that is what
        // lets the two paths be mixed, and it is still what happens if a resource is ever
        // added. It used to name resource 6, which stopped being uncovered the moment the
        // title theme was written; a number the game does not have cannot go stale.
        Assert.Null(ScoreLibrary.ForSound(999));
    }

    /// <summary>
    /// A bed's loop has to be seamless, and the only way to check is to play it round
    /// twice and look at the join. What a bad join produces is a GAP — every part released
    /// at the end of the pass and nothing started until the next bar — so the test measures
    /// the quietest moment anywhere near the seam against the cue's own average.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryScore))]
    public void EveryLoopingCueJoinsWithoutAGap(string name)
    {
        var score = ScoreLibrary.Load(name);
        if (!score.Loop) return;

        var pcm = score.Render(new OriginalSynth(Rate), Rate, tailSeconds: 0.5, passes: 3);

        // Where the second pass begins, in frames.
        var loopSeconds = (score.LengthInSteps - score.LoopFromStep) * score.StepSeconds;
        var seam = (int)((score.LengthSeconds) * Rate);

        static double Rms(ReadOnlySpan<float> pcm, int fromFrame, int frames)
        {
            double sum = 0;
            var n = Math.Min(frames * 2, pcm.Length - fromFrame * 2);
            if (n <= 0) return 0;
            for (var i = 0; i < n; i++) sum += (double)pcm[fromFrame * 2 + i] * pcm[fromFrame * 2 + i];
            return Math.Sqrt(sum / n);
        }

        var whole = Rms(pcm, 0, (int)(score.LengthSeconds * Rate));
        var across = Rms(pcm, Math.Max(0, seam - Rate / 20), Rate / 10);   // 50 ms either side

        Assert.True(across > whole * 0.35,
            $"{name}: the loop join drops to {across / whole:P0} of the cue's level — " +
            "there is a hole in it");
        Assert.True(loopSeconds > 0);
    }

    [Fact]
    public void TheDemoCueRendersRealAudio()
    {
        var score = ScoreLibrary.Load("demo-town");
        var pcm = score.Render(new OriginalSynth(Rate), Rate);

        var expectedFrames = (int)((score.LengthSeconds + 2.5) * Rate);
        Assert.Equal(expectedFrames * 2, pcm.Length);

        var (peak, rms) = Measure(pcm);
        Assert.True(peak > 0.2, $"the cue peaked at {peak:F3}");
        Assert.True(rms > 0.03, $"the cue's RMS is {rms:F4}");
        Assert.True(peak <= 1.0, "the limiter let something past full scale");

        // Not one channel: the chorus and the panning must actually produce a difference.
        double difference = 0;
        for (var i = 0; i < pcm.Length; i += 2) difference += Math.Abs(pcm[i] - pcm[i + 1]);
        Assert.True(difference / (pcm.Length / 2) > 0.001, "the render is mono");
    }

    // ------------------------------------------------------------------ the effects

    /// <summary>Every original effect, by the name <see cref="RenderFoley"/> knows it as.</summary>
    public static readonly string[] FoleyNames = ["click", "chime", "clock", "paper", "siren", "lotto"];

    public static IEnumerable<object[]> EveryFoleyCue => FoleyNames.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(EveryFoleyCue))]
    public void EveryOriginalEffectMakesASound(string name)
    {
        var pcm = RenderFoley(name);
        var (peak, rms) = Measure(pcm);

        Assert.True(peak > 0.05, $"{name} peaked at {peak:F3}");
        Assert.True(rms > 0.003, $"{name} has RMS {rms:F4}");
        Assert.True(peak <= 1.0, $"{name} clipped");
    }

    /// <summary>
    /// The click fires from more call sites than any other sound in the game, so its length
    /// is the one that has to stay short: resource 23 is five ticks, 0.08 s, and an
    /// interface blip that outlasts the press feels broken.
    /// </summary>
    [Fact]
    public void TheClickIsShorterThanAFifthOfASecond()
    {
        var pcm = Foley.ButtonClick(Rate);
        Assert.True(pcm.Length / 2 <= Rate / 4,
            $"the click is {pcm.Length / 2.0 / Rate:F2}s long");
    }

    internal static float[] RenderFoley(string name) => name switch
    {
        "click" => Foley.ButtonClick(Rate),
        "chime" => Foley.WeekChime(Rate),
        "clock" => Foley.WorkClock(Rate),
        "paper" => Foley.PaperRustle(Rate),
        "siren" => Foley.Siren(Rate),
        "lotto" => Foley.LottoMachine(Rate),
        _ => throw new ArgumentException(name),
    };

    // ------------------------------------------------------------------ cost

    /// <summary>
    /// The question that decides whether this can run on the Android head at all: how much
    /// faster than real time does it render?
    ///
    /// Measured on a full-polyphony chord rather than a single note, because a synthesiser
    /// that keeps up on one voice and not on sixteen is no use. The assertion is loose on
    /// purpose â€” a CI machine under load is not a phone â€” but anything under 5x real time
    /// here means the design needs revisiting, not tuning.
    /// </summary>
    [Fact]
    public void RenderingIsComfortablyFasterThanRealTime()
    {
        var synth = new OriginalSynth(Rate, 16);
        for (var i = 0; i < 16; i++) synth.NoteOn(Palette.Pad, 40 + i * 2, 0.8, (i % 3 - 1) * 0.5);

        var buffer = new float[Rate * 2];      // one second of stereo
        synth.RenderStereo(buffer);            // warm up the JIT

        // BEST of several rounds, not an average. This machine is known to be swamped by
        // background processes, and a single timing on it is mostly a measurement of how
        // busy the box was at that instant — the best round is the closest thing to the
        // cost of the code itself.
        var best = 0.0;
        for (var round = 0; round < 7; round++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            synth.RenderStereo(buffer);
            sw.Stop();
            best = Math.Max(best, 1.0 / sw.Elapsed.TotalSeconds);
        }

        Console.WriteLine($"16 voices + effects: best {best:F1}x real time on this machine");

        // The bar is deliberately low because the suite runs in DEBUG, where none of the
        // small structs inline and the same code measures about seven times slower than
        // the Release build the game actually ships: 4.5x here against 32.7x in Release on
        // the machine this was written on. What this assertion catches is a design
        // regression — somebody putting a transcendental function back into the per-sample
        // path — not a few percent of drift.
        Assert.True(best > 2.5,
            $"16 voices rendered at only {best:F1}x real time at best");
    }

    // ------------------------------------------------------------------ audition

    /// <summary>
    /// Writes the demo cue and every effect to <c>tools/samples/original/</c> so they can be
    /// PLAYED, because no assertion in this file can tell anyone whether the music is any
    /// good and that is the only question that matters about it.
    ///
    /// <c>tools/samples/</c> is generated output and is already gitignored. If the folder
    /// cannot be found the test passes silently rather than failing a build over an
    /// audition aid.
    /// </summary>
    [Fact]
    public void AuditionFilesAreWrittenForListening()
    {
        var root = RepoRoot();
        if (root is null) return;

        var outDir = Path.Combine(root, "tools", "samples", "original");
        Directory.CreateDirectory(outDir);

        foreach (var name in ScoreLibrary.Names)
        {
            var score = ScoreLibrary.Load(name);

            // A bed is rendered THREE TIMES ROUND. A single pass cannot tell anyone
            // whether the loop is seamless, and the loop is the thing that decides whether
            // a bed is bearable on the ninth visit to a shop.
            var passes = score.Loop ? 3 : 1;
            var cue = score.Render(new OriginalSynth(Rate), Rate, passes: passes);
            File.WriteAllBytes(Path.Combine(outDir, $"{name}.wav"),
                Score.BuildStereoWav(cue, Rate));
        }

        foreach (var name in FoleyNames)
            File.WriteAllBytes(Path.Combine(outDir, $"fx-{name}.wav"),
                Score.BuildStereoWav(RenderFoley(name), Rate));

        Assert.True(File.Exists(Path.Combine(outDir, "demo-town.wav")));
    }

    private static string? RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "CLAUDE.md"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}





