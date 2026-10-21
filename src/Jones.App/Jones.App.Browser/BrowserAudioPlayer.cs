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

    /// <summary>How far ahead of the playhead to keep the queue, in seconds.</summary>
    private const double TargetBuffer = 0.30;

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

        // REPORTED RATHER THAN ASSUMED, for the same reason the art reports what it found:
        // silence and silence-because-nothing-was-built look identical from the outside,
        // and this head has already shipped silent once with nothing to say why.
        Console.WriteLine($"Jones: audio synth={_engine is not null}, output={_started}, "
                          + $"rate {JonesAudioMixerRate}");

    }

    private bool _pumping;

    /// <summary>
    /// Starts the pump, the first time the game asks for a sound.
    ///
    /// <para>
    /// NOT IN THE CONSTRUCTOR, AND THAT COST AN HOUR. This player is built in
    /// <c>Program.Main</c>, BEFORE <c>StartBrowserAppAsync</c> - so there is no Avalonia
    /// dispatcher yet, and a <c>DispatcherTimer</c> created there never ticks. The context
    /// came up "running", the game asked for music, and not one sample was ever queued:
    /// `bufferedAhead: 0`, with nothing raised anywhere to say so.
    /// </para>
    ///
    /// <para>
    /// By the time anything asks for a sound, Avalonia is running by definition - the ask
    /// comes from a screen it drew.
    /// </para>
    /// </summary>
    private void EnsurePump()
    {
        if (_pumping || _engine is null || !_started) return;
        _pumping = true;
        Console.WriteLine("Jones: audio pump started");

        // 60 ms against a 300 ms buffer: five chances to refill before it runs dry, which
        // is the margin that survives the screen rebuilds this head is prone to.
        DispatcherTimer.Run(() => { Pump(); return true; },
                            TimeSpan.FromMilliseconds(60), DispatcherPriority.Background);
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
            for (var i = 0; i < 4; i++)
            {
                _engine.RenderStereo(_samples);
                Buffer.BlockCopy(_samples, 0, _bytes, 0, _bytes.Length);
                if (Push(_bytes) >= TargetBuffer) return;
            }
        }
        catch (Exception)
        {
            // A context the browser has closed under us must not take the game with it.
        }
    }

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
        if (!_enabled) return;
        EnsurePump();
        _engine?.PlayMusic(soundResource, loop);
    }

    public void StopMusic() => _engine?.StopMusic();
    public void CutMusic() => _engine?.CutMusic();
    public void PauseMusic() => _engine?.PauseMusic();

    public void PlayEffect(int soundResource, bool loop = false, bool resumeMusicWhenDone = false)
    {
        if (!_enabled) return;
        EnsurePump();
        _engine?.PlayEffect(soundResource, loop, resumeMusicWhenDone);
    }

    public void EndEffectLoop() => _engine?.EndEffectLoop();

    public void PlayEffect2(int soundResource)
    {
        if (!_enabled) return;
        EnsurePump();
        _engine?.PlayEffect2(soundResource);
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
