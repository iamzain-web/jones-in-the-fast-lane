namespace Jones.Audio.Original;

/// <summary>
/// A subtractive synthesiser written from scratch for this project.
///
/// WHY THIS EXISTS, stated plainly so nobody removes it by accident: the port's existing
/// sound is Sierra's arrangements played through a port of ScummVM's AdLib driver on an
/// LGPL OPL3 emulator. All of that stays, works and remains the default. This is the
/// beginning of a SECOND, entirely original audio path â€” our own instruments playing our
/// own music â€” so that the project can eventually stand on material it owns.
///
/// Every sample this file produces is arithmetic. There is no sample data, no recording,
/// no instrument bank, no wavetable read from disk and no third-party code. That is a
/// deliberate constraint, not an accident of implementation: it is what makes the output
/// original, and it is also what makes the whole thing portable, because arithmetic runs
/// identically on the Android head and the Windows one with no native binary to ship.
///
/// The signal path of one voice:
///
///     osc A â”€â”€â”
///     osc B â”€â”€â”¤ (detuned against A â€” the whole reason it sounds wide)
///     sub    â”€â”¼â”€â”€â–º mix â”€â”€â–º resonant low-pass â”€â”€â–º amp â”€â”€â–º pan â”€â”€â–º bus
///     noise  â”€â”˜              â–²                    â–²
///                            â”‚                    â”‚
///                       filter ADSR           amp ADSR
///                            â–²
///                          vibrato LFO
///
/// and the bus then goes through chorus, a tempo-free delay, a small reverb and a soft
/// limiter. Those four are what separate "a synthesiser" from "a beep": the dry sum of a
/// handful of oscillators always sounds like 1983 no matter how good the oscillators are.
/// </summary>
public sealed class OriginalSynth
{
    /// <summary>
    /// The same rate <see cref="SciSoundEngine"/> renders at, so both paths can hand the
    /// same device the same buffer format and be switched between without either head
    /// re-opening its audio output.
    /// </summary>
    public const int SampleRate = 48000;

    /// <summary>
    /// Sized from the busiest thing we would ever write: a four-note chord pad, a bass, a
    /// lead, and three or four drum hits ringing at once is about ten. Twenty-four leaves
    /// room for release tails overlapping the next chord, which is where polyphony is
    /// actually spent.
    /// </summary>
    public const int MaxVoices = 24;

    private readonly int _rate;
    private readonly Voice[] _voices;
    private readonly Effects _fx;
    private int _nextId = 1;

    public OriginalSynth(int sampleRate = SampleRate, int maxVoices = MaxVoices)
    {
        if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (maxVoices < 1) throw new ArgumentOutOfRangeException(nameof(maxVoices));

        _rate = sampleRate;
        _voices = new Voice[maxVoices];
        for (var i = 0; i < maxVoices; i++) _voices[i] = new Voice(sampleRate);
        _fx = new Effects(sampleRate);
    }

    /// <summary>Master gain applied before the limiter. 1.0 is unity.</summary>
    public double Gain { get; set; } = 1.0;

    public EffectSettings Reverb { get => _fx.Settings; set => _fx.Settings = value; }

    /// <summary>How many voices are currently making sound.</summary>
    public int ActiveVoices
    {
        get
        {
            var n = 0;
            foreach (var v in _voices) if (v.Active) n++;
            return n;
        }
    }

    /// <summary>
    /// Starts a note. <paramref name="midiNote"/> is fractional so a patch can be played
    /// off the grid; <paramref name="velocity"/> is 0..1; <paramref name="pan"/> is -1..1.
    /// Returns a handle for <see cref="NoteOff"/>, or 0 when every voice is busy with
    /// something louder.
    /// </summary>
    public int NoteOn(SynthPatch patch, double midiNote, double velocity, double pan = 0)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var slot = FindFreeVoice(velocity);
        if (slot < 0) return 0;

        var id = _nextId++;
        _voices[slot].Start(id, patch, midiNote, Math.Clamp(velocity, 0, 1), Math.Clamp(pan, -1, 1));
        return id;
    }

    /// <summary>Releases the note with this handle. Unknown handles are ignored.</summary>
    public void NoteOff(int id)
    {
        if (id == 0) return;
        foreach (var v in _voices)
            if (v.Id == id) { v.Release(); return; }
    }

    public void AllNotesOff()
    {
        foreach (var v in _voices) v.Release();
    }

    public void Silence()
    {
        foreach (var v in _voices) v.Kill();
        _fx.Clear();
    }

    /// <summary>
    /// Renders interleaved stereo floats, ADDING to whatever is already there is NOT what
    /// happens â€” the span is overwritten.
    /// </summary>
    public void RenderStereo(Span<float> interleaved)
    {
        if (interleaved.Length % 2 != 0)
            throw new ArgumentException("interleaved stereo needs an even length", nameof(interleaved));

        var frames = interleaved.Length / 2;
        for (var i = 0; i < frames; i++)
        {
            double l = 0, r = 0;
            foreach (var v in _voices)
            {
                if (!v.Active) continue;
                v.Next(out var s, out var vl, out var vr);
                l += s * vl;
                r += s * vr;
            }

            _fx.Process(ref l, ref r);

            l *= Gain;
            r *= Gain;

            interleaved[i * 2] = (float)SoftClip(l);
            interleaved[i * 2 + 1] = (float)SoftClip(r);
        }
    }

    /// <summary>
    /// The mono fold-down the game's existing audio seam takes: both heads push 16-bit mono
    /// at <see cref="SampleRate"/>, because a real AdLib card was mono and nothing
    /// downstream was ever built for two channels.
    /// </summary>
    public void Render(Span<short> mono)
    {
        var stereo = new float[mono.Length * 2];
        RenderStereo(stereo);
        for (var i = 0; i < mono.Length; i++)
        {
            var m = (stereo[i * 2] + stereo[i * 2 + 1]) * 0.5;
            mono[i] = (short)Math.Clamp(Math.Round(m * 32767.0), short.MinValue, short.MaxValue);
        }
    }

    /// <summary>
    /// A cubic soft knee rather than a hard clip. Two oscillators, a resonant filter and a
    /// reverb tail can momentarily sum past full scale on a loud chord; hard clipping that
    /// is audible as a click, and this is not.
    /// </summary>
    private static double SoftClip(double x)
    {
        if (x >= 1.5) return 1.0;
        if (x <= -1.5) return -1.0;
        return x - x * x * x / 6.75;
    }

    private int FindFreeVoice(double velocity)
    {
        // A genuinely idle slot first.
        for (var i = 0; i < _voices.Length; i++)
            if (!_voices[i].Active) return i;

        // Otherwise steal the quietest voice that is already releasing, and failing that
        // the quietest voice outright â€” never the loudest, which is the one being heard.
        var best = -1;
        var bestLevel = double.MaxValue;
        for (var i = 0; i < _voices.Length; i++)
        {
            var level = _voices[i].Level * (_voices[i].Releasing ? 0.25 : 1.0);
            if (level < bestLevel) { bestLevel = level; best = i; }
        }

        // If everything sounding is louder than the note asking for a slot, drop the note
        // instead of punching a hole in the chord.
        return bestLevel > velocity * 1.5 ? -1 : best;
    }
}

// ======================================================================================
// Oscillator
// ======================================================================================

public enum Wave
{
    Sine,
    Triangle,
    Saw,
    Square,
    /// <summary>Square with a settable duty cycle â€” thin and reedy at 10%, hollow at 50%.</summary>
    Pulse,
    Noise,
}

/// <summary>
/// One oscillator. The saw and pulse shapes are corrected with PolyBLEP, which subtracts a
/// small polynomial around each discontinuity.
///
/// This is not a nicety. A naive saw at 2 kHz aliases every harmonic above 24 kHz back down
/// into the audible band as inharmonic tones, and the result is the metallic, slightly sour
/// sound that gives away a hand-rolled synth immediately. The correction is about a dozen
/// arithmetic operations per sample and removes most of it.
/// </summary>
internal struct Osc
{
    private double _phase;

    public void Reset(double phase = 0) => _phase = phase;

    public double Next(Wave wave, double increment, double pulseWidth, ref uint noiseState)
    {
        if (wave == Wave.Noise)
        {
            // xorshift32: a whole-period generator, flat spectrum, four operations.
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            return (noiseState & 0xFFFFFF) / 8388608.0 - 1.0;
        }

        _phase += increment;
        if (_phase >= 1.0) _phase -= 1.0;

        switch (wave)
        {
            case Wave.Sine:
                return Math.Sin(_phase * 2 * Math.PI);

            case Wave.Triangle:
                // Integrating a corrected square would be more exact, but a triangle's
                // harmonics fall off at 12 dB/octave and alias far below audibility.
                return 4.0 * Math.Abs(_phase - 0.5) - 1.0;

            case Wave.Saw:
                return 2.0 * _phase - 1.0 - PolyBlep(_phase, increment);

            case Wave.Square:
            case Wave.Pulse:
            {
                var width = wave == Wave.Square ? 0.5 : Math.Clamp(pulseWidth, 0.05, 0.95);
                var v = _phase < width ? 1.0 : -1.0;
                v += PolyBlep(_phase, increment);
                var down = _phase - width;
                if (down < 0) down += 1.0;
                v -= PolyBlep(down, increment);
                return v;
            }

            default:
                return 0;
        }
    }

    private static double PolyBlep(double t, double dt)
    {
        if (dt <= 0) return 0;
        if (t < dt) { t /= dt; return t + t - t * t - 1.0; }
        if (t > 1.0 - dt) { t = (t - 1.0) / dt; return t * t + t + t + 1.0; }
        return 0;
    }
}

// ======================================================================================
// Envelope
// ======================================================================================

/// <summary>Attack, decay, sustain level, release â€” all times in seconds, sustain 0..1.</summary>
public readonly record struct Adsr(double Attack, double Decay, double Sustain, double Release)
{
    public static readonly Adsr Organ = new(0.005, 0.0, 1.0, 0.08);
    public static readonly Adsr Pluck = new(0.002, 0.25, 0.0, 0.18);
    public static readonly Adsr Pad = new(0.35, 0.6, 0.6, 0.9);
}

internal struct Envelope
{
    private enum Stage { Idle, Attack, Decay, Sustain, Release }

    private Stage _stage;
    private double _value;
    private double _releaseFrom;
    private double _t;          // seconds into the current stage
    private double _dt;
    private Adsr _a;

    /// <summary>
    /// The decay curve, as a per-sample multiplier rather than a call to Math.Exp.
    ///
    /// Two envelopes per voice at sixteen voices is 1.5 million Exp calls a second, and it
    /// dominated the whole engine's cost. An exponential decay is by definition a constant
    /// ratio per sample, so the same curve comes out of one multiply.
    /// </summary>
    private double _fallMul;
    private double _fall;

    public double Value => _value;
    public bool Active => _stage != Stage.Idle;
    public bool Releasing => _stage == Stage.Release;

    public void Start(Adsr a, int sampleRate)
    {
        _a = a;
        _dt = 1.0 / sampleRate;
        _t = 0;
        _value = 0;
        _stage = Stage.Attack;
    }

    public void Release()
    {
        if (_stage is Stage.Idle or Stage.Release) return;
        _releaseFrom = _value;
        _t = 0;
        BeginFall(_a.Release);
        _stage = Stage.Release;
    }

    public void Kill()
    {
        _stage = Stage.Idle;
        _value = 0;
    }

    public double Next()
    {
        switch (_stage)
        {
            case Stage.Idle:
                return 0;

            case Stage.Attack:
                _t += _dt;
                if (_a.Attack <= 0 || _t >= _a.Attack) { _value = 1; _t = 0; BeginFall(_a.Decay); _stage = Stage.Decay; break; }
                // Slightly convex: a linear attack on a plucked patch sounds soft-edged.
                // x(2-x) has the same shape as x^0.65 and costs one multiply.
                {
                    var x = _t / _a.Attack;
                    _value = x * (2.0 - x);
                }
                break;

            case Stage.Decay:
                _t += _dt;
                if (_a.Decay <= 0 || _t >= _a.Decay) { _value = _a.Sustain; _stage = Stage.Sustain; break; }
                _fall *= _fallMul;
                _value = _a.Sustain + (1.0 - _a.Sustain) * Shape(_fall);
                break;

            case Stage.Sustain:
                _value = _a.Sustain;
                if (_a.Sustain <= 0) { _stage = Stage.Idle; _value = 0; }
                break;

            case Stage.Release:
                _t += _dt;
                if (_a.Release <= 0 || _t >= _a.Release) { _value = 0; _stage = Stage.Idle; break; }
                _fall *= _fallMul;
                _value = _releaseFrom * Shape(_fall);
                break;
        }

        return _value;
    }

    /// <summary>
    /// Starts an exponential fall lasting <paramref name="seconds"/>, reaching -40 dB at
    /// the end. Called once per stage, not once per sample.
    /// </summary>
    private void BeginFall(double seconds)
    {
        _fall = 1.0;
        _fallMul = seconds <= 0 ? 0 : Math.Exp(-4.6 * _dt / seconds);
    }

    /// <summary>
    /// Pulls the tail of the exponential down onto zero. A true exponential never reaches
    /// it, and a voice whose envelope never reaches zero never frees its slot.
    /// </summary>
    private static double Shape(double e) => e <= 0.01 ? 0 : (e - 0.01) / 0.99;
}

// ======================================================================================
// Filter
// ======================================================================================

/// <summary>
/// A topology-preserving-transform state-variable filter: two integrators, resonant, and
/// stable when the cutoff is swept quickly â€” which a filter envelope does on every note.
/// The naive digital ladder blows up under exactly that condition.
/// </summary>
internal struct Svf
{
    private double _ic1, _ic2;

    public void Reset() { _ic1 = 0; _ic2 = 0; }

    /// <summary>
    /// The three coefficients, from the cutoff and the resonance. Worked out whenever the
    /// cutoff moves â€” which is at control rate â€” rather than per sample, because it
    /// contains a divide.
    /// </summary>
    /// <param name="g">tan(pi * cutoff / sampleRate)</param>
    /// <param name="k">1 / Q â€” smaller is more resonant.</param>
    public static void Coefficients(double g, double k, out double a1, out double a2, out double a3)
    {
        a1 = 1.0 / (1.0 + g * (g + k));
        a2 = g * a1;
        a3 = g * a2;
    }

    public double LowPass(double input, double a1, double a2, double a3)
    {
        var v3 = input - _ic2;
        var v1 = a1 * _ic1 + a2 * v3;
        var v2 = _ic2 + a2 * _ic1 + a3 * v3;

        _ic1 = 2 * v1 - _ic1;
        _ic2 = 2 * v2 - _ic2;

        return v2;
    }
}

// ======================================================================================
// Voice
// ======================================================================================

internal sealed class Voice
{
    private readonly int _rate;
    private readonly double _nyquist;

    private SynthPatch _patch = SynthPatch.Silent;
    private Osc _a, _b, _sub;
    private Envelope _amp, _flt;
    private Svf _filter;
    private uint _noise;
    private double _lfoPhase;
    private double _baseNote, _velocity, _panL, _panR;
    private double _age;

    /// <summary>
    /// Modulation runs at 1/32 of the sample rate â€” 1.5 kHz, which is far above anything a
    /// filter sweep or a vibrato contains.
    ///
    /// This is not a micro-optimisation, it is the difference between the synth being
    /// usable on a phone and not. Recomputing the filter coefficient per sample means a
    /// Math.Tan and two Math.Pow calls per voice per sample; at sixteen voices that is
    /// 2.3 million transcendental calls a second, and it measured as roughly a 6x cost over
    /// the whole engine. Nothing in the sound changes.
    /// </summary>
    private const int ModulationInterval = 32;

    private int _modCounter;
    private double _incA, _incB, _incSub, _k, _cutoffScale;
    private double _fa1, _fa2, _fa3;

    public int Id { get; private set; }
    public bool Active => _amp.Active;
    public bool Releasing => _amp.Releasing;
    public double Level => _amp.Value * _velocity;

    public Voice(int sampleRate)
    {
        _rate = sampleRate;
        _nyquist = sampleRate * 0.5;
        _noise = 0x9E3779B9;
    }

    public void Start(int id, SynthPatch patch, double midiNote, double velocity, double pan)
    {
        Id = id;
        _patch = patch;
        _baseNote = midiNote;
        _velocity = velocity;
        _age = 0;
        _lfoPhase = 0;

        // Free-running phase would make a kick drum's level depend on when it happened.
        _a.Reset(0);
        _b.Reset(patch.OscBPhase);
        _sub.Reset(0);
        _filter.Reset();
        _noise = (uint)(0x9E3779B9 + id * 2654435761u);

        _amp.Start(patch.Amp, _rate);
        _flt.Start(patch.FilterEnv, _rate);

        // Everything about the cutoff that cannot change while the note sounds.
        _cutoffScale = patch.CutoffHz
                       * Math.Pow(2.0, patch.KeyTrack * (midiNote - 60.0) / 12.0)
                       * (0.35 + 0.65 * velocity);
        _k = 1.0 / Math.Max(0.5, patch.Resonance);
        _modCounter = 0;

        // Constant-power pan: a part swept across the image keeps its loudness.
        var angle = (pan + 1.0) * 0.25 * Math.PI;
        _panL = Math.Cos(angle);
        _panR = Math.Sin(angle);
    }

    public void Release() { _amp.Release(); _flt.Release(); }

    public void Kill() { _amp.Kill(); _flt.Kill(); Id = 0; }

    public void Next(out double sample, out double left, out double right)
    {
        left = _panL;
        right = _panR;

        var amp = _amp.Next();
        var fenv = _flt.Next();
        if (!_amp.Active) { sample = 0; Id = 0; return; }

        _age += 1.0 / _rate;

        if (_patch.VibratoDepthCents > 0)
        {
            _lfoPhase += _patch.VibratoHz / _rate;
            if (_lfoPhase >= 1) _lfoPhase -= 1;
        }

        // ---- modulation, at control rate ---------------------------------------
        if (_modCounter-- <= 0)
        {
            _modCounter = ModulationInterval - 1;

            var note = _baseNote;

            // The drop that turns a sine into a kick drum, and a noise burst into a tom.
            if (_patch.PitchDropOctaves != 0 && _patch.PitchDropSeconds > 0)
            {
                var x = Math.Min(1.0, _age / _patch.PitchDropSeconds);
                var f = 1.0 - x;
                note -= _patch.PitchDropOctaves * 12.0 * (1.0 - f * f * f);
            }

            // ...and the opposite shape: starting off the note and settling onto it, which
            // is the first fifty milliseconds of a brass player finding the pitch.
            if (_patch.PitchRiseSemitones != 0 && _patch.PitchRiseSeconds > 0)
            {
                var x = Math.Min(1.0, _age / _patch.PitchRiseSeconds);
                var f = 1.0 - x;
                note += _patch.PitchRiseSemitones * f * f;
            }

            if (_patch.VibratoDepthCents > 0)
            {
                var onset = _patch.VibratoDelaySeconds <= 0
                    ? 1.0
                    : Math.Min(1.0, _age / _patch.VibratoDelaySeconds);
                note += Math.Sin(_lfoPhase * 2 * Math.PI) * _patch.VibratoDepthCents / 100.0 * onset;
            }

            var hz = 440.0 * Math.Pow(2.0, (note - 69.0) / 12.0);
            _incA = Math.Min(hz * Math.Pow(2.0, _patch.DetuneCents / -1200.0) / _rate, 0.49);
            _incB = Math.Min(hz * Math.Pow(2.0,
                (_patch.OscBSemitones + _patch.DetuneCents / 100.0) / 12.0) / _rate, 0.49);
            _incSub = Math.Min(hz * 0.5 / _rate, 0.49);

            var cutoff = Math.Clamp(
                _cutoffScale * Math.Pow(2.0, _patch.EnvAmountOctaves * fenv),
                20.0, _nyquist * 0.92);
            Svf.Coefficients(Math.Tan(Math.PI * cutoff / _rate), _k, out _fa1, out _fa2, out _fa3);
        }

        // ---- oscillators -------------------------------------------------------
        var mix = _a.Next(_patch.OscA, _incA, _patch.PulseWidth, ref _noise) * (1.0 - _patch.OscBMix);
        mix += _b.Next(_patch.OscB, _incB, _patch.PulseWidth, ref _noise) * _patch.OscBMix;
        if (_patch.SubLevel > 0)
            mix += _sub.Next(Wave.Sine, _incSub, 0.5, ref _noise) * _patch.SubLevel;
        if (_patch.NoiseLevel > 0)
        {
            // Noise has no phase, so it can share oscillator A's struct: the noise branch
            // returns before the phase accumulator is touched.
            mix += _a.Next(Wave.Noise, 0, 0, ref _noise) * _patch.NoiseLevel;
        }

        // ---- filter ------------------------------------------------------------
        var filtered = _filter.LowPass(mix, _fa1, _fa2, _fa3);

        // ---- amp ---------------------------------------------------------------
        sample = filtered * amp * _velocity * _patch.Gain;
    }
}

// ======================================================================================
// Effects
// ======================================================================================

/// <param name="ChorusDepth">0 disables the chorus.</param>
/// <param name="DelaySeconds">Echo time; 0 disables the delay.</param>
/// <param name="DelayFeedback">0..0.85.</param>
/// <param name="DelayMix">How much echo is heard.</param>
/// <param name="ReverbMix">0..1 wet.</param>
/// <param name="ReverbSize">0..1; scales the comb lengths.</param>
public readonly record struct EffectSettings(
    double ChorusDepth,
    double ChorusMix,
    double DelaySeconds,
    double DelayFeedback,
    double DelayMix,
    double ReverbMix,
    double ReverbSize)
{
    /// <summary>A little of everything â€” the default a music cue renders through.</summary>
    public static readonly EffectSettings Room =
        new(ChorusDepth: 0.004, ChorusMix: 0.35,
            DelaySeconds: 0.24, DelayFeedback: 0.32, DelayMix: 0.18,
            ReverbMix: 0.16, ReverbSize: 0.55);

    /// <summary>Dry. What a UI blip wants â€” an echo on a button click is maddening.</summary>
    public static readonly EffectSettings Dry = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Chorus, delay and a small Schroeder reverb: four parallel comb filters into two series
/// allpasses, per channel, with the right channel's lengths offset so the two decorrelate.
///
/// It is the oldest reverb topology there is and it is a handful of adds per sample, which
/// is the point â€” this has to run on a phone alongside the game.
/// </summary>
internal sealed class Effects
{
    private readonly int _rate;

    private readonly double[] _chorusL, _chorusR;
    private int _chorusWrite;
    private double _chorusPhase;

    private readonly double[] _delayL, _delayR;
    private int _delayWrite;

    private readonly Comb[] _combL, _combR;
    private readonly AllPass[] _apL, _apR;

    public EffectSettings Settings { get; set; } = EffectSettings.Room;

    public Effects(int sampleRate)
    {
        _rate = sampleRate;

        var chorusLen = (int)(0.05 * sampleRate);
        _chorusL = new double[chorusLen];
        _chorusR = new double[chorusLen];

        var delayLen = (int)(1.5 * sampleRate);
        _delayL = new double[delayLen];
        _delayR = new double[delayLen];

        // Mutually prime-ish comb lengths in milliseconds; the classic Schroeder set.
        double[] combMs = [29.7, 37.1, 41.1, 43.7];
        double[] apMs = [5.0, 1.7];

        _combL = new Comb[combMs.Length];
        _combR = new Comb[combMs.Length];
        for (var i = 0; i < combMs.Length; i++)
        {
            _combL[i] = new Comb((int)(combMs[i] * 0.001 * sampleRate));
            _combR[i] = new Comb((int)((combMs[i] + 2.3) * 0.001 * sampleRate));
        }

        _apL = new AllPass[apMs.Length];
        _apR = new AllPass[apMs.Length];
        for (var i = 0; i < apMs.Length; i++)
        {
            _apL[i] = new AllPass((int)(apMs[i] * 0.001 * sampleRate));
            _apR[i] = new AllPass((int)((apMs[i] + 0.4) * 0.001 * sampleRate));
        }
    }

    public void Clear()
    {
        Array.Clear(_chorusL); Array.Clear(_chorusR);
        Array.Clear(_delayL); Array.Clear(_delayR);
        foreach (var c in _combL) c.Clear();
        foreach (var c in _combR) c.Clear();
        foreach (var a in _apL) a.Clear();
        foreach (var a in _apR) a.Clear();
    }

    public void Process(ref double l, ref double r)
    {
        var s = Settings;

        // ---- chorus ------------------------------------------------------------
        if (s.ChorusMix > 0)
        {
            _chorusL[_chorusWrite] = l;
            _chorusR[_chorusWrite] = r;

            _chorusPhase += 0.6 / _rate;                 // 0.6 Hz sweep
            if (_chorusPhase >= 1) _chorusPhase -= 1;
            var lfo = Math.Sin(_chorusPhase * 2 * Math.PI);

            // The two taps move in opposition, which is what makes a chorus wide rather
            // than just detuned.
            var dL = (0.012 + s.ChorusDepth * lfo) * _rate;
            var dR = (0.012 - s.ChorusDepth * lfo) * _rate;

            l += Tap(_chorusL, _chorusWrite, dL) * s.ChorusMix;
            r += Tap(_chorusR, _chorusWrite, dR) * s.ChorusMix;

            if (++_chorusWrite >= _chorusL.Length) _chorusWrite = 0;
        }

        // ---- delay -------------------------------------------------------------
        if (s.DelayMix > 0 && s.DelaySeconds > 0)
        {
            var d = (int)Math.Clamp(s.DelaySeconds * _rate, 1, _delayL.Length - 1);
            var read = (_delayWrite - d + _delayL.Length) % _delayL.Length;

            var el = _delayL[read];
            var er = _delayR[read];

            // Cross-fed, so a repeat bounces between the speakers.
            _delayL[_delayWrite] = l + er * s.DelayFeedback;
            _delayR[_delayWrite] = r + el * s.DelayFeedback;
            if (++_delayWrite >= _delayL.Length) _delayWrite = 0;

            l += el * s.DelayMix;
            r += er * s.DelayMix;
        }

        // ---- reverb ------------------------------------------------------------
        if (s.ReverbMix > 0)
        {
            var feedback = 0.70 + 0.28 * Math.Clamp(s.ReverbSize, 0, 1);
            double wl = 0, wr = 0;
            foreach (var c in _combL) wl += c.Process(l, feedback);
            foreach (var c in _combR) wr += c.Process(r, feedback);
            wl *= 0.25; wr *= 0.25;
            foreach (var a in _apL) wl = a.Process(wl);
            foreach (var a in _apR) wr = a.Process(wr);

            l = l * (1 - s.ReverbMix) + wl * s.ReverbMix;
            r = r * (1 - s.ReverbMix) + wr * s.ReverbMix;
        }
    }

    private static double Tap(double[] buffer, int write, double delaySamples)
    {
        var d = Math.Clamp(delaySamples, 1, buffer.Length - 2);
        var i = (int)d;
        var frac = d - i;
        var a = (write - i + buffer.Length) % buffer.Length;
        var b = (a - 1 + buffer.Length) % buffer.Length;
        return buffer[a] * (1 - frac) + buffer[b] * frac;
    }

    private sealed class Comb(int length)
    {
        private readonly double[] _buf = new double[Math.Max(1, length)];
        private int _i;
        private double _store;

        public void Clear() { Array.Clear(_buf); _store = 0; }

        public double Process(double input, double feedback)
        {
            var output = _buf[_i];
            // One-pole damping in the loop: without it the tail is bright and metallic.
            _store = output * 0.6 + _store * 0.4;
            _buf[_i] = input + _store * feedback;
                if (++_i >= _buf.Length) _i = 0;
            return output;
        }
    }

    private sealed class AllPass(int length)
    {
        private readonly double[] _buf = new double[Math.Max(1, length)];
        private int _i;

        public void Clear() => Array.Clear(_buf);

        public double Process(double input)
        {
            var buffered = _buf[_i];
            var output = -input + buffered;
            _buf[_i] = input + buffered * 0.5;
                if (++_i >= _buf.Length) _i = 0;
            return output;
        }
    }
}

