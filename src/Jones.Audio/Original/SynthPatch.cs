namespace Jones.Audio.Original;

/// <summary>
/// One instrument, as numbers. No sample data, no wavetable, no file: a patch IS this
/// record, and the sound is whatever <see cref="Voice"/> computes from it.
///
/// Values are deliberately plain units â€” hertz, seconds, cents, octaves â€” so that someone
/// changing a sound can reason about the change rather than nudging a magic constant.
/// </summary>
public sealed record SynthPatch
{
    public required string Name { get; init; }

    public Wave OscA { get; init; } = Wave.Saw;
    public Wave OscB { get; init; } = Wave.Saw;

    /// <summary>0 = only A, 1 = only B, 0.5 = an even blend.</summary>
    public double OscBMix { get; init; } = 0.5;

    /// <summary>B's interval against A. -12 for a sub-octave layer, +7 for a fifth.</summary>
    public double OscBSemitones { get; init; }

    /// <summary>
    /// Split either side of the played pitch. This single number is most of what makes a
    /// synth sound wide and alive rather than sterile; 8-18 cents is the useful range, and
    /// past about 30 it stops sounding detuned and starts sounding broken.
    /// </summary>
    public double DetuneCents { get; init; } = 10;

    /// <summary>B's starting phase, so two identical oscillators do not cancel.</summary>
    public double OscBPhase { get; init; } = 0.33;

    /// <summary>A sine an octave below, added straight in. Weight, for a bass.</summary>
    public double SubLevel { get; init; }

    /// <summary>White noise added before the filter â€” breath, or a whole drum.</summary>
    public double NoiseLevel { get; init; }

    /// <summary>Duty cycle for <see cref="Wave.Pulse"/>.</summary>
    public double PulseWidth { get; init; } = 0.5;

    public Adsr Amp { get; init; } = Adsr.Organ;
    public Adsr FilterEnv { get; init; } = Adsr.Pluck;

    public double CutoffHz { get; init; } = 1200;

    /// <summary>Q. 0.7 is flat, 4 is a clear peak, past 8 it whistles.</summary>
    public double Resonance { get; init; } = 1.0;

    /// <summary>How far the filter envelope opens the cutoff, in octaves.</summary>
    public double EnvAmountOctaves { get; init; } = 2.0;

    /// <summary>1 = the cutoff follows the note exactly; 0 = fixed. 0.4 is usual.</summary>
    public double KeyTrack { get; init; } = 0.4;

    public double VibratoHz { get; init; } = 5.2;
    public double VibratoDepthCents { get; init; }
    public double VibratoDelaySeconds { get; init; } = 0.4;

    /// <summary>
    /// Octaves the pitch BENDS AWAY BY over <see cref="PitchDropSeconds"/>. Starts at the
    /// played note and moves off it, which is what makes a drum a drum: the kick starts at
    /// 65 Hz and falls out of hearing in fifty milliseconds.
    /// </summary>
    public double PitchDropOctaves { get; init; }
    public double PitchDropSeconds { get; init; } = 0.06;

    /// <summary>
    /// Semitones the pitch STARTS OFF BY and settles from, over
    /// <see cref="PitchRiseSeconds"/>. The opposite shape to the drop above: it begins away
    /// from the played note and arrives at it.
    ///
    /// <para>
    /// This is what a brass player does. The first fifty milliseconds of a real trumpet
    /// note are flat while the lip finds the pitch, and a synth brass patch without it
    /// sounds like an organ no matter how the filter is set. Negative starts flat, which is
    /// the one anyone actually wants.
    /// </para>
    /// </summary>
    public double PitchRiseSemitones { get; init; }
    public double PitchRiseSeconds { get; init; } = 0.07;

    public double Gain { get; init; } = 0.5;

    public static readonly SynthPatch Silent = new()
    {
        Name = "silent",
        Gain = 0,
        Amp = new Adsr(0.001, 0.001, 0, 0.001),
    };
}

/// <summary>
/// THE PALETTE. Every instrument the original music is written for, in one place.
///
/// The brief is a late-80s/early-90s life simulator about jobs, rent, shopping and getting
/// ahead, so the idiom is bright synth-funk: an electric-piano-ish bell tone for chords, a
/// round synth bass with a little bite, a clean lead with vibrato that arrives late, a
/// glassy pad underneath, and a small drum kit. That is a PALETTE, not a quotation â€” none
/// of these is modelled on any particular record or any particular cue in the original.
///
/// Anyone who wants a different sound edits the numbers here. That is the whole point of
/// building the synthesiser rather than shipping somebody else's instruments.
/// </summary>
public static class Palette
{
    // ---------------------------------------------------------------- keys / chords

    /// <summary>
    /// The chord instrument. Two sines an octave and a fifth apart through a fast-decaying
    /// filter gives the struck-tine character an electric piano has, without a sample.
    /// </summary>
    public static readonly SynthPatch Keys = new()
    {
        Name = "keys",
        OscA = Wave.Sine, OscB = Wave.Triangle,
        OscBMix = 0.34, OscBSemitones = 12,
        DetuneCents = 4,
        Amp = new Adsr(0.004, 1.6, 0.18, 0.45),
        FilterEnv = new Adsr(0.001, 0.35, 0.05, 0.3),
        CutoffHz = 900, Resonance = 1.4, EnvAmountOctaves = 2.6, KeyTrack = 0.55,
        Gain = 0.42,
    };

    /// <summary>A brighter, reedier version of the same for stabs and comping.</summary>
    public static readonly SynthPatch Clav = new()
    {
        Name = "clav",
        OscA = Wave.Pulse, OscB = Wave.Pulse, PulseWidth = 0.22,
        OscBMix = 0.5, DetuneCents = 9,
        Amp = new Adsr(0.002, 0.30, 0.0, 0.12),
        FilterEnv = new Adsr(0.001, 0.12, 0.0, 0.1),
        CutoffHz = 700, Resonance = 3.2, EnvAmountOctaves = 3.4, KeyTrack = 0.5,
        Gain = 0.30,
    };

    // ---------------------------------------------------------------- bass

    /// <summary>
    /// Round below, bitey on top. The sub sine carries the weight and the resonant sweep
    /// on each note is what makes it read as funk rather than as an organ pedal.
    /// </summary>
    public static readonly SynthPatch Bass = new()
    {
        Name = "bass",
        OscA = Wave.Saw, OscB = Wave.Square,
        OscBMix = 0.3, DetuneCents = 6,
        SubLevel = 0.45,
        Amp = new Adsr(0.004, 0.5, 0.55, 0.10),
        FilterEnv = new Adsr(0.002, 0.16, 0.12, 0.10),
        CutoffHz = 180, Resonance = 3.6, EnvAmountOctaves = 3.0, KeyTrack = 0.3,
        Gain = 0.55,
    };

    // ---------------------------------------------------------------- lead / pad

    public static readonly SynthPatch Lead = new()
    {
        Name = "lead",
        OscA = Wave.Saw, OscB = Wave.Saw,
        OscBMix = 0.5, DetuneCents = 14,
        Amp = new Adsr(0.02, 0.5, 0.75, 0.22),
        FilterEnv = new Adsr(0.03, 0.4, 0.5, 0.2),
        CutoffHz = 1400, Resonance = 2.0, EnvAmountOctaves = 1.8, KeyTrack = 0.6,
        VibratoHz = 5.4, VibratoDepthCents = 14, VibratoDelaySeconds = 0.35,
        Gain = 0.32,
    };

    /// <summary>
    /// BRASS. Three things together make a saw stack read as a horn section rather than as
    /// a loud synth: the pitch starts a third of a semitone flat and settles in seventy
    /// milliseconds, the filter opens two and a half octaves just after the amplitude does
    /// so the attack has a bite the sustain does not, and the detune is wide enough to be a
    /// section rather than a player.
    ///
    /// Added for the winner's fanfare (resource 7), which the rest of the palette could not
    /// do. It is an addition to the palette, not a change to it: nothing already written
    /// uses it and nothing already written sounds different.
    /// </summary>
    public static readonly SynthPatch Brass = new()
    {
        Name = "brass",
        OscA = Wave.Saw, OscB = Wave.Saw,
        OscBMix = 0.5, DetuneCents = 22,
        Amp = new Adsr(0.035, 0.35, 0.72, 0.20),
        FilterEnv = new Adsr(0.055, 0.45, 0.35, 0.18),
        CutoffHz = 520, Resonance = 1.6, EnvAmountOctaves = 2.5, KeyTrack = 0.5,
        PitchRiseSemitones = -0.35, PitchRiseSeconds = 0.07,
        VibratoHz = 5.0, VibratoDepthCents = 9, VibratoDelaySeconds = 0.5,
        Gain = 0.34,
    };

    public static readonly SynthPatch Pad = new()
    {
        Name = "pad",
        OscA = Wave.Saw, OscB = Wave.Triangle,
        OscBMix = 0.42, OscBSemitones = 12,
        DetuneCents = 16,
        Amp = new Adsr(0.5, 1.2, 0.6, 1.1),
        FilterEnv = new Adsr(0.9, 1.5, 0.5, 1.0),
        CutoffHz = 420, Resonance = 1.1, EnvAmountOctaves = 2.2, KeyTrack = 0.35,
        Gain = 0.22,
    };

    /// <summary>A short glassy bell for stings and chimes.</summary>
    public static readonly SynthPatch Bell = new()
    {
        Name = "bell",
        OscA = Wave.Sine, OscB = Wave.Sine,
        OscBMix = 0.3, OscBSemitones = 19,       // an octave and a fifth: inharmonic enough
        DetuneCents = 2,
        Amp = new Adsr(0.002, 1.4, 0.0, 0.6),
        FilterEnv = new Adsr(0.001, 0.6, 0.0, 0.4),
        CutoffHz = 2400, Resonance = 1.0, EnvAmountOctaves = 1.6, KeyTrack = 0.8,
        Gain = 0.40,
    };

    // ---------------------------------------------------------------- drums
    //
    // A drum here is an ordinary voice with an extreme envelope. The kick is a sine whose
    // pitch falls two and a half octaves in 60 ms; the snare is noise plus a body tone; the
    // hats are noise with the filter almost shut and then wide open.

    public static readonly SynthPatch Kick = new()
    {
        Name = "kick",
        OscA = Wave.Sine, OscB = Wave.Sine, OscBMix = 0,
        DetuneCents = 0,
        NoiseLevel = 0.06,
        Amp = new Adsr(0.001, 0.30, 0.0, 0.05),
        FilterEnv = new Adsr(0.001, 0.04, 0.0, 0.04),
        CutoffHz = 120, Resonance = 0.9, EnvAmountOctaves = 3.0, KeyTrack = 0,
        PitchDropOctaves = 2.6, PitchDropSeconds = 0.055,
        Gain = 0.95,
    };

    public static readonly SynthPatch Snare = new()
    {
        Name = "snare",
        OscA = Wave.Triangle, OscB = Wave.Triangle, OscBMix = 0,
        NoiseLevel = 0.9,
        Amp = new Adsr(0.001, 0.16, 0.0, 0.06),
        FilterEnv = new Adsr(0.001, 0.09, 0.0, 0.05),
        CutoffHz = 900, Resonance = 1.3, EnvAmountOctaves = 2.6, KeyTrack = 0,
        PitchDropOctaves = 0.8, PitchDropSeconds = 0.03,
        Gain = 0.50,
    };

    public static readonly SynthPatch HatClosed = new()
    {
        Name = "hat",
        OscA = Wave.Square, OscB = Wave.Square, OscBMix = 0,
        NoiseLevel = 1.0,
        Amp = new Adsr(0.001, 0.045, 0.0, 0.02),
        FilterEnv = new Adsr(0.001, 0.03, 0.0, 0.02),
        CutoffHz = 6500, Resonance = 1.1, EnvAmountOctaves = 1.0, KeyTrack = 0,
        Gain = 0.26,
    };

    public static readonly SynthPatch HatOpen = HatClosed with
    {
        Name = "hatopen",
        Amp = new Adsr(0.001, 0.32, 0.0, 0.12),
        Gain = 0.20,
    };

    /// <summary>
    /// A crash cymbal: noise with the filter wide open and a decay measured in seconds
    /// rather than milliseconds, plus a slow downward tilt so the top comes off the sound
    /// as it rings, which is what a real cymbal does and what a flat noise decay does not.
    ///
    /// Added with <see cref="Brass"/> and for the same job. A fanfare and a factory fill
    /// both want one and the kit had no cymbal at all.
    /// </summary>
    public static readonly SynthPatch Crash = new()
    {
        Name = "crash",
        OscA = Wave.Square, OscB = Wave.Square, OscBMix = 0,
        NoiseLevel = 1.0,
        Amp = new Adsr(0.002, 1.8, 0.0, 0.5),
        FilterEnv = new Adsr(0.004, 1.4, 0.0, 0.5),
        CutoffHz = 3200, Resonance = 0.8, EnvAmountOctaves = 1.9, KeyTrack = 0,
        Gain = 0.22,
    };

    public static readonly SynthPatch Clap = new()
    {
        Name = "clap",
        OscA = Wave.Square, OscB = Wave.Square, OscBMix = 0,
        NoiseLevel = 1.0,
        Amp = new Adsr(0.003, 0.14, 0.0, 0.05),
        FilterEnv = new Adsr(0.002, 0.10, 0.0, 0.05),
        CutoffHz = 1500, Resonance = 3.0, EnvAmountOctaves = 1.4, KeyTrack = 0,
        Gain = 0.34,
    };

    /// <summary>Everything above, by the name a score file uses.</summary>
    public static readonly IReadOnlyDictionary<string, SynthPatch> ByName =
        new Dictionary<string, SynthPatch>(StringComparer.OrdinalIgnoreCase)
        {
            ["keys"] = Keys,
            ["clav"] = Clav,
            ["bass"] = Bass,
            ["lead"] = Lead,
            ["pad"] = Pad,
            ["bell"] = Bell,
            ["brass"] = Brass,
            ["kick"] = Kick,
            ["snare"] = Snare,
            ["hat"] = HatClosed,
            ["hatopen"] = HatOpen,
            ["clap"] = Clap,
            ["crash"] = Crash,
        };

    /// <summary>
    /// The drum names a score writes in a <c>kit</c> part, and the pitch each is triggered
    /// at. A kit part names the drum instead of a note, because "bd" is readable and "C1"
    /// is not.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (SynthPatch Patch, double Note)> Kit =
        new Dictionary<string, (SynthPatch, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["bd"] = (Kick, 36),
            ["sd"] = (Snare, 50),
            ["hh"] = (HatClosed, 78),
            ["oh"] = (HatOpen, 78),
            ["cp"] = (Clap, 60),
            ["cr"] = (Crash, 84),
        };
}

