using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jones.Audio;
using Jones.Audio.Original;

namespace Jones.Tests;

/// <summary>
/// Spectrum measurement for the original cues.
///
/// WHY THIS EXISTS: the first five location beds came back judged "horrible and tinny"
/// while the demo cue they share a synthesiser with was liked. "Tinny" is a specific
/// complaint â€” thin, no body, too much upper mid â€” and it is measurable, so it gets
/// measured rather than guessed at. Everything else in this port is settled by reading the
/// bytes; there is no reason for the audio to be settled by opinion.
///
/// The band split is the one the complaint implies:
///   BODY    below 200 Hz   â€” the bass fundamental and the kick
///   LOWMID  200-800 Hz     â€” where a chord's weight sits
///   MID     800-2000 Hz
///   PRESENCE 2-6 kHz       â€” where "tinny" lives
///   AIR     above 6 kHz    â€” hats and cymbals
/// </summary>
public class AudioSpectrumTests
{
    private const int Rate = OriginalSynth.SampleRate;

    // ------------------------------------------------------------------ FFT

    /// <summary>In-place iterative radix-2 FFT. Length must be a power of two.</summary>
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wRe = Math.Cos(ang);
            var wIm = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var uRe = re[i + k];
                    var uIm = im[i + k];
                    var vRe = re[i + k + len / 2] * curRe - im[i + k + len / 2] * curIm;
                    var vIm = re[i + k + len / 2] * curIm + im[i + k + len / 2] * curRe;
                    re[i + k] = uRe + vRe;
                    im[i + k] = uIm + vIm;
                    re[i + k + len / 2] = uRe - vRe;
                    im[i + k + len / 2] = uIm - vIm;
                    var nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }

    /// <param name="Loudness">
    /// RMS over the frames actually analysed, i.e. ignoring silence.
    ///
    /// NOT the RMS of the whole file, and the difference matters as soon as short cues are
    /// measured: Good News is 1.33 seconds rendered with a 2.5-second tail, so two thirds
    /// of the file is silence and a whole-file RMS reads it nine decibels quieter than it
    /// actually sounds. Every absolute band figure is referenced to this instead.
    /// </param>
    private sealed record Bands(double Body, double LowMid, double Mid, double Presence,
                                double Air, double Total, double Loudness)
    {
        /// <summary>Presence against body, in dB. The higher this is, the tinnier.</summary>
        public double TinnyDb => 10 * Math.Log10(Math.Max(1e-20, Presence) / Math.Max(1e-20, Body));

        public double Db(double band) => 10 * Math.Log10(Math.Max(1e-20, band) / Math.Max(1e-20, Total));

        public override string ToString() =>
            $"body {Db(Body),6:F1}  lowmid {Db(LowMid),6:F1}  mid {Db(Mid),6:F1}  " +
            $"pres {Db(Presence),6:F1}  air {Db(Air),6:F1}  |  presence-vs-body {TinnyDb,6:F1} dB";
    }

    /// <summary>
    /// Averaged power spectrum of a mono signal, summed into the five bands. Hann window,
    /// 8192 points, 50% overlap, skipping frames that are essentially silent so a cue with
    /// gaps is not measured as if the gaps were part of its tone.
    /// </summary>
    private static Bands Analyse(double[] mono)
    {
        const int N = 8192;
        var window = new double[N];
        for (var i = 0; i < N; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1));

        var power = new double[N / 2];
        var frames = 0;
        double liveEnergy = 0;

        for (var start = 0; start + N <= mono.Length; start += N / 2)
        {
            double energy = 0;
            for (var i = 0; i < N; i++) energy += mono[start + i] * mono[start + i];
            if (Math.Sqrt(energy / N) < 1e-4) continue;          // silence
            liveEnergy += energy;

            var re = new double[N];
            var im = new double[N];
            for (var i = 0; i < N; i++) re[i] = mono[start + i] * window[i];

            Fft(re, im);
            for (var k = 0; k < N / 2; k++) power[k] += re[k] * re[k] + im[k] * im[k];
            frames++;
        }

        if (frames == 0) return new Bands(0, 0, 0, 0, 0, 1, 0);

        double body = 0, lowMid = 0, mid = 0, presence = 0, air = 0, total = 0;
        for (var k = 1; k < N / 2; k++)
        {
            var hz = k * (double)Rate / N;
            var p = power[k] / frames;
            total += p;
            if (hz < 200) body += p;
            else if (hz < 800) lowMid += p;
            else if (hz < 2000) mid += p;
            else if (hz < 6000) presence += p;
            else air += p;
        }

        return new Bands(body, lowMid, mid, presence, air, total,
            Math.Sqrt(liveEnergy / (frames * (double)N)));
    }

    private static double[] MonoFromWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var channels = BitConverter.ToInt16(bytes, 22);
        var samples = (bytes.Length - 44) / 2;
        var frames = samples / channels;
        var mono = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++)
                sum += BitConverter.ToInt16(bytes, 44 + (f * channels + c) * 2) / 32768.0;
            mono[f] = sum / channels;
        }
        return mono;
    }

    private static double[] MonoFromStereo(float[] interleaved)
    {
        var mono = new double[interleaved.Length / 2];
        for (var i = 0; i < mono.Length; i++)
            mono[i] = (interleaved[i * 2] + interleaved[i * 2 + 1]) * 0.5;
        return mono;
    }

    private static double Rms(double[] x)
    {
        double sum = 0;
        foreach (var s in x) sum += s * s;
        return Math.Sqrt(sum / Math.Max(1, x.Length));
    }

    private static double Dbfs(double rms) => 20 * Math.Log10(Math.Max(1e-12, rms));

    private static string? SampleDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "CLAUDE.md")))
                return Path.Combine(dir, "tools", "samples", "original");
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    // ------------------------------------------------------------------ 1. the cues

    /// <summary>
    /// THE HEADLINE MEASUREMENT: every cue's band balance against the demo's.
    ///
    /// Prints rather than asserts, because the first question is what the numbers ARE. The
    /// assertion that follows from them lives in
    /// <see cref="NoBedIsThinnerThanTheDemoItWasWrittenBeside"/>.
    /// </summary>
    [Fact]
    public void ReportTheSpectrumOfEveryRenderedCue()
    {
        var dir = SampleDir();
        if (dir is null || !Directory.Exists(dir)) return;

        var report = new StringBuilder();
        report.AppendLine();
        report.AppendLine("cue                              rms      " +
                          "band energy relative to the cue's own total (dB)");

        foreach (var path in Directory.GetFiles(dir, "*.wav").OrderBy(p => p))
        {
            var mono = MonoFromWav(path);
            var bands = Analyse(mono);
            report.AppendLine($"{Path.GetFileNameWithoutExtension(path),-30} " +
                              $"{Dbfs(Rms(mono)),6:F1}  {bands}");
        }

        Console.WriteLine(report.ToString());
    }

    /// <summary>
    /// THE REGRESSION GUARD, and the lesson from the first five beds written down as an
    /// assertion rather than as advice.
    ///
    /// A cue that is short of midrange sounds thin no matter how much bottom end it has.
    /// The first version of the town board had BETTER low end than the demo and was still
    /// rejected, because its 200-800 Hz was 3.5 dB down, its 800-2000 Hz 3.8 dB down and
    /// its 2-6 kHz 11.5 dB down: all bass, nothing above it, holes in between. So the test
    /// is on the bands ABOVE the bass, measured in absolute terms against the demo, which
    /// is the mix the user actually liked.
    ///
    /// THE LIST GROWS AS BEDS ARE REMIXED. Only cues that have been mixed to this standard
    /// are named here; adding one before it has been is how a test starts lying.
    /// </summary>
    [Theory]
    [InlineData("res05-town-board")]
    [InlineData("res34-security-apartments")]
    [InlineData("res43-employment-office")]
    [InlineData("res46-factory")]
    [InlineData("res47-bank-broker")]
    [InlineData("res37-zmart")]
    [InlineData("res39-qt-clothing")]
    [InlineData("res40-socket-city")]
    [InlineData("res49-blacks-market")]
    [InlineData("res50-pawn-shoppe")]
    [InlineData("res41-university")]
    [InlineData("res10-monolith-burgers")]
    [InlineData("res35-rent-office")]
    [InlineData("res36-low-cost-housing")]
    [InlineData("res48-broker-floor")]
    public void ARemixedCueHoldsUpAgainstTheDemoAboveTheBass(string name)
    {
        var dir = SampleDir();
        if (dir is null || !File.Exists(Path.Combine(dir, $"{name}.wav"))) return;

        var demo = MonoFromWav(Path.Combine(dir, "demo-town.wav"));
        var cue = MonoFromWav(Path.Combine(dir, $"{name}.wav"));

        var d = Analyse(demo);
        var c = Analyse(cue);

        // Absolute band level: the cue's overall level plus the band's share of it.
        static double Absolute(Bands b, double band) => Dbfs(b.Loudness) + b.Db(band);

        foreach (var (label, want, got) in new (string, double, double)[]
                 {
                     ("low mid (200-800 Hz)", Absolute(d, d.LowMid), Absolute(c, c.LowMid)),
                     ("mid (800-2000 Hz)", Absolute(d, d.Mid), Absolute(c, c.Mid)),
                     ("presence (2-6 kHz)", Absolute(d, d.Presence), Absolute(c, c.Presence)),
                 })
            Assert.True(got > want - 3.0,
                $"{name}: {label} is {got - want:F1} dB against the demo â€” " +
                "this is the shape that got the first five beds rejected");

        // ...and it must not have been fixed by simply making everything louder.
        Assert.InRange(Dbfs(c.Loudness), Dbfs(d.Loudness) - 3.0, Dbfs(d.Loudness) + 2.0);
    }

    // ------------------------------------------------------------------ 2. the parts

    /// <summary>
    /// Each part of a cue on its own, so it can be seen whether the pad and the bass are
    /// actually doing anything or are sitting under everything else.
    ///
    /// DUTY CYCLE is printed beside the level and is the number I most wanted: a bass that
    /// is only sounding a third of the time cannot hold the bottom up however loud it is
    /// while it plays.
    /// </summary>
    [Theory]
    [InlineData("demo-town")]
    [InlineData("res05-town-board")]
    public void ReportEachPartOfACueOnItsOwn(string name)
    {
        var score = ScoreLibrary.Load(name);
        var report = new StringBuilder();
        report.AppendLine();
        report.AppendLine($"--- {name}: parts in isolation ---");

        foreach (var part in score.Parts)
        {
            var solo = SoloRender(score, part.Name);
            var mono = MonoFromStereo(solo);
            var bands = Analyse(mono);

            // How much of the cue this part is actually sounding for.
            var steps = part.Steps.Count;
            var sounding = 0;
            var live = false;
            foreach (var token in part.Steps)
            {
                if (token == ".") live = false;
                else if (token != "-") live = true;
                if (live) sounding++;
            }

            report.AppendLine($"  {part.Name,-7} gain {part.Gain:F2}  " +
                              $"rms {Dbfs(Rms(mono)),6:F1}  sounding {sounding * 100.0 / steps,5:F1}%  {bands}");
        }

        var whole = MonoFromStereo(score.Render(new OriginalSynth(Rate), Rate, 1.0));
        report.AppendLine($"  {"ALL",-7}              rms {Dbfs(Rms(whole)),6:F1}                 {Analyse(whole)}");
        Console.WriteLine(report.ToString());
    }

    /// <summary>Renders a score with every part but one silenced.</summary>
    private static float[] SoloRender(Score score, string keep)
    {
        var text = new StringBuilder();
        // Rebuilding the score text is the only way to drop a part without adding a mute
        // flag to the format that nothing else would ever use.
        foreach (var part in score.Parts)
        {
            var kind = part.IsKit ? "kit" : $"patch={part.Patch.Name}";
            text.AppendLine($"part {part.Name} {kind} gain={part.Gain.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"pan={part.Pan.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        text.Insert(0, $"tempo {score.Tempo.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n" +
                       $"steps {score.StepsPerBar}\n" +
                       $"swing {score.Swing.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");

        foreach (var part in score.Parts)
        {
            var tokens = part.Name == keep ? part.Steps : part.Steps.Select(_ => ".");
            text.AppendLine($"{part.Name}: {string.Join(" ", tokens)}");
        }

        return Score.Parse(text.ToString()).Render(new OriginalSynth(Rate), Rate, 1.0);
    }

    // ------------------------------------------------------------------ 3. the game path

    /// <summary>
    /// THE PLAYBACK CHAIN, measured independently of the notes.
    ///
    /// The same cue rendered two ways: through <see cref="Score.Render"/>, which is what
    /// produced the WAVs, and through <see cref="JonesAudioMixer"/>, which is what the game
    /// actually plays. A rate mismatch or a bad resample anywhere in the second path would
    /// sound exactly like "tinny" and would have nothing to do with the music, so the two
    /// spectra have to agree.
    /// </summary>
    [Fact]
    public void TheGamePathAndTheFilePathProduceTheSameSpectrum()
    {
        var dir = SampleDir();

        var score = ScoreLibrary.Load("res05-town-board");
        var viaFile = MonoFromStereo(score.Render(new OriginalSynth(Rate), Rate, tailSeconds: 0.5));

        var mixer = new JonesAudioMixer(new OriginalSoundBank(Rate));
        mixer.PlayMusic(5, loop: true);

        var frames = viaFile.Length;
        var stereo = new float[frames * 2];
        var done = 0;
        while (done < frames)
        {
            var take = Math.Min(2048, frames - done);
            mixer.RenderStereo(stereo.AsSpan(done * 2, take * 2));
            done += take;
        }
        var viaGame = MonoFromStereo(stereo);

        var a = Analyse(viaFile);
        var b = Analyse(viaGame);

        Console.WriteLine();
        Console.WriteLine($"  file path  rms {Dbfs(Rms(viaFile)),6:F1}  {a}");
        Console.WriteLine($"  game path  rms {Dbfs(Rms(viaGame)),6:F1}  {b}");
        Console.WriteLine($"  sample rate: synth {OriginalSynth.SampleRate}, " +
                          $"mixer {JonesAudioMixer.SampleRate}");

        // Every stage must run at one rate. A resample would show as a shifted spectrum.
        Assert.Equal(OriginalSynth.SampleRate, JonesAudioMixer.SampleRate);

        foreach (var (name, x, y) in new (string, double, double)[]
                 {
                     ("body", a.Db(a.Body), b.Db(b.Body)),
                     ("lowmid", a.Db(a.LowMid), b.Db(b.LowMid)),
                     ("mid", a.Db(a.Mid), b.Db(b.Mid)),
                     ("presence", a.Db(a.Presence), b.Db(b.Presence)),
                     ("air", a.Db(a.Air), b.Db(b.Air)),
                 })
            Assert.True(Math.Abs(x - y) < 1.5,
                $"the game path's {name} band is {y - x:F1} dB off the file path's â€” " +
                "the playback chain is changing the sound, not the notes");
    }

    /// <summary>
    /// The mono fold-down, which is what a head that has not been widened receives. It must
    /// not lose the bottom end â€” summing two channels that are out of phase down low would
    /// thin the sound, and the chorus is exactly the kind of thing that could do it.
    /// </summary>
    [Fact]
    public void TheMonoFoldDownKeepsTheBody()
    {
        var score = ScoreLibrary.Load("res05-town-board");
        var stereo = score.Render(new OriginalSynth(Rate), Rate, tailSeconds: 0.5);

        var mono = MonoFromStereo(stereo);
        var left = new double[stereo.Length / 2];
        for (var i = 0; i < left.Length; i++) left[i] = stereo[i * 2];

        var m = Analyse(mono);
        var l = Analyse(left);

        Console.WriteLine();
        Console.WriteLine($"  left only  {l}");
        Console.WriteLine($"  folded     {m}");

        Assert.True(m.Db(m.Body) > l.Db(l.Body) - 2.0,
            "folding to mono lost more than 2 dB of body â€” something is out of phase");
    }
}



