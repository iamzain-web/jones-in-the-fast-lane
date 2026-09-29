using NukedOPL3Sharp;

namespace Jones.Audio;

/// <summary>
/// Plays the game's sound resources the way an AdLib card did: a live OPL2 driven by
/// <see cref="SciAdLibDriver"/>, rendered to PCM on demand.
///
/// There are three slots because the game has three Sound objects, declared together at
/// `Main.sc:1261-1312`: `gASong` holds the music bed, `gASoundEffect` holds one-shots, and
/// `gASoundEffect2` holds the few effects that must sound OVER another effect. Scripts
/// talk to them separately (`gASong playBed: 40`, `gASoundEffect play: 23`) and a click
/// must not disturb the bed. The third is not redundant: `room1.sc:1499-1500` chimes the
/// end of the week on `gASoundEffect2` at the moment `timeKeep::doit` is reached, which is
/// in the middle of the button press whose own click is still on `gASoundEffect`.
///
/// KNOWN DEVIATION, stated rather than hidden: the slots have a chip each, so twenty-seven
/// voices exist where the real card had nine. On the original, a button click during a
/// busy bed would steal a music voice for an instant. Reproducing that needs SCI's
/// channel-remapping arbitration between concurrently playing sounds, which is a separate
/// piece of the kernel and not something to guess at; giving each slot its own chip keeps
/// every arrangement individually exact and only loses the contention. The game itself
/// works around the same contention in places — `employment.sc:35-36` pauses the song
/// before playing effect 44 — and that ducking IS reproduced here, because the scripts ask
/// for it explicitly rather than relying on the card.
/// </summary>
public sealed class SciSoundEngine
{
    /// <summary>
    /// Mono samples per second. See <see cref="SciAdLibDriver.SampleRate"/> for why this
    /// is 48 kHz rather than the chip's own 49716 Hz.
    /// </summary>
    public const int SampleRate = SciAdLibDriver.SampleRate;

    /// <summary>
    /// `(gASong fade:)` with no arguments is `(DoSound sndFADE self 0 25 10 1)`
    /// (Sound.sc:102-104): fade to 0, one step every 25 ticks, 10 per step, then stop.
    /// `playBed` starts a sound at volume 127 (Sound.sc:57), so a full fade is 13 steps
    /// over 325 ticks, a little under five and a half seconds.
    /// </summary>
    private const int FadeTickerStep = 25;
    private const int FadeStep = 10;
    private const int FullVolume = 127;

    private readonly Slot _music;
    private readonly Slot _effect;
    private readonly Slot _effect2;
    private readonly object _gate = new();
    private short[] _scratch = new short[2048];

    public SciSoundEngine(AdLibBank bank)
    {
        _music = new Slot(bank);
        _effect = new Slot(bank);
        _effect2 = new Slot(bank);
    }

    /// <summary>0..15, the scale <c>sndMASTER_VOLUME</c> uses. 0 silences the chip.</summary>
    public int MasterVolume
    {
        get { lock (_gate) return _music.Driver.MasterVolume; }
        set
        {
            lock (_gate)
            {
                _music.Driver.MasterVolume = value;
                _effect.Driver.MasterVolume = value;
                _effect2.Driver.MasterVolume = value;
            }
        }
    }

    public bool MusicPlaying { get { lock (_gate) return _music.Active; } }
    public bool EffectPlaying { get { lock (_gate) return _effect.Active; } }
    public bool Effect2Playing { get { lock (_gate) return _effect2.Active; } }

    /// <summary>True while `(gASong pause: 1)` is in force — see <see cref="PauseMusic"/>.</summary>
    public bool MusicPaused { get { lock (_gate) return _music.Paused; } }

    /// <summary>
    /// `gASong playBed:` — starts the bed at full volume, looping. Starting a new one
    /// replaces whatever the slot held, cancelling a fade in progress, because the game
    /// has exactly one `gASong` and `play:` on it abandons the fade the same way.
    /// </summary>
    public void PlayMusic(SciSoundResource resource, bool loop = true)
    {
        lock (_gate) _music.Start(resource, loop);
    }

    /// <summary>`gASong fade:` — not a cut. See the fade constants above.</summary>
    public void StopMusic()
    {
        lock (_gate) _music.BeginFade();
    }

    /// <summary>
    /// `gASong stop:` — `(DoSound sndSTOP self)` (Sound.sc:73-81), which is a CUT and not
    /// the fade `StopMusic` performs. The two are distinct in the scripts and are used for
    /// different things: a location's exit fades (`bank.sc:135`), while something that
    /// takes the screen over cuts — `newspaper.sc:203`, `lottoScript.sc:42`,
    /// `muggedByMarket.sc:30`, `room1.sc:1499`.
    /// </summary>
    public void CutMusic()
    {
        lock (_gate) _music.Stop();
    }

    /// <summary>
    /// `gASong pause: 1` — `(DoSound sndPAUSE self 1)` (Sound.sc:83-88). The bed goes
    /// silent where it stands and resumes from the same place, which is how the game
    /// ducks the music under a sting: `employment.sc:35-36` and `rentOffice.sc:257-301`
    /// pause the song and then play the sting with `gASong` as the cue target, so the bed
    /// comes back when the sting ends. See <see cref="PlayEffect"/>.
    /// </summary>
    public void PauseMusic()
    {
        lock (_gate) _music.Pause();
    }

    /// <summary>
    /// `gASong pause: 0`, which is what `aSong::cue` does (`Main.sc:1267-1270`) and what
    /// each job-list dialog does on opening (`applianceJobs.sc:30` and its six siblings).
    /// </summary>
    public void ResumeMusic()
    {
        lock (_gate) _music.Resume();
    }

    /// <summary>
    /// `gASoundEffect play:` — one-shot, and a new one interrupts the last.
    ///
    /// <paramref name="loop"/> is `(gASoundEffect loop: -1 play: 25)`, the lotto machine
    /// (`lottoScript.sc:43`); end it with <see cref="EndEffectLoop"/>.
    ///
    /// <paramref name="resumeMusicWhenDone"/> is the second argument in
    /// `(gASoundEffect play: 44 gASong)`: `Sound::play` stores it as `client`
    /// (Sound.sc:49) and `Sound::check` cues it when the sound ends (Sound.sc:118-127),
    /// and `aSong::cue` is `(self pause: 0)` (`Main.sc:1267-1270`). So the whole of it is
    /// "resume the bed when this sting finishes".
    /// </summary>
    public void PlayEffect(SciSoundResource resource, bool loop = false,
                           bool resumeMusicWhenDone = false)
    {
        lock (_gate)
            _effect.Start(resource, loop, resumeMusicWhenDone ? _music.Resume : null);
    }

    /// <summary>
    /// `(gASoundEffect loop: 1)` — `lottoScript.sc:232` ends the lotto machine's loop by
    /// setting the loop COUNT back to one, so the sound plays out its current pass and
    /// stops rather than being cut off mid-note.
    /// </summary>
    public void EndEffectLoop()
    {
        lock (_gate) _effect.EndLoop();
    }

    /// <summary>
    /// `gASoundEffect2 play:` — the second effect object (`Main.sc:1297`). Only
    /// `room1.sc:1500` uses it, and it does so precisely because the first slot is busy.
    /// </summary>
    public void PlayEffect2(SciSoundResource resource)
    {
        lock (_gate) _effect2.Start(resource, loop: false);
    }

    /// <summary>
    /// `(gASoundEffect stop:)` and `(gASoundEffect2 stop:)` together —
    /// `winnerScript.sc:67-68` clears both before the winner's fanfare.
    /// </summary>
    public void StopEffects()
    {
        lock (_gate) { _effect.Stop(); _effect2.Stop(); }
    }

    public void StopAll()
    {
        lock (_gate) { _music.Stop(); _effect.Stop(); _effect2.Stop(); }
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with mono 16-bit samples at
    /// <see cref="SampleRate"/>. Returns false when both slots are idle, so a caller can
    /// stop pushing buffers at a sound device rather than feeding it silence forever.
    /// </summary>
    public bool Render(Span<short> destination)
    {
        destination.Clear();
        lock (_gate)
        {
            var any = _music.Render(destination, ref _scratch);
            any |= _effect.Render(destination, ref _scratch);
            any |= _effect2.Render(destination, ref _scratch);
            return any;
        }
    }

    // ------------------------------------------------------------------

    private sealed class Slot
    {
        private readonly Opl3Chip _chip = new();
        public readonly SciAdLibDriver Driver;

        private SciSoundResource? _resource;
        private int _cursor;
        private int _tick;
        private int _acc;            // fixed point: one tick every SampleRate/60 samples
        private bool _loop;
        private bool _finished;      // events exhausted; only the release tail is left
        private int _tailSamples;
        private int _volume = FullVolume;
        private int _fadeTicker;
        private bool _fading;
        private bool _paused;

        /// <summary>
        /// The sound's `client`, cued when it ends (Sound.sc:118-127). Only ever set to
        /// the music slot's resume, which is the whole of `aSong::cue`.
        /// </summary>
        private Action? _finishedCue;

        /// <summary>
        /// How long a one-shot is allowed to ring on after its last event before being
        /// cut. Release tails on this bank are short; five seconds is a backstop against
        /// an instrument with a sustaining envelope holding a slot open forever.
        /// </summary>
        private const int MaxTailSamples = SampleRate * 5;

        /// <summary>
        /// Below this the output counts as silence for the purpose of retiring a slot.
        /// One 16-bit LSB is 1/32768 of full scale, so this is inaudible by any measure.
        /// </summary>
        private const int SilenceThreshold = 4;
        private const int SilenceRunToStop = SampleRate / 10;
        private int _silentRun;

        public Slot(AdLibBank bank)
        {
            // The driver resets the chip itself - it has to, because the emulator will
            // not accept a register write before it has been given a sample rate.
            Driver = new SciAdLibDriver(_chip, bank);
        }

        public bool Active => _resource is not null;
        public bool Paused => _paused;

        public void Start(SciSoundResource resource, bool loop, Action? finishedCue = null)
        {
            Driver.Reset();

            _resource = resource;
            _cursor = 0;
            _tick = 0;
            _acc = 0;
            _loop = loop;
            _finished = false;
            _tailSamples = 0;
            _silentRun = 0;
            _volume = FullVolume;
            _fadeTicker = 0;
            _fading = false;

            // `Sound::play` re-inits the node, so a paused sound that is given a new
            // number comes back playing (Sound.sc:39-54). The music slot relies on this:
            // a bed left paused by a sting that was cut short is released by the next
            // `playBed:`.
            _paused = false;

            // NOT carried over from whatever this slot was playing before. `Sound::play`
            // assigns `client` unconditionally, so a `play:` with no second argument
            // clears it (Sound.sc:49) and the old cue is simply lost.
            _finishedCue = finishedCue;

            DispatchDue();
        }

        public void Stop()
        {
            if (_resource is null) return;
            Driver.AllNotesOff();
            Driver.Reset();
            _resource = null;
            _fading = false;
            _paused = false;
            _finishedCue = null;
            _volume = FullVolume;
        }

        /// <summary>
        /// `pause: 1`. The sequencer stops where it is and the chip is keyed off; nothing
        /// is rendered and no tick is counted until <see cref="Resume"/>, so the sound
        /// carries on from the same bar.
        /// </summary>
        public void Pause()
        {
            if (_resource is null || _paused) return;
            _paused = true;
            Driver.AllNotesOff();
        }

        /// <summary>`pause: 0`.</summary>
        public void Resume() => _paused = false;

        /// <summary>
        /// `loop: 1` on a sound already looping. The loop marker is simply not taken the
        /// next time the events run out, so the current pass finishes and the sound ends.
        /// </summary>
        public void EndLoop() => _loop = false;

        public void BeginFade()
        {
            if (_resource is null || _fading) return;
            _fading = true;
            _fadeTicker = 0;
        }

        public bool Render(Span<short> destination, ref short[] scratch)
        {
            if (_resource is null || _paused) return false;

            var written = 0;
            while (written < destination.Length)
            {
                // Samples left before the next 1/60 s tick.
                var untilTick = (SampleRate - _acc + 59) / 60;
                var chunk = Math.Min(destination.Length - written, untilTick);

                if (scratch.Length < chunk * 2) scratch = new short[chunk * 2];
                var stereo = scratch.AsSpan(0, chunk * 2);
                _chip.GenerateStream(stereo);

                // The OPL2 is mono. In OPL2 mode the emulator puts the same signal on
                // both outputs, so one of them is the whole story.
                var gain = _volume;
                for (var i = 0; i < chunk; i++)
                {
                    var s = stereo[i * 2];
                    if (gain != FullVolume) s = (short)(s * gain / FullVolume);
                    if (Math.Abs((int)s) > SilenceThreshold) _silentRun = 0; else _silentRun++;

                    var mixed = destination[written + i] + s;
                    destination[written + i] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
                }

                written += chunk;
                _acc += chunk * 60;

                if (_finished) _tailSamples += chunk;

                if (_acc >= SampleRate)
                {
                    _acc -= SampleRate;
                    _tick++;
                    Driver.Tick();
                    StepFade();
                    DispatchDue();
                }

                if (_resource is null) return true;                 // the fade finished it

                if (_finished && !_loop &&
                    (_silentRun >= SilenceRunToStop || _tailSamples >= MaxTailSamples))
                {
                    // `Sound::check` cues the client as the sound ends, and only then —
                    // a sound REPLACED before it finishes never cues, which is why the
                    // ducked bed in employment.sc stays down if you click again while the
                    // sting is playing. Stop() clears the cue, so that is what happens.
                    var cue = _finishedCue;
                    Stop();
                    cue?.Invoke();
                    return true;
                }
            }

            return true;
        }

        private void StepFade()
        {
            if (!_fading) return;

            if (_fadeTicker < FadeTickerStep) { _fadeTicker++; return; }

            _fadeTicker = 0;
            _volume -= FadeStep;
            if (_volume <= 0)
            {
                // stopAfterFading is 1 in every `fade:` the scripts make.
                Stop();
            }
        }

        private void DispatchDue()
        {
            var res = _resource;
            if (res is null) return;

            // Two passes at most: dispatch what is due, and if that runs the resource out
            // and it loops, wrap and dispatch whatever sits on the loop tick itself.
            for (var pass = 0; pass < 2; pass++)
            {
                while (_cursor < res.Events.Count && res.Events[_cursor].Tick <= _tick)
                {
                    var e = res.Events[_cursor++];
                    Driver.Send(e.Status, e.Data1, e.Data2);
                }

                if (_cursor < res.Events.Count) return;

                if (!_loop) { _finished = true; return; }

                // 0xFC on a looping sound jumps back to the marked loop point, or to the
                // start when the resource marked none. The driver keeps its channel setup
                // across the jump, as the original parser does.
                _tick = res.LoopTick;
                _cursor = 0;
                while (_cursor < res.Events.Count && res.Events[_cursor].Tick < res.LoopTick)
                    _cursor++;

                // A loop point past the last event would spin here forever.
                if (_cursor >= res.Events.Count) { _finished = true; _loop = false; return; }
            }
        }
    }
}
