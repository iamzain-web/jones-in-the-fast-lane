using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Jones.App.Audio;

namespace Jones.App.Browser;

/// <summary>
/// SOUND ON THE WEB HEAD, WHICH HAD NONE.
///
/// <para>
/// Desktop has <c>WindowsAudioPlayer</c> and Android has <c>AndroidAudioPlayer</c>; this
/// head had nothing, so <c>SilentAudioPlayer</c> answered every call and the game played
/// in silence - no music, no effects, no speech - with nothing anywhere saying why.
/// </para>
///
/// <para>
/// TWO PATHS, BECAUSE THE GAME HAS TWO, and they are nothing alike:
/// </para>
/// <list type="bullet">
/// <item><b>Music and effects are SYNTHESISED</b> by <c>Jones.Audio</c>, in managed code,
///   which is the thing that makes this possible at all: no MIDI device, no system
///   synthesiser, nothing platform-specific. It renders stereo PCM and this class finds it
///   somewhere to go.</item>
/// <item><b>Speech is 533 .wav files</b>, which a browser plays natively and better than
///   anything here could - including reporting its own playback position, which is exactly
///   what the lip sync needs and what the desktop head has to interrogate MCI for.</item>
/// </list>
///
/// <para>
/// PACED, NOT STREAMED. WebAssembly has one thread and the browser's event loop is on it,
/// so there is no audio callback to fill - the pump below renders a quarter-second at a
/// time, on a timer, and hands it to WebAudio to schedule. When the game stalls the main
/// thread for longer than that, playback gaps rather than garbles; see the re-anchoring in
/// jonesAudioPush. That is a real limitation of this head and not a bug to be found later.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserAudioPlayer : IAudioPlayer
{
    [JSImport("globalThis.jonesAudioStart")]
    private static partial int Start(int sampleRate);

    [JSImport("globalThis.jonesAudioPush")]
    private static partial double Push([JSMarshalAs<JSType.MemoryView>] Span<byte> pcm);

    [JSImport("globalThis.jonesMusicPlay")]
    private static partial void MusicPlay([JSMarshalAs<JSType.MemoryView>] Span<byte> pcm, bool loop);

    [JSImport("globalThis.jonesMusicStop")]
    private static partial void MusicStop();

    [JSImport("globalThis.jonesMusicPause")]
    private static partial void MusicPause();

    [JSImport("globalThis.jonesMusicResume")]
    private static partial void MusicResume();

    [JSImport("globalThis.jonesSpeechPlay")]
    private static partial void SpeechPlay(string url);

    [JSImport("globalThis.jonesSpeechStop")]
    private static partial void SpeechStop();

    [JSImport("globalThis.jonesSpeechPosition")]
    private static partial double SpeechAt();

    /// <summary>
    /// How far ahead of the playhead to keep the queue, in seconds.
    ///
    /// <para>
    /// MEASURED TWICE, DEEPENED TWICE. At 0.30 the queue sat between 0.0 and 0.2 and went
    /// negative - an underrun, heard as a gap. At 0.65 it survived a local page and STILL
    /// went negative on the deployed one, where the same thread is also fetching art and
    /// speech: readings of -0.029 and -0.120, with a silent frame between them.
    /// </para>
    ///
    /// <para>
    /// The pump shares ONE THREAD with everything this head does, including the screen
    /// rebuilds that are its whole performance problem, so it does not lose single ticks -
    /// it loses runs of them. The buffer has to be deeper than the longest stall, not
    /// deeper than the average one.
    /// </para>
    ///
    /// <para>
    /// MEASURED A THIRD TIME, AND THE THIRD MEASUREMENT IS THE ONE THAT MATTERS. With 1.2
    /// the queue never actually REACHED 1.2: it alternated between 0.3 and EMPTY, roughly
    /// 25,000 starved frames per cycle - a stutter rather than a gap. One pump tick fills
    /// about a second, so the pattern says the tick itself is being starved of the thread
    /// for about a second at a time. A one-second head start cannot cover a one-second
    /// stall; it can only just fail to.
    ///
    /// Three seconds covers it with room, and the cost is three seconds of latency on a
    /// volume change to a background bed, which nobody can hear. A stutter, everybody
    /// hears.
    /// </para>
    /// </summary>
    private const double TargetBuffer = 3.00;

    /// <summary>One chunk, in frames. 1/20th of a second at the mixer's rate.</summary>
    private const int ChunkFrames = JonesAudioMixerRate / 20;
    private const int JonesAudioMixerRate = Jones.Audio.JonesAudioMixer.SampleRate;

    private readonly Jones.Audio.JonesAudioMixer? _engine;
    private Jones.Audio.Original.OriginalSoundBank? _bank;

    /// <summary>
    /// Each bed, rendered once and kept. There are twenty-six scores in the whole game and
    /// a player walks in and out of the same buildings all session, so this is small and
    /// turns every repeat visit into a dictionary lookup instead of a second of synthesis.
    /// </summary>
    private readonly Dictionary<int, byte[]> _beds = [];

    /// <summary>Which bed is playing, so a repeat request is not a restart.</summary>
    private int _playingBed;
    private readonly float[] _samples = new float[ChunkFrames * 2];   // interleaved stereo
    private readonly byte[] _bytes = new byte[ChunkFrames * 2 * sizeof(float)];
    private bool _started;

    public BrowserAudioPlayer()
    {
        try
        {
            // The bank pre-renders the synthesised effects so the first button click of a
            // session is not also the first time anything has been synthesised - the same
            // reason the desktop head does it off its startup path.
            _bank = new Jones.Audio.Original.OriginalSoundBank(JonesAudioMixerRate);
            _bank.PreRenderEffects();
            _engine = new Jones.Audio.JonesAudioMixer(_bank);
        }
        catch (Exception)
        {
            // A head with no sound is still a playable game.
            _engine = null;
        }

        try { _started = Start(JonesAudioMixerRate) > 0; }
        catch (Exception) { _started = false; }

        // Delivered when the dispatcher starts running, which is after this constructor
        // returns and after Avalonia is up. See StartPump.
        try { Dispatcher.UIThread.Post(StartPump, DispatcherPriority.Background); }
        catch (Exception) { /* no dispatcher at all: the game is still playable, silently */ }

        // REPORTED RATHER THAN ASSUMED, for the same reason the art reports what it found:
        // silence and silence-because-nothing-was-built look identical from the outside,
        // and this head has already shipped silent once with nothing to say why.
        Console.WriteLine($"Jones: audio synth={_engine is not null}, output={_started}, "
                          + $"rate {JonesAudioMixerRate}");

    }

    private bool _pumping;

    /// <summary>
    /// Queues the pump to start as soon as Avalonia's dispatcher is running.
    ///
    /// <para>
    /// POSTED, NOT STARTED, AND THIS IS THE SECOND TIME THE SAME TRAP HAS BITTEN. A
    /// <c>DispatcherTimer</c> created before the dispatcher exists never ticks - it is
    /// created happily, reports nothing, and is simply dead. This player is built in
    /// <c>Program.Main</c>, which runs BEFORE <c>StartBrowserAppAsync</c>, so anything the
    /// constructor starts directly is on the wrong side of that line.
    /// </para>
    ///
    /// <para>
    /// Starting it on the first sound instead only moved the trap: a sound requested before
    /// the app was up - which is exactly what a startup probe does - latched the "started"
    /// flag onto a timer that could never fire, and KILLED AUDIO FOR THE SESSION. A Post
    /// made before the loop is running is queued and delivered when it starts, so there is
    /// no window in which this can be done too early.
    /// </para>
    /// </summary>
    private void StartPump()
    {
        if (_pumping || _engine is null || !_started) return;
        _pumping = true;
        Console.WriteLine("Jones: audio pump started");

        // 60 ms against a 300 ms buffer: five chances to refill before it runs dry, which
        // is the margin that survives the screen rebuilds this head is prone to.
        // NORMAL PRIORITY, NOT BACKGROUND. Background is behind the screen rebuilds, and
        // those are exactly what stalls this head - so the one thing that must not be late
        // was queued behind the one thing that is always late. A frame arriving a
        // millisecond after the audio refill is invisible; the reverse is a hole in the
        // music.
        DispatcherTimer.Run(() => { Pump(); return true; },
                            TimeSpan.FromMilliseconds(60), DispatcherPriority.Normal);
    }

    /// <summary>
    /// Keeps the queue topped up. Renders at most four chunks a tick so a long stall cannot
    /// turn into a burst of audio that then has to be thrown away.
    /// </summary>
    private void Pump()
    {
        if (_engine is null || !_started || !_enabled) return;

        try
        {
            // Up to eighty chunks - four seconds - in one tick. The loop stops the moment
            // the target is met, so this is a CEILING for catching up after a long stall,
            // not work done every tick.
            for (var i = 0; i < 80; i++)
            {
                var rendered = _engine.RenderStereo(_samples);

                // WHAT CAME OUT, NOT WHETHER IT WAS ASKED FOR. A pump that is faithfully
                // scheduling silence looks identical, from every other indicator, to one
                // that is playing music - which is precisely how this head reported itself
                // healthy while making no sound at all. Reported for the first few seconds
                // and then never again.
                if (_reports < 3)
                {
                    float peak = 0;
                    foreach (var v in _samples) { var a = v < 0 ? -v : v; if (a > peak) peak = a; }
                    if (peak > 0 || ++_quiet % 50 == 0)
                    {
                        _reports++;
                        Console.WriteLine($"Jones: audio rendered={rendered}, "
                                          + $"music={_engine.MusicPlaying}, peak={peak:0.0000}");
                    }
                }

                Buffer.BlockCopy(_samples, 0, _bytes, 0, _bytes.Length);
                if (Push(_bytes) >= TargetBuffer) return;
            }
        }
        catch (Exception e)
        {
            // A context the browser has closed under us must not take the game with it -
            // but SAID ONCE, because a pump that is failing every 60ms in silence is how
            // this head shipped mute in the first place.
            if (!_reported)
            {
                _reported = true;
                Console.WriteLine($"Jones: audio pump failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    private bool _reported;
    private int _reports;
    private int _quiet;

    // ------------------------------------------------------------------
    // Speech - the browser's own player, by its resource id
    // ------------------------------------------------------------------

    public void PlaySpeech(int audioId)
    {
        if (!_enabled) return;
        try { SpeechPlay($"res/audio/speech/{audioId}.wav"); }
        catch (Exception) { /* a missing clip is a silent line */ }
    }

    public TimeSpan? SpeechPosition
    {
        get
        {
            try
            {
                var at = SpeechAt();
                return at < 0 ? null : TimeSpan.FromSeconds(at);
            }
            catch (Exception) { return null; }
        }
    }

    public void StopSpeech()
    {
        try { SpeechStop(); } catch (Exception) { /* already gone */ }
    }

    // ------------------------------------------------------------------
    // Music and effects - straight through to the synthesiser
    // ------------------------------------------------------------------

    /// <summary>
    /// `gASong playBed: n` - RENDERED WHOLE AND HANDED TO THE AUDIO THREAD, which is the
    /// difference between music and stuttering on this head.
    ///
    /// <para>
    /// Streaming it could not work and the measurements say why: the pump is a timer on the
    /// SAME THREAD as the game's 40Hz screen rebuilds and its art decoding, so it does not
    /// run while the game is busy - 68,000 starved frames in eleven seconds on the deployed
    /// page, every one of them a hole. No buffer depth fixes a pump that does not get to
    /// run.
    /// </para>
    ///
    /// <para>
    /// A bed is a fixed piece of music that repeats, so it never needed streaming. Rendered
    /// once and given to WebAudio, it loops on the audio thread, which the main thread
    /// cannot stall. The cost is the render itself - paid once per score per session, and
    /// cached.
    /// </para>
    /// </summary>
    public void PlayMusic(int soundResource, bool loop = true)
    {
        if (!_enabled || _bank is null) return;

        // ASKING FOR THE BED THAT IS ALREADY PLAYING MUST NOT RESTART IT. The game re-issues
        // `playBed:` freely - it is idempotent against one `gASong` object in the original -
        // and here each re-issue stopped the source and started a new one, which is an
        // audible hole every time. The suite says the board theme contains NO silent window
        // at all, so every silence measured in the browser was this.
        if (_playingBed == soundResource) return;

        try
        {
            if (!_beds.TryGetValue(soundResource, out var pcm))
            {
                pcm = RenderBed(soundResource);
                if (pcm.Length == 0) return;      // nothing to play, and nothing recorded
                _beds[soundResource] = pcm;
            }

            // SET ONLY ON SUCCESS. Marking it before the render meant a bed that failed to
            // render was recorded as "already playing", and every later request for it
            // returned at the guard - one empty render and the location was silent for the
            // rest of the session.
            _playingBed = soundResource;
            MusicPlay(pcm, loop);
        }
        catch (Exception) { /* a bed that will not render is a quiet room, not a dead game */ }
    }

    /// <summary>
    /// One pass of a score, as interleaved stereo bytes. Rendered on its OWN engine so the
    /// effects engine is left alone, and stopped when the score does rather than at a
    /// guessed length - with a cap, because a score that never reports an end must not
    /// render for ever.
    /// </summary>
    private byte[] RenderBed(int resource)
    {
        var engine = new Jones.Audio.JonesAudioMixer(_bank);
        engine.PlayMusic(resource, loop: false);

        var chunk = new float[ChunkFrames * 2];
        var all = new List<float>(JonesAudioMixerRate * 2 * 30);
        // SIXTY SECONDS, NOT THREE MINUTES. Rendered stereo float, a three-minute cap is
        // 69 MB for ONE bed - and these are cached per resource, in a WebAssembly heap that
        // is also holding 1,254 decoded cels. A bed that long would be a memory bomb rather
        // than a convenience.
        var cap = JonesAudioMixerRate * 60 / ChunkFrames;

        // THE ENGINE IS NOT PLAYING YET ON THE FIRST TICK, and asking it then is how this
        // rendered an empty bed: one chunk of silence, `MusicPlaying` still false, break,
        // trim the silence, return nothing. The board was silent and nothing reported a
        // failure because nothing had failed. Give it a few chunks to start before its
        // answer is worth anything.
        const int settle = 8;      // 400ms

        for (var i = 0; i < cap; i++)
        {
            engine.RenderStereo(chunk);
            all.AddRange(chunk);
            if (i >= settle && !engine.MusicPlaying) break;
        }

        // TRIM THE TAIL. The last chunk runs past the final note, and a loop that includes
        // the silence gains a gap every time round.
        var end = all.Count;
        while (end >= 2 && all[end - 1] == 0 && all[end - 2] == 0) end -= 2;

        var bytes = new byte[end * sizeof(float)];
        Buffer.BlockCopy(all.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public void StopMusic() { _playingBed = 0; try { MusicStop(); } catch (Exception) { } }
    public void CutMusic() { _playingBed = 0; try { MusicStop(); } catch (Exception) { } }
    public void PauseMusic() { try { MusicPause(); } catch (Exception) { } }

    public void PlayEffect(int soundResource, bool loop = false, bool resumeMusicWhenDone = false)
    {
        if (_enabled) _engine?.PlayEffect(soundResource, loop, resumeMusicWhenDone);
    }

    public void EndEffectLoop() => _engine?.EndEffectLoop();

    public void PlayEffect2(int soundResource)
    {
        if (_enabled) _engine?.PlayEffect2(soundResource);
    }

    public void StopEffects() => _engine?.StopEffects();

    private bool _enabled = true;

    /// <summary>
    /// The master switch (Ctrl-V). Turning it off silences what is playing rather than
    /// letting the current bed run on under a muted game.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value) return;

            try { SpeechStop(); } catch (Exception) { /* nothing to stop */ }
            _engine?.StopAll();
        }
    }
}
