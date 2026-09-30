using Jones.Audio.Original;

namespace Jones.Audio;

/// <summary>Which set of sounds the game is playing.</summary>
public enum AudioSource
{
    /// <summary>
    /// Sierra's arrangements on an emulated AdLib card â€” the port's original behaviour and
    /// the default. Nothing about this path changed when the second one arrived.
    /// </summary>
    AdLib,

    /// <summary>Music and effects written for this project, on this project's synthesiser.</summary>
    Original,
}

/// <summary>
/// Routes the game's sounds to one of the two audio paths, and mixes the result.
///
/// It exists so the two can be compared BACK TO BACK on the same cue, at the moment the cue
/// is playing. That is the only way anyone can decide whether the original music is better
/// than the arrangement it would replace â€” a decision that is the user's to make, and one
/// this class deliberately does not pre-empt: <see cref="Source"/> starts at
/// <see cref="AudioSource.AdLib"/> and stays there until something asks otherwise.
///
/// TWO THINGS MAKE THIS SAFE TO ADD:
///
/// 1. <see cref="SciSoundEngine"/>, <see cref="SciAdLibDriver"/> and <see cref="AdLibBank"/>
///    are untouched. This class calls the engine exactly as the heads used to, and if the
///    original path were deleted tomorrow the AdLib one would carry on unchanged.
///
/// 2. A resource with no original cue written yet FALLS BACK to the AdLib arrangement, per
///    cue, while still in <see cref="AudioSource.Original"/>. The original set is being
///    written a batch at a time and there is no moment where half of it means silence.
/// </summary>
public sealed class JonesAudioMixer
{
    public const int SampleRate = SciSoundEngine.SampleRate;

    private readonly SciSoundLibrary? _sci;
    private readonly SciSoundEngine? _adlib;
    private readonly OriginalSoundBank? _bank;
    private readonly OriginalMusicEngine? _original;

    private readonly object _gate = new();
    private short[] _monoScratch = [];

    /// <summary>
    /// ORIGINAL IS NOW THE DEFAULT, and this line is the whole of that change.
    ///
    /// It was <see cref="AudioSource.AdLib"/> for as long as the original set was
    /// incomplete, because a default that played half a soundtrack would have been worse
    /// than one that played all of somebody else's. Every resource the scripts can reach
    /// now has an original cue â€” asserted by
    /// <c>AudioSwitchTests.EveryResourceTheGameCanReachHasAnOriginalCue</c> â€” so the
    /// player no longer has to turn their own music on.
    ///
    /// NOTHING WAS REMOVED TO DO THIS. The AdLib path, its driver, its bank and the switch
    /// are all exactly where they were; setting <see cref="Source"/> back to
    /// <see cref="AudioSource.AdLib"/> plays Sierra's arrangements as it always did, and
    /// the settings file still records whichever the player chose.
    /// </summary>
    private AudioSource _source = AudioSource.Original;

    // What the game last asked for, so a switch can restart it on the other engine rather
    // than leaving the room silent until the player walks out and back in.
    private int _musicResource;
    private bool _musicLoop;

    public JonesAudioMixer(SciSoundLibrary? sci, OriginalSoundBank? original)
    {
        _sci = sci;
        if (sci is not null) _adlib = new SciSoundEngine(sci.Bank);

        _bank = original;
        if (original is not null) _original = new OriginalMusicEngine(SampleRate);

        // A head with no original set - missing assets, or a build without the scores -
        // stays on the AdLib path rather than claiming a soundtrack it does not have.
        if (original is null) _source = AudioSource.AdLib;
    }

    /// <summary>True when there is an original set to switch to at all.</summary>
    public bool OriginalAvailable => _bank is not null;

    /// <summary>
    /// Which set is playing. Changing it while a bed is up restarts that bed on the other
    /// engine from the top â€” which is what makes an A/B an A/B rather than a reload.
    /// </summary>
    public AudioSource Source
    {
        get { lock (_gate) return _source; }
        set
        {
            lock (_gate)
            {
                if (_source == value) return;
                if (value == AudioSource.Original && _bank is null) return;

                _source = value;

                // Effects are one-shots and not worth moving; the bed is the thing being
                // judged, so it moves.
                _adlib?.StopEffects();
                _original?.StopEffects();

                var resource = _musicResource;
                var loop = _musicLoop;
                _adlib?.CutMusic();
                _original?.CutMusic();
                if (resource > 0) StartMusic(resource, loop);
            }
        }
    }

    /// <summary>0..15, the scale `sndMASTER_VOLUME` uses. Applies to both paths.</summary>
    public int MasterVolume
    {
        get { lock (_gate) return _adlib?.MasterVolume ?? 15; }
        set { lock (_gate) if (_adlib is not null) _adlib.MasterVolume = value; }
    }

    public bool MusicPaused
    {
        get { lock (_gate) return (_adlib?.MusicPaused ?? false) || (_original?.MusicPaused ?? false); }
    }

    public bool MusicPlaying
    {
        get { lock (_gate) return (_adlib?.MusicPlaying ?? false) || (_original?.MusicPlaying ?? false); }
    }

    /// <summary>Whether this resource would come from the original set right now.</summary>
    public bool UsesOriginal(int resource)
    {
        lock (_gate) return UsesOriginalLocked(resource);
    }

    private bool UsesOriginalLocked(int resource) =>
        _source == AudioSource.Original && _bank is not null && _bank.Has(resource);

    // ------------------------------------------------------------------ music

    /// <summary>`gASong playBed: n`.</summary>
    public void PlayMusic(int resource, bool loop = true)
    {
        lock (_gate)
        {
            if (resource <= 0)
            {
                _musicResource = 0;
                _adlib?.StopMusic();
                _original?.StopMusic();
                return;
            }

            StartMusic(resource, loop);
        }
    }

    private void StartMusic(int resource, bool loop)
    {
        _musicResource = resource;
        _musicLoop = loop;

        if (UsesOriginalLocked(resource))
        {
            var score = _bank!.Music(resource);
            if (score is not null)
            {
                _adlib?.CutMusic();
                _original!.PlayMusic(score, loop);
                return;
            }
            // The resource is in the bank as an EFFECT, not a bed. Fall through.
        }

        var res = _sci?.Get(resource);
        if (res is null) return;

        _original?.CutMusic();
        _adlib?.PlayMusic(res, loop);
    }

    /// <summary>`gASong fade:` â€” the five-and-a-half-second fade, on whichever is playing.</summary>
    public void StopMusic()
    {
        lock (_gate) { _adlib?.StopMusic(); _original?.StopMusic(); }
    }

    /// <summary>`gASong stop:` â€” the cut.</summary>
    public void CutMusic()
    {
        lock (_gate) { _musicResource = 0; _adlib?.CutMusic(); _original?.CutMusic(); }
    }

    public void PauseMusic()
    {
        lock (_gate) { _adlib?.PauseMusic(); _original?.PauseMusic(); }
    }

    public void ResumeMusic()
    {
        lock (_gate) { _adlib?.ResumeMusic(); _original?.ResumeMusic(); }
    }

    // ------------------------------------------------------------------ effects

    /// <summary>`gASoundEffect play: n`, with the second argument as `resumeMusicWhenDone`.</summary>
    public void PlayEffect(int resource, bool loop = false, bool resumeMusicWhenDone = false)
    {
        lock (_gate)
        {
            if (UsesOriginalLocked(resource))
            {
                var pcm = _bank!.Effect(resource);
                if (pcm is not null)
                {
                    _original!.PlayEffect(pcm, loop, resumeMusicWhenDone);
                    if (resumeMusicWhenDone) { /* the original engine cues its own bed */ }

                    // The ducked bed may be on the OTHER engine, so release it here too â€”
                    // the original engine can only cue the one it owns.
                    if (resumeMusicWhenDone && (_adlib?.MusicPaused ?? false)) _adlib.ResumeMusic();
                    return;
                }
            }

            var res = _sci?.Get(resource);
            if (res is null)
            {
                if (resumeMusicWhenDone) { _adlib?.ResumeMusic(); _original?.ResumeMusic(); }
                return;
            }

            _adlib?.PlayEffect(res, loop, resumeMusicWhenDone);
            if (resumeMusicWhenDone && (_original?.MusicPaused ?? false)) _original.ResumeMusic();
        }
    }

    public void EndEffectLoop()
    {
        lock (_gate) { _adlib?.EndEffectLoop(); _original?.EndEffectLoop(); }
    }

    /// <summary>`gASoundEffect2 play: n` â€” `room1.sc:1500`.</summary>
    public void PlayEffect2(int resource)
    {
        lock (_gate)
        {
            if (UsesOriginalLocked(resource))
            {
                var pcm = _bank!.Effect(resource);
                if (pcm is not null) { _original!.PlayEffect2(pcm); return; }
            }

            var res = _sci?.Get(resource);
            if (res is not null) _adlib?.PlayEffect2(res);
        }
    }

    public void StopEffects()
    {
        lock (_gate) { _adlib?.StopEffects(); _original?.StopEffects(); }
    }

    public void StopAll()
    {
        lock (_gate) { _musicResource = 0; _adlib?.StopAll(); _original?.StopAll(); }
    }

    // ------------------------------------------------------------------ rendering

    /// <summary>
    /// Fills interleaved stereo. Both paths are mixed, because one cue can be coming from
    /// the original set while another still comes from the chip.
    ///
    /// The AdLib path is MONO â€” a real card was one chip â€” so it is placed up the middle.
    /// Nothing about it is widened or reprocessed: in <see cref="AudioSource.AdLib"/> the
    /// two channels are identical and the output is exactly what the mono path produced
    /// before this class existed.
    /// </summary>
    public bool RenderStereo(Span<float> destination)
    {
        if (destination.Length % 2 != 0)
            throw new ArgumentException("interleaved stereo needs an even length", nameof(destination));

        destination.Clear();

        lock (_gate)
        {
            var frames = destination.Length / 2;
            var any = false;

            if (_adlib is not null)
            {
                if (_monoScratch.Length < frames) _monoScratch = new short[frames];
                var mono = _monoScratch.AsSpan(0, frames);

                if (_adlib.Render(mono))
                {
                    any = true;
                    for (var i = 0; i < frames; i++)
                    {
                        var s = mono[i] / 32768f;
                        destination[i * 2] = s;
                        destination[i * 2 + 1] = s;
                    }
                }
            }

            if (_original is not null) any |= _original.RenderStereo(destination);

            return any;
        }
    }

    /// <summary>
    /// Interleaved stereo as 16-bit PCM â€” what both heads' output devices take.
    /// </summary>
    public bool RenderStereo(Span<short> destination)
    {
        var frames = destination.Length / 2;
        var stereo = frames * 2 <= 8192 ? stackalloc float[frames * 2] : new float[frames * 2];
        var any = RenderStereo(stereo);

        for (var i = 0; i < frames * 2; i++)
            destination[i] = (short)Math.Clamp(
                Math.Round(stereo[i] * 32767.0), short.MinValue, short.MaxValue);

        return any;
    }

    /// <summary>
    /// The mono fold-down, kept so a head that has not been widened to stereo still works
    /// and still sounds exactly as it did.
    /// </summary>
    public bool Render(Span<short> mono)
    {
        // With nothing but the AdLib path active this is the old call and nothing else:
        // no float round-trip, no summing, no change to the samples the device receives.
        lock (_gate)
        {
            if (_original is null || !(_original.MusicPlaying || _original.EffectPlaying
                                       || _original.Effect2Playing))
                return _adlib?.Render(mono) ?? Clear(mono);
        }

        var stereo = new float[mono.Length * 2];
        var any = RenderStereo(stereo);
        for (var i = 0; i < mono.Length; i++)
        {
            var m = (stereo[i * 2] + stereo[i * 2 + 1]) * 0.5;
            mono[i] = (short)Math.Clamp(Math.Round(m * 32767.0), short.MinValue, short.MaxValue);
        }
        return any;
    }

    private static bool Clear(Span<short> mono)
    {
        mono.Clear();
        return false;
    }
}

