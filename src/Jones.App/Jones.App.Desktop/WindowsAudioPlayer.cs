using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Jones.App.Audio;

namespace Jones.App.Desktop;

/// <summary>
/// Windows speech playback through MCI (winmm.dll).
///
/// WHY NOT System.Media.SoundPlayer: it cannot tell you where playback has got to, and
/// that turned out to be the whole problem. The lip-sync animation was started on a wall
/// clock the moment Play() returned, but the sound itself began a second or more later
/// (device wake-up plus buffering), so the mouth ran its full 4.7 seconds and stopped
/// before a word came out. Calling Load() first did not fix it, because the latency is in
/// the device, not the file read.
///
/// MCI reports the clip's actual playback position in milliseconds, so the mouth can
/// follow the AUDIO's clock instead of a guess: while the sound is still spinning up the
/// position stays at 0 and the mouth simply waits. Whatever the latency turns out to be
/// on a given machine, the two stay in step.
///
/// MCI also handles the extracted format directly â€” those WAVs are 8-bit unsigned mono
/// PCM at 11025 Hz.
///
/// MUSIC AND EFFECTS take a different route, and not the obvious one. The 34 sound
/// resources were converted to MIDI early on, but they carry MT-32 program numbers rather
/// than General MIDI, so playing that MIDI on a GM synth gives the right notes with the
/// wrong instruments. The way out was not to invent a patch map: every one of those
/// resources ALSO holds an AdLib arrangement, and the game ships the matching AdLib
/// instrument definitions in <c>patch.003</c>. So the sound is synthesised here the way an
/// AdLib card did it â€” see <see cref="Jones.Audio.JonesAudioMixer"/> â€” and pushed at the
/// device as PCM through <see cref="WaveOutStream"/>. Nothing about the timbres is a
/// guess; they are read out of the game's own bank.
///
/// The synthesiser itself is in Jones.Audio, which is plain portable C#, so the planned
/// Android head needs only its own equivalent of WaveOutStream.
/// </summary>
public sealed class WindowsAudioPlayer : IAudioPlayer, IDisposable
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(
        string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    private const string Alias = "jonesSpeech";

    private readonly string _speechDir;
    private bool _open;


    // Built on a background thread, then read from the UI thread. A reference assignment
    // is atomic, and a caller that gets here a moment early simply finds null and does
    // nothing â€” which is the right answer, because there is no device to hear it on yet.
    private volatile Jones.Audio.JonesAudioMixer? _engine;
    private volatile WaveOutStream? _stream;

    private readonly Thread? _startingAudio;

    private bool _enabled = true;

    /// <summary>
    /// The sound switch. Turning it off stops what is playing; turning it back on leaves
    /// the game to start the next bed or effect, exactly as `soundOn` does in Sound.sc.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value)
            {
                // Silence comes from stopping, not from turning the volume down:
                // `sndMASTER_VOLUME` is a 0..15 scale folded into note velocity, and its
                // bottom is very quiet rather than silent. The original does the same â€”
                // `soundOn` gates every `play:` in Sound.sc, and the checks in PlayMusic
                // and PlayEffect below are that gate.
                StopSpeech();
                _engine?.StopAll();
            }
        }
    }

    public WindowsAudioPlayer(string assetRoot)
    {
        _speechDir = Path.Combine(assetRoot, "audio", "speech");

        // NOTHING IS LOADED FROM DISK HERE ANY MORE, and removing it fixed a real bug.
        // This used to read Sierra's AdLib instrument bank and `return` if it was missing,
        // which meant a build without those assets got NO AUDIO AT ALL — including the
        // original soundtrack, which never needed them. The music is compiled into the
        // assembly, so there is nothing left on disk to check for.

        // BOTH the synthesiser and the output device are built off the startup path,
        // because this constructor runs before Avalonia does and neither is quick:
        // rendering the stings once and opening the waveOut device both cost real time,
        // and done here that is seconds of title screen nobody asked for.
        //
        // Nothing is lost by deferring. The device cannot make a sound until it is open,
        // so a bed or a click asked for before then had nowhere to go anyway; the game is
        // several clicks from starting a turn by the time the audio is live.
        // A plain thread rather than Task.Run: the FIRST use of the thread pool in a
        // process costs about three seconds on this machine, and this constructor runs
        // early enough to be the one that pays it. Starting a thread does not touch the
        // pool, so the constructor comes back in single-digit milliseconds.
        _startingAudio = new Thread(StartAudio)
        {
            IsBackground = true,
            Name = "jones-audio-start",
        };
        _startingAudio.Start();
    }

    private void StartAudio()
    {
        try
        {
            // The soundtrack is built here, on the same off-startup thread: the six
            // synthesised effects and the short stings are rendered once so that the first
            // button click of a session is not also the first time anything has been
            // synthesised.
            var bank = new Jones.Audio.Original.OriginalSoundBank(Jones.Audio.JonesAudioMixer.SampleRate);
            bank.PreRenderEffects();

            var engine = new Jones.Audio.JonesAudioMixer(bank);

            // STEREO, where this used to be mono. The AdLib path is still a single mono
            // chip placed up the middle and sounds exactly as it did; the width is there
            // for the original synthesiser, whose chorus and delay are stereo effects and
            // are worth almost nothing folded down.
            _stream = new WaveOutStream(
                Jones.Audio.JonesAudioMixer.SampleRate,
                (buffer, count) => engine.RenderStereo(buffer.AsSpan(0, count)),
                channels: 2);

            // Published last: the play methods check this, and there is no point
            // accepting a sound before there is somewhere to put it.
            _engine = engine;
        }
        catch (Exception)
        {
            // A machine with no usable output device still gets a playable game.
            _stream = null;
            _engine = null;
        }
    }

    private static string? Query(string command)
    {
        var buf = new StringBuilder(128);
        return mciSendString(command, buf, buf.Capacity, IntPtr.Zero) == 0 ? buf.ToString() : null;
    }

    private void CloseCurrent()
    {
        if (!_open) return;
        mciSendString($"close {Alias}", null, 0, IntPtr.Zero);
        _open = false;
    }

    public void PlaySpeech(int audioId)
    {
        if (!Enabled) return;

        try
        {
            // The original never overlaps two lines.
            CloseCurrent();

            var path = Path.Combine(_speechDir, $"{audioId}.wav");
            if (!File.Exists(path)) return;

            // Quoting the path matters: MCI splits the command on spaces otherwise.
            if (mciSendString($"open \"{path}\" type waveaudio alias {Alias}", null, 0, IntPtr.Zero) != 0)
                return;

            _open = true;
            mciSendString($"set {Alias} time format milliseconds", null, 0, IntPtr.Zero);
            mciSendString($"play {Alias}", null, 0, IntPtr.Zero);
        }
        catch (Exception)
        {
            // A missing codec or a busy device must never take the game down.
        }
    }

    /// <summary>
    /// Where playback actually is. Null once the clip has finished or nothing is open,
    /// which is how the caller knows to stop the mouth.
    /// </summary>
    public TimeSpan? SpeechPosition
    {
        get
        {
            if (!_open) return null;

            // "mode" is one of: playing, stopped, paused, not ready, open, recording,
            // seeking. Anything other than playing means the line is over (or has not
            // begun), and a finished clip reports "stopped".
            var mode = Query($"status {Alias} mode");
            if (mode is null) return null;
            if (!mode.StartsWith("playing", StringComparison.OrdinalIgnoreCase)) return null;

            var pos = Query($"status {Alias} position");
            return int.TryParse(pos, out var ms) ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Closing the MCI device stops playback immediately, which is what every location's
    /// `(DoAudio audSTOP)` does as its dialog closes.
    /// </summary>
    public void StopSpeech() => CloseCurrent();

    /// <summary>
    /// `gASong playBed: n`. Every location has one (appliance.sc:119 is 40, bank.sc:103
    /// is 47, and so on) and the board's own theme is 5 (room1.sc:281).
    /// </summary>
    public void PlayMusic(int soundResource, bool loop = true)
    {
        if (!_enabled || _engine is null) return;
        _engine.PlayMusic(soundResource, loop);
    }

    /// <summary>
    /// `gASong fade:`, which is a fade and not a cut â€” the engine follows the arguments
    /// Sound.sc passes. Starting another bed cancels it, the same way one `gASong` object
    /// does in the original.
    /// </summary>
    public void StopMusic() => _engine?.StopMusic();

    /// <summary>`gASong stop:` â€” the cut, as distinct from the fade above.</summary>
    public void CutMusic() => _engine?.CutMusic();

    /// <summary>`gASong pause: 1` â€” the duck under a sting.</summary>
    public void PauseMusic() => _engine?.PauseMusic();

    /// <summary>
    /// `gASoundEffect play: n`. Resource 23 is the universal button click â€”
    /// `WButton::doit` plays it on every press (WButton.sc:152-153).
    /// </summary>
    public void PlayEffect(int soundResource, bool loop = false, bool resumeMusicWhenDone = false)
    {
        if (!_enabled || _engine is null) return;
        _engine.PlayEffect(soundResource, loop, resumeMusicWhenDone);
    }

    /// <summary>`(gASoundEffect loop: 1)` â€” `lottoScript.sc:232`.</summary>
    public void EndEffectLoop() => _engine?.EndEffectLoop();

    /// <summary>`gASoundEffect2 play: n` â€” `room1.sc:1500` is its only caller.</summary>
    public void PlayEffect2(int soundResource)
    {
        if (!_enabled || _engine is null) return;
        _engine.PlayEffect2(soundResource);
    }

    /// <summary>`winnerScript.sc:67-68` clears both effect slots.</summary>
    public void StopEffects() => _engine?.StopEffects();

    public void Dispose()
    {
        CloseCurrent();
        _engine?.StopAll();

        // The device may still be opening; closing it from under that would leave a
        // waveOut handle nobody owns.
        _startingAudio?.Join(TimeSpan.FromSeconds(10));

        _stream?.Dispose();
    }
}




