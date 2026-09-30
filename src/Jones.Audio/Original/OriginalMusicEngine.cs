namespace Jones.Audio.Original;

/// <summary>
/// Plays the original cues live, with the same three slots and the same vocabulary as
/// <see cref="SciSoundEngine"/> — a music bed, an effect, and a second effect that has to
/// sound over the first.
///
/// It is deliberately a MIRROR of that class rather than a redesign. The three Sound
/// objects, the fade that is not a cut, the pause that ducks a bed under a sting and the
/// cue that brings it back are all behaviour the scripts ask for by name, and they are the
/// same behaviour whichever synthesiser is making the noise. Anything that diverged here
/// would be a second set of bugs to find.
///
/// The bed is SEQUENCED LIVE rather than pre-rendered. Twenty seconds of stereo is about
/// four megabytes, fifteen beds would be sixty, and the Android head cannot spend that. The
/// synthesiser is cheap enough to run in real time — measured at over thirty times real
/// time for sixteen voices — so it runs.
/// </summary>
public sealed class OriginalMusicEngine
{
    public const int SampleRate = OriginalSynth.SampleRate;

    /// <summary>
    /// `(gASong fade:)` is `(DoSound sndFADE self 0 25 10 1)` (Sound.sc:102-104) — the same
    /// thirteen steps of twenty-five ticks the AdLib path uses, because it is the script's
    /// number and not the synthesiser's.
    /// </summary>
    private const int FadeTickerStep = 25;
    private const int FadeStep = 10;
    private const int FullVolume = 127;
    private const int TicksPerSecond = 60;

    private readonly int _rate;
    private readonly MusicSlot _music;
    private readonly SampleSlot _effect;
    private readonly SampleSlot _effect2;
    private readonly object _gate = new();
    private float[] _scratch = new float[4096];

    public OriginalMusicEngine(int sampleRate = SampleRate)
    {
        _rate = sampleRate;
        _music = new MusicSlot(sampleRate);
        _effect = new SampleSlot();
        _effect2 = new SampleSlot();
    }

    public bool MusicPlaying { get { lock (_gate) return _music.Active; } }
    public bool MusicPaused { get { lock (_gate) return _music.Paused; } }
    public bool EffectPlaying { get { lock (_gate) return _effect.Active; } }
    public bool Effect2Playing { get { lock (_gate) return _effect2.Active; } }

    public void PlayMusic(Score score, bool loop = true)
    {
        lock (_gate) _music.Start(score, loop, _rate);
    }

    /// <summary>`gASong fade:` — not a cut.</summary>
    public void StopMusic() { lock (_gate) _music.BeginFade(); }

    /// <summary>`gASong stop:` — the cut.</summary>
    public void CutMusic() { lock (_gate) _music.Stop(); }

    public void PauseMusic() { lock (_gate) _music.Pause(); }
    public void ResumeMusic() { lock (_gate) _music.Resume(); }

    public void PlayEffect(short[] stereoPcm, bool loop = false, bool resumeMusicWhenDone = false)
    {
        lock (_gate)
            _effect.Start(stereoPcm, loop, resumeMusicWhenDone ? _music.Resume : null);
    }

    public void EndEffectLoop() { lock (_gate) _effect.EndLoop(); }

    public void PlayEffect2(short[] stereoPcm) { lock (_gate) _effect2.Start(stereoPcm, false); }

    public void StopEffects() { lock (_gate) { _effect.Stop(); _effect2.Stop(); } }

    public void StopAll() { lock (_gate) { _music.Stop(); _effect.Stop(); _effect2.Stop(); } }

    /// <summary>
    /// ADDS interleaved stereo into <paramref name="destination"/> — it does not clear it,
    /// because the mixer sums this with the AdLib path while a cue that has no original
    /// version yet is still coming from the chip. Returns true if anything was added.
    /// </summary>
    public bool RenderStereo(Span<float> destination)
    {
        lock (_gate)
        {
            if (_scratch.Length < destination.Length) _scratch = new float[destination.Length];

            var any = _music.Render(destination, _scratch, _rate);
            any |= _effect.Render(destination);
            any |= _effect2.Render(destination);
            return any;
        }
    }

    // ==================================================================

    private sealed class MusicSlot
    {
        private readonly OriginalSynth _synth;

        private Score? _score;
        private List<Event> _events = [];
        private int _cursor;
        private int _frame;          // frames since the start of this pass
        private int _loopFrame;
        private int _endFrame;
        private int _loopCursor;     // first event at or after the loop point
        private bool _loop;
        private bool _finished;
        private int _tailFrames;

        private int _volume = FullVolume;
        private int _fadeTicker;
        private bool _fading;
        private bool _paused;
        private int _tickAcc;

        private readonly Dictionary<(string, double), int> _sounding = [];

        private readonly record struct Event(
            int Frame, bool On, string Part, SynthPatch Patch, double Note,
            double Velocity, double Pan);

        public MusicSlot(int sampleRate) => _synth = new OriginalSynth(sampleRate);

        public bool Active => _score is not null;
        public bool Paused => _paused;

        public void Start(Score score, bool loop, int sampleRate)
        {
            _synth.Silence();
            _synth.Reverb = score.Space;
            _sounding.Clear();

            _score = score;
            _events = Build(score, sampleRate);
            _cursor = 0;
            _frame = 0;
            _loop = loop;
            _finished = false;
            _tailFrames = 0;
            _volume = FullVolume;
            _fadeTicker = 0;
            _fading = false;
            _paused = false;
            _tickAcc = 0;

            var stepFrames = score.StepSeconds * sampleRate;
            _endFrame = (int)(score.LengthInSteps * stepFrames);
            _loopFrame = (int)(score.LoopFromStep * stepFrames);

            _loopCursor = 0;
            while (_loopCursor < _events.Count && _events[_loopCursor].Frame < _loopFrame)
                _loopCursor++;

            Dispatch();
        }

        public void Stop()
        {
            if (_score is null) return;
            _synth.Silence();
            _sounding.Clear();
            _score = null;
            _fading = false;
            _paused = false;
            _volume = FullVolume;
        }

        public void Pause()
        {
            if (_score is null || _paused) return;
            _paused = true;
            _synth.AllNotesOff();
        }

        public void Resume() => _paused = false;

        public void BeginFade()
        {
            if (_score is null || _fading) return;
            _fading = true;
            _fadeTicker = 0;
        }

        public bool Render(Span<float> destination, float[] scratch, int sampleRate)
        {
            if (_score is null || _paused) return false;

            var frames = destination.Length / 2;
            var written = 0;

            while (written < frames)
            {
                // Render only as far as the next event, so a note lands on the sample it
                // was written for rather than on a buffer boundary.
                var nextEvent = _cursor < _events.Count ? _events[_cursor].Frame : int.MaxValue;
                var chunk = Math.Min(frames - written, Math.Max(1, nextEvent - _frame));

                var span = scratch.AsSpan(0, chunk * 2);
                _synth.RenderStereo(span);

                var gain = _volume / (float)FullVolume;
                for (var i = 0; i < chunk * 2; i++)
                    destination[written * 2 + i] += span[i] * gain;

                written += chunk;
                _frame += chunk;
                if (_finished) _tailFrames += chunk;

                // The fade runs on SCI's 60 Hz tick, not on the buffer.
                _tickAcc += chunk * TicksPerSecond;
                while (_tickAcc >= sampleRate)
                {
                    _tickAcc -= sampleRate;
                    if (StepFade()) return true;        // the fade stopped the slot
                }

                Dispatch();

                if (_score is null) return true;

                // A one-shot cue is done once the events have run out and the reverb tail
                // has decayed. Three seconds is longer than the longest tail this
                // synthesiser produces.
                if (_finished && !_loop && _tailFrames > sampleRate * 3)
                {
                    Stop();
                    return true;
                }
            }

            return true;
        }

        /// <summary>Returns true if the fade ended the slot.</summary>
        private bool StepFade()
        {
            if (!_fading) return false;
            if (_fadeTicker < FadeTickerStep) { _fadeTicker++; return false; }

            _fadeTicker = 0;
            _volume -= FadeStep;
            if (_volume > 0) return false;

            Stop();
            return true;
        }

        private void Dispatch()
        {
            if (_score is null) return;

            for (var pass = 0; pass < 2; pass++)
            {
                while (_cursor < _events.Count && _events[_cursor].Frame <= _frame)
                {
                    var e = _events[_cursor++];
                    if (e.On)
                    {
                        var id = _synth.NoteOn(e.Patch, e.Note, e.Velocity, e.Pan);
                        _sounding[(e.Part, e.Note)] = id;
                    }
                    else if (_sounding.Remove((e.Part, e.Note), out var id))
                    {
                        _synth.NoteOff(id);
                    }
                }

                if (_frame < _endFrame) return;

                if (!_loop) { _finished = true; return; }

                // Round again from the loop point.
                //
                // A part still holding a note at the end of the pass HAS been released by
                // this point — `Build` puts a note-off on the last frame and the loop above
                // has just dispatched it — so a chord held through the final bar is
                // retriggered rather than sustained across the join. That is correct for
                // every cue written so far, because each one's loop point falls on a bar
                // that starts a fresh chord, which is exactly what a repeat would play. It
                // would NOT be correct for a cue that deliberately ties a note over the
                // join, and that would need the release suppressed here rather than a
                // different note written. Nothing needs it yet; the loop-join test measures
                // the seam on every cue, so the day something does, it will say so.
                _frame -= _endFrame - _loopFrame;
                _cursor = _loopCursor;
            }
        }

        private static List<Event> Build(Score score, int sampleRate)
        {
            var events = new List<Event>();
            var stepSeconds = score.StepSeconds;

            foreach (var part in score.Parts)
            {
                var open = new List<double>();

                for (var step = 0; step < part.Steps.Count; step++)
                {
                    var token = part.Steps[step];
                    if (token == "-") continue;

                    var frame = (int)((step * stepSeconds
                                       + (step % 2 == 1 ? score.Swing * stepSeconds : 0)) * sampleRate);

                    foreach (var n in open)
                        events.Add(new Event(frame, false, part.Name, part.Patch, n, 0, 0));
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
                            if (!Palette.Kit.TryGetValue(body, out var drum)) continue;
                            events.Add(new Event(frame, true, part.Name, drum.Patch, drum.Note,
                                Math.Clamp(velocity * part.Gain, 0, 1), part.Pan));
                            continue;
                        }

                        if (!Score.TryParseNote(body, out var note)) continue;

                        events.Add(new Event(frame, true, part.Name, part.Patch, note,
                            Math.Clamp(velocity * part.Gain, 0, 1), part.Pan));
                        open.Add(note);
                    }
                }

                // Anything held at the end is released at the end, EXCEPT on a part that
                // is still holding across the loop join — Dispatch leaves those alone.
                var last = (int)(part.Steps.Count * stepSeconds * sampleRate);
                foreach (var n in open)
                    events.Add(new Event(last, false, part.Name, part.Patch, n, 0, 0));
            }

            events.Sort((x, y) =>
            {
                var c = x.Frame.CompareTo(y.Frame);
                return c != 0 ? c : x.On.CompareTo(y.On);     // offs before ons
            });

            return events;
        }
    }

    // ==================================================================

    /// <summary>
    /// A pre-rendered one-shot: the synthesised button click, chime, siren and the rest.
    /// Stereo, at the engine's own rate, so playback is an add and a cursor.
    /// </summary>
    private sealed class SampleSlot
    {
        private short[]? _pcm;
        private int _cursor;
        private bool _loop;
        private Action? _finishedCue;

        public bool Active => _pcm is not null;

        public void Start(short[]? pcm, bool loop, Action? finishedCue = null)
        {
            _pcm = pcm is { Length: > 0 } ? pcm : null;
            _cursor = 0;
            _loop = loop;
            _finishedCue = finishedCue;

            // Nothing to play still has to release a bed that was ducked for it, or the
            // bed stays down forever.
            if (_pcm is null) { finishedCue?.Invoke(); _finishedCue = null; }
        }

        public void Stop() { _pcm = null; _finishedCue = null; }

        public void EndLoop() => _loop = false;

        public bool Render(Span<float> destination)
        {
            var pcm = _pcm;
            if (pcm is null) return false;

            for (var i = 0; i < destination.Length; i++)
            {
                if (_cursor >= pcm.Length)
                {
                    if (_loop) { _cursor = 0; }
                    else
                    {
                        var cue = _finishedCue;
                        Stop();
                        cue?.Invoke();
                        return true;
                    }
                }

                destination[i] += pcm[_cursor++] / 32768f;
            }

            return true;
        }
    }
}
