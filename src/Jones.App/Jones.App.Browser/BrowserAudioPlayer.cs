using System;
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
    /// 1.2 seconds of slack costs 1.2 seconds of latency on a volume change, which is a
    /// background music bed and nobody can hear it. A gap in the music, everybody hears.
    /// </para>
    /// </summary>
    private const double TargetBuffer = 1.20;

    /// <summary>One chunk, in frames. 1/20th of a second at the mixer's rate.</summary>
    private const int ChunkFrames = JonesAudioMixerRate / 20;
    private const int JonesAudioMixerRate = Jones.Audio.JonesAudioMixer.SampleRate;

    private readonly Jones.Audio.JonesAudioMixer? _engine;
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
            var bank = new Jones.Audio.Original.OriginalSoundBank(JonesAudioMixerRate);
            bank.PreRenderEffects();
            _engine = new Jones.Audio.JonesAudioMixer(bank);
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
            // Up to twenty chunks - a full second of audio - in one tick, so a pump that
            // has just lost a run of ticks catches up in ONE rather than crawling back a
            // chunk at a time and underrunning again on the way back.
            for (var i = 0; i < 20; i++)
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

    public void PlayMusic(int soundResource, bool loop = true)
    {
        if (_enabled) _engine?.PlayMusic(soundResource, loop);
    }

    public void StopMusic() => _engine?.StopMusic();
    public void CutMusic() => _engine?.CutMusic();
    public void PauseMusic() => _engine?.PauseMusic();

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
