using Jones.Audio.Original;

namespace Jones.Audio;

/// <summary>
/// The game's sound, addressed the way the scripts address it: by resource number.
///
/// <para>
/// THIS USED TO BE A MIXER between two audio paths â€” Sierra's arrangements on an emulated
/// AdLib card, and the music written for this project â€” with a runtime switch so the two
/// could be compared. The comparison is over. The original set covers every resource the
/// scripts can reach, the AdLib path has been removed along with the emulator it needed and
/// the instrument bank it read, and what is left is one engine behind the same call surface
/// the heads already used.
/// </para>
///
/// <para>
/// The name is kept because both heads and their tests refer to it and renaming buys
/// nothing; what it mixes now is the three Sound objects the game itself has â€” a bed, an
/// effect, and a second effect that has to sound over the first â€” which is what
/// <see cref="OriginalMusicEngine"/> provides.
/// </para>
/// </summary>
public sealed class JonesAudioMixer
{
    public const int SampleRate = OriginalMusicEngine.SampleRate;

    private readonly OriginalSoundBank? _bank;
    private readonly OriginalMusicEngine? _engine;
    private readonly object _gate = new();

    private int _musicResource;

    public JonesAudioMixer(OriginalSoundBank? bank)
    {
        _bank = bank;
        if (bank is not null) _engine = new OriginalMusicEngine(SampleRate);
    }

    /// <summary>True when there is a soundtrack to play at all.</summary>
    public bool Available => _bank is not null;

    public bool MusicPlaying { get { lock (_gate) return _engine?.MusicPlaying ?? false; } }
    public bool MusicPaused { get { lock (_gate) return _engine?.MusicPaused ?? false; } }

    /// <summary>
    /// Kept so the heads' volume plumbing still compiles and still means something. The
    /// original engine has no 0-15 master of its own; this scales its output the way
    /// `sndMASTER_VOLUME` scaled the chip's.
    /// </summary>
    public int MasterVolume
    {
        get { lock (_gate) return _masterVolume; }
        set
        {
            lock (_gate)
            {
                _masterVolume = Math.Clamp(value, 0, 15);
                if (_engine is not null) _engine.Gain = _masterVolume / 15.0;
            }
        }
    }

    private int _masterVolume = 15;

    // ------------------------------------------------------------------ music

    /// <summary>`gASong playBed: n`.</summary>
    public void PlayMusic(int resource, bool loop = true)
    {
        lock (_gate)
        {
            if (_engine is null) return;

            if (resource <= 0) { _musicResource = 0; _engine.StopMusic(); return; }

            var score = _bank!.Music(resource);
            if (score is null) return;

            _musicResource = resource;
            _engine.PlayMusic(score, loop);
        }
    }

    /// <summary>`gASong fade:` â€” the five-and-a-half-second fade, not a cut.</summary>
    public void StopMusic() { lock (_gate) _engine?.StopMusic(); }

    /// <summary>`gASong stop:` â€” the cut.</summary>
    public void CutMusic() { lock (_gate) { _musicResource = 0; _engine?.CutMusic(); } }

    public void PauseMusic() { lock (_gate) _engine?.PauseMusic(); }
    public void ResumeMusic() { lock (_gate) _engine?.ResumeMusic(); }

    // ------------------------------------------------------------------ effects

    /// <summary>`gASoundEffect play: n`, with the second argument as `resumeMusicWhenDone`.</summary>
    public void PlayEffect(int resource, bool loop = false, bool resumeMusicWhenDone = false)
    {
        lock (_gate)
        {
            if (_engine is null) return;

            // `Effect` covers both the six synthesised effects and the stings, which are
            // scores rendered and cached on first use.
            var pcm = _bank!.Effect(resource);
            if (pcm is not null) { _engine.PlayEffect(pcm, loop, resumeMusicWhenDone); return; }

            // Nothing to play still has to release a bed that was ducked for it, or the bed
            // stays down forever.
            if (resumeMusicWhenDone) _engine.ResumeMusic();
        }
    }

    public void EndEffectLoop() { lock (_gate) _engine?.EndEffectLoop(); }

    /// <summary>`gASoundEffect2 play: n` â€” `room1.sc:1500`.</summary>
    public void PlayEffect2(int resource)
    {
        lock (_gate)
        {
            if (_engine is null) return;
            var pcm = _bank!.Effect(resource);
            if (pcm is not null) _engine.PlayEffect2(pcm);
        }
    }

    public void StopEffects() { lock (_gate) _engine?.StopEffects(); }

    public void StopAll() { lock (_gate) { _musicResource = 0; _engine?.StopAll(); } }

    // ------------------------------------------------------------------ rendering

    /// <summary>Fills interleaved stereo floats. Returns false when nothing is sounding.</summary>
    public bool RenderStereo(Span<float> destination)
    {
        if (destination.Length % 2 != 0)
            throw new ArgumentException("interleaved stereo needs an even length", nameof(destination));

        destination.Clear();
        lock (_gate) return _engine?.RenderStereo(destination) ?? false;
    }

    /// <summary>Interleaved stereo as 16-bit PCM â€” what both heads' output devices take.</summary>
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
    /// The mono fold-down, kept so a head that has not been widened to stereo still works.
    /// Neither head uses it today; both open two channels.
    /// </summary>
    public bool Render(Span<short> mono)
    {
        var stereo = new float[mono.Length * 2];
        var any = RenderStereo(stereo);
        for (var i = 0; i < mono.Length; i++)
        {
            var m = (stereo[i * 2] + stereo[i * 2 + 1]) * 0.5;
            mono[i] = (short)Math.Clamp(Math.Round(m * 32767.0), short.MinValue, short.MaxValue);
        }
        return any;
    }
}

