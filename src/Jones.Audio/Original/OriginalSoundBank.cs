namespace Jones.Audio.Original;

/// <summary>
/// The original audio, indexed by the game's own sound resource number.
///
/// The scripts ask for sounds by number â€” `(gASong playBed: 43)`, `(gASoundEffect play: 23)`
/// â€” so that number is the only key anything needs. A resource this bank does not know
/// about returns null, and the caller falls back to the AdLib arrangement for that one cue.
/// That fallback is not a safety net, it is the plan: the original set is being written a
/// batch at a time, and the two paths have to interleave cleanly while it fills in.
/// </summary>
public sealed class OriginalSoundBank
{
    /// <summary>
    /// The synthesised effects, by the resource each stands in for. These are one-shots, so
    /// they are rendered ONCE and kept as PCM rather than synthesised live: the button
    /// click fires from about sixty-five call sites and must never cost anything at the
    /// moment of a press.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, Func<int, float[]>> EffectRenderers =
        new Dictionary<int, Func<int, float[]>>
        {
            [23] = Foley.ButtonClick,                       // the universal button click
            [29] = Foley.WeekChime,                         // the week is over
            [31] = Foley.WorkClock,                         // the shift clock
            [8] = rate => Foley.PaperRustle(rate),          // the newspaper
            [21] = rate => Foley.Siren(rate),               // the ambulance
            [25] = rate => Foley.LottoMachine(rate),        // the lotto machine
        };

    private readonly int _rate;
    private readonly Dictionary<int, Score> _music = [];
    private readonly Dictionary<int, short[]> _effects = [];
    private readonly object _gate = new();

    public OriginalSoundBank(int sampleRate = OriginalSynth.SampleRate) => _rate = sampleRate;

    /// <summary>Every resource this bank can stand in for, music and effects together.</summary>
    public IEnumerable<int> Resources =>
        ScoreLibrary.ForResource.Keys.Concat(EffectRenderers.Keys).Distinct().OrderBy(n => n);

    public bool Has(int resource) =>
        ScoreLibrary.ForResource.ContainsKey(resource) || EffectRenderers.ContainsKey(resource);

    /// <summary>The written cue for this resource, or null where none exists yet.</summary>
    public Score? Music(int resource)
    {
        lock (_gate)
        {
            if (_music.TryGetValue(resource, out var cached)) return cached;
            var score = ScoreLibrary.ForSound(resource);
            if (score is not null) _music[resource] = score;
            return score;
        }
    }

    /// <summary>
    /// The synthesised effect for this resource as interleaved stereo PCM, or null.
    ///
    /// Rendered on first use and kept. The largest is the lotto machine at 7.9 seconds,
    /// about 1.5 MB as 16-bit stereo; the whole set is under four, and only what the player
    /// actually triggers is ever built.
    /// </summary>
    /// <summary>
    /// The longest cue that will be rendered to PCM for an effect slot. The stings are all
    /// under eleven seconds; the beds loop and the title is two minutes, and neither has any
    /// business being pre-rendered into memory.
    /// </summary>
    private const double LongestEffectSeconds = 15.0;

    public short[]? Effect(int resource)
    {
        Func<int, float[]>? render = null;

        if (EffectRenderers.TryGetValue(resource, out var foley))
        {
            render = foley;
        }
        else
        {
            // A STING IS A SCORE, NOT A FOLEY EFFECT, and the scripts still play it through
            // an effect slot: `muggedByMarket.sc:31` is `(gASoundEffect play: 20)` and the
            // rentOffice sites play 44 the same way.
            //
            // This branch is here because its absence was a real bug. While the AdLib path
            // still existed, a sting reached `Effect()`, got null because it is not one of
            // the six synthesised effects, and fell through to the chip - so Bad News was
            // still Sierra's long after the original one had been written, and the coverage
            // test did not catch it because the bank DOES know resource 44, as a score.
            var score = ScoreLibrary.ForSound(resource);
            if (score is null || score.Loop || score.LengthSeconds > LongestEffectSeconds)
                return null;

            render = rate => score.Render(new OriginalSynth(rate), rate, tailSeconds: 1.5);
        }

        lock (_gate)
        {
            if (_effects.TryGetValue(resource, out var cached)) return cached;

            var pcm = render(_rate);
            var samples = new short[pcm.Length];
            for (var i = 0; i < pcm.Length; i++)
                samples[i] = (short)Math.Clamp(Math.Round(pcm[i] * 32767.0), -32768, 32767);

            _effects[resource] = samples;
            return samples;
        }
    }

    /// <summary>
    /// Renders every effect now, off the audio thread. The heads call this on their audio
    /// start-up thread so that the first button click of a session is not also the first
    /// time anything has been synthesised.
    /// </summary>
    public void PreRenderEffects()
    {
        foreach (var resource in EffectRenderers.Keys) Effect(resource);

        // ...and the stings, which are scores rather than Foley but still land in an effect
        // slot. `Effect` renders and caches them; doing it here means the first mugging of a
        // session is not also the first time that cue has been synthesised.
        foreach (var resource in ScoreLibrary.ForResource.Keys) Effect(resource);
    }
}

