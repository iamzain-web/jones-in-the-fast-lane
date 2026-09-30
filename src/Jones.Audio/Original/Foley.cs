namespace Jones.Audio.Original;

/// <summary>
/// The game's non-musical sounds, synthesised from nothing.
///
/// These come FIRST in the original-audio work because they are the cheap, unarguable
/// wins: a button click, a clock tick, a chime and a siren are sound design, not
/// composition. Nobody has to like a melody for them to be right, there is no question of
/// them resembling anyone else's material, and between them they cover most of what a
/// player actually hears in a session — the click alone fires from sixty-odd call sites
/// across the scripts.
///
/// Each one is named for the cue it replaces and carries the cue's own length, taken from
/// the original resource's tick count, so that anything timed against a sound in the
/// scripts still lines up.
/// </summary>
public static class Foley
{
    /// <summary>
    /// Resource 23, the universal button click — the single most-played sound in the game.
    /// Five ticks, 0.08 s.
    ///
    /// Two very short blips a fourth apart with the filter almost shut. Dry: an echo or a
    /// reverb tail on a sound that fires on every press is unbearable within a minute.
    /// </summary>
    public static float[] ButtonClick(int sampleRate = OriginalSynth.SampleRate)
    {
        var patch = new SynthPatch
        {
            Name = "click",
            OscA = Wave.Pulse, OscB = Wave.Pulse, PulseWidth = 0.3,
            OscBMix = 0.4, DetuneCents = 20,
            NoiseLevel = 0.25,
            Amp = new Adsr(0.0005, 0.035, 0.0, 0.02),
            FilterEnv = new Adsr(0.0005, 0.02, 0.0, 0.015),
            CutoffHz = 900, Resonance = 2.6, EnvAmountOctaves = 2.4, KeyTrack = 0.5,
            Gain = 0.55,
        };

        var synth = new OriginalSynth(sampleRate, 4) { Reverb = EffectSettings.Dry };
        return RenderEvents(synth, sampleRate, 0.09,
        [
            (0.000, patch, 84, 0.9),
            (0.028, patch, 89, 0.7),
        ], tail: 0.04);
    }

    /// <summary>
    /// Resource 29 — the week is over. `room1.sc:1500`, on the second effect slot because
    /// the button click that spent the last hour is still playing. 108 ticks, 1.8 s.
    /// </summary>
    public static float[] WeekChime(int sampleRate = OriginalSynth.SampleRate)
    {
        var synth = new OriginalSynth(sampleRate, 8)
        {
            Reverb = new EffectSettings(0.003, 0.25, 0.30, 0.25, 0.12, 0.22, 0.6),
        };

        return RenderEvents(synth, sampleRate, 2.4,
        [
            (0.00, Palette.Bell, 72, 0.9),
            (0.00, Palette.Bell, 79, 0.5),
            (0.22, Palette.Bell, 76, 0.8),
            (0.44, Palette.Bell, 84, 0.85),
        ]);
    }

    /// <summary>
    /// Resource 31 — the work clock counting the shift down. `WButton.sc:308`. 19 ticks,
    /// 0.32 s: two dry mechanical ticks, high and low.
    /// </summary>
    public static float[] WorkClock(int sampleRate = OriginalSynth.SampleRate)
    {
        var tick = new SynthPatch
        {
            Name = "tick",
            OscA = Wave.Square, OscB = Wave.Square, OscBMix = 0,
            NoiseLevel = 1.0,
            Amp = new Adsr(0.0004, 0.020, 0.0, 0.012),
            FilterEnv = new Adsr(0.0004, 0.012, 0.0, 0.01),
            CutoffHz = 2600, Resonance = 4.0, EnvAmountOctaves = 1.6, KeyTrack = 0,
            Gain = 0.45,
        };

        var synth = new OriginalSynth(sampleRate, 4) { Reverb = EffectSettings.Dry };
        return RenderEvents(synth, sampleRate, 0.4,
        [
            (0.00, tick, 72, 0.9),
            (0.16, tick with { CutoffHz = 1700 }, 66, 0.75),
        ]);
    }

    /// <summary>
    /// Resource 8 — the newspaper opening. `newspaper.sc:204`. 487 ticks, 8.1 s in the
    /// original, which is a long time to listen to paper; this is the first 1.6 s of rustle
    /// and the wiring can let the page turn finish under silence.
    ///
    /// Filtered noise with a moving band and an irregular envelope. Paper is broadband
    /// noise whose spectrum shifts as the sheet bends, and a static noise burst reads as
    /// "static" rather than "paper" — the movement is the whole effect.
    /// </summary>
    public static float[] PaperRustle(int sampleRate = OriginalSynth.SampleRate,
                                      double seconds = 1.6)
    {
        var frames = (int)(seconds * sampleRate);
        var output = new float[frames * 2];

        uint rng = 0x1F123BB5;
        double lp1 = 0, lp2 = 0, hp = 0, prev = 0;
        var phase = 0.0;

        for (var i = 0; i < frames; i++)
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            var n = (rng & 0xFFFFFF) / 8388608.0 - 1.0;

            // Three overlapping rustles at different rates, so the texture never repeats.
            var t = i / (double)sampleRate;
            var crinkle = 0.55 + 0.45 * Math.Sin(t * 11.3) * Math.Sin(t * 4.1 + 0.7);
            var envelope = Math.Min(1.0, t / 0.05) * Math.Exp(-1.1 * t) * crinkle;

            // A band-pass swept between roughly 1.4 and 4.5 kHz.
            phase += 1.0 / sampleRate;
            var cutoff = 1400 + 3100 * (0.5 + 0.5 * Math.Sin(phase * 3.7));
            var a = 1.0 - Math.Exp(-2 * Math.PI * cutoff / sampleRate);
            lp1 += a * (n - lp1);
            lp2 += a * (lp1 - lp2);
            hp = 0.97 * (hp + lp2 - prev);
            prev = lp2;

            var s = hp * envelope * 0.9;
            // Slight left/right difference so the sheet has some width.
            output[i * 2] = (float)(s * 1.0);
            output[i * 2 + 1] = (float)(s * 0.82 + lp2 * envelope * 0.15);
        }

        return output;
    }

    /// <summary>
    /// Resource 21 — the ambulance. `startTrn.sc:1078`, `moveAmbulance` state 0. 281 ticks,
    /// 4.7 s.
    ///
    /// A two-tone siren with a Doppler fall across the pass, which is what makes it read as
    /// a vehicle going by rather than an alarm sitting still.
    /// </summary>
    public static float[] Siren(int sampleRate = OriginalSynth.SampleRate, double seconds = 4.7)
    {
        var frames = (int)(seconds * sampleRate);
        var output = new float[frames * 2];

        double phase = 0, lp = 0;

        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)sampleRate;

            // Alternating high/low every 0.42 s, the European two-tone.
            var high = (int)(t / 0.42) % 2 == 0;
            var baseHz = high ? 660.0 : 495.0;

            // Doppler: approaching for the first half, receding for the second.
            var doppler = 1.06 - 0.12 * Math.Clamp(t / seconds, 0, 1);
            var hz = baseHz * doppler;

            phase += hz / sampleRate;
            if (phase >= 1) phase -= 1;

            // A soft square: the siren horn is not a pure tone.
            var square = phase < 0.5 ? 1.0 : -1.0;
            var sine = Math.Sin(phase * 2 * Math.PI);
            var raw = sine * 0.65 + square * 0.35;

            var a = 1.0 - Math.Exp(-2 * Math.PI * 2200 / sampleRate);
            lp += a * (raw - lp);

            // Level peaks as it passes.
            var pass = Math.Exp(-Math.Pow((t - seconds * 0.45) / (seconds * 0.40), 2));
            var fade = Math.Min(1.0, t / 0.15) * Math.Min(1.0, (seconds - t) / 0.3);
            var s = lp * pass * fade * 0.42;

            // Pan across the listener as it goes by.
            var pan = Math.Clamp(-0.8 + 1.6 * (t / seconds), -1, 1);
            var angle = (pan + 1) * 0.25 * Math.PI;
            output[i * 2] = (float)(s * Math.Cos(angle));
            output[i * 2 + 1] = (float)(s * Math.Sin(angle));
        }

        return output;
    }

    /// <summary>
    /// Resource 25 — the lotto machine, played looping at `lottoScript.sc:43`. 452 ticks,
    /// 7.5 s in the original.
    ///
    /// A rumbling blower with balls knocking about in it: low filtered noise for the air,
    /// and short resonant clacks at irregular intervals for the balls. The interval is
    /// driven by a counter rather than a random number so the sound is the same every run,
    /// which matters for a loop.
    /// </summary>
    public static float[] LottoMachine(int sampleRate = OriginalSynth.SampleRate,
                                       double seconds = 7.5)
    {
        var frames = (int)(seconds * sampleRate);
        var output = new float[frames * 2];

        uint rng = 0x2545F491;
        double lpA = 0, lpB = 0;

        // Ball strikes: a fixed, slightly uneven pattern.
        double[] gaps = [0.13, 0.09, 0.21, 0.07, 0.17, 0.11, 0.26, 0.08];
        var strikes = new List<double>();
        for (double t = 0.05, g = 0; t < seconds; g++) { strikes.Add(t); t += gaps[(int)g % gaps.Length]; }

        var clack = new SynthPatch
        {
            Name = "clack",
            OscA = Wave.Triangle, OscB = Wave.Triangle, OscBMix = 0,
            NoiseLevel = 0.8,
            Amp = new Adsr(0.0005, 0.035, 0.0, 0.02),
            FilterEnv = new Adsr(0.0005, 0.02, 0.0, 0.015),
            CutoffHz = 1300, Resonance = 5.0, EnvAmountOctaves = 1.8, KeyTrack = 0.4,
            PitchDropOctaves = 0.5, PitchDropSeconds = 0.02,
            Gain = 0.30,
        };

        var events = new List<(double, SynthPatch, double, double)>();
        for (var i = 0; i < strikes.Count; i++)
            events.Add((strikes[i], clack, 68 + (i % 5) * 2, 0.5 + (i % 3) * 0.15));

        var balls = RenderEvents(new OriginalSynth(sampleRate, 8)
        {
            Reverb = new EffectSettings(0, 0, 0, 0, 0, 0.10, 0.35),
        }, sampleRate, seconds, events, tail: 0);

        for (var i = 0; i < frames; i++)
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            var n = (rng & 0xFFFFFF) / 8388608.0 - 1.0;

            var a = 1.0 - Math.Exp(-2 * Math.PI * 320 / sampleRate);
            lpA += a * (n - lpA);
            lpB += a * (lpA - lpB);

            var t = i / (double)sampleRate;
            var fade = Math.Min(1.0, t / 0.25) * Math.Min(1.0, (seconds - t) / 0.35);
            var blower = lpB * 3.2 * fade * (0.85 + 0.15 * Math.Sin(t * 7.3));

            var l = blower * 0.9 + (i * 2 < balls.Length ? balls[i * 2] : 0);
            var r = blower * 0.9 + (i * 2 + 1 < balls.Length ? balls[i * 2 + 1] : 0);
            output[i * 2] = (float)Math.Clamp(l, -1, 1);
            output[i * 2 + 1] = (float)Math.Clamp(r, -1, 1);
        }

        return output;
    }

    // ------------------------------------------------------------------

    private static float[] RenderEvents(
        OriginalSynth synth, int sampleRate, double seconds,
        IReadOnlyList<(double At, SynthPatch Patch, double Note, double Velocity)> events,
        double tail = 0.3)
    {
        var frames = (int)((seconds + tail) * sampleRate);
        var output = new float[frames * 2];

        var ordered = events.OrderBy(e => e.At).ToList();
        var cursor = 0;
        var next = 0;

        while (cursor < frames)
        {
            while (next < ordered.Count && (int)(ordered[next].At * sampleRate) <= cursor)
            {
                var e = ordered[next++];
                synth.NoteOn(e.Patch, e.Note, e.Velocity);
            }

            var until = next < ordered.Count
                ? Math.Min((int)(ordered[next].At * sampleRate), frames)
                : frames;
            var chunk = Math.Max(1, until - cursor);
            synth.RenderStereo(output.AsSpan(cursor * 2, chunk * 2));
            cursor += chunk;
        }

        return output;
    }
}
