using System;
using System.IO;
using System.Threading;
using Jones.App.Audio;
using AContent = global::Android.Content;
using AMedia = global::Android.Media;

namespace Jones.App.Android;

/// <summary>
/// The Android <see cref="IAudioPlayer"/>. Same two routes as the Windows head, both
/// through AudioTrack.
///
/// SPEECH is a finished clip, played on a STATIC AudioTrack created per line. The
/// Windows head uses MCI for this and says why: the lip sync has to follow where playback
/// actually IS, not a wall clock started when Play() returned, because the device can
/// take a second to wake up and the mouth otherwise finishes before a word comes out.
/// AudioTrack answers that question better than MCI does - <c>PlaybackHeadPosition</c> is
/// a frame counter straight off the mixer, with no string parsing and no rounding to
/// whatever MCI feels like reporting. While the device is still spinning up it reads 0
/// and the mouth simply waits, which is exactly the behaviour
/// <see cref="IAudioPlayer.SpeechPosition"/> is specified to have.
///
/// The clips are 8-bit UNSIGNED mono PCM at 11025 Hz. AudioTrack has an 8-bit encoding,
/// but it is a legacy path that some devices convert badly, and converting a 40KB clip to
/// signed 16-bit here costs nothing and is what every mixer wants anyway. That conversion
/// is the only thing done to the samples.
///
/// The clips are read straight out of the APK through the AssetManager and never unpacked
/// - see <see cref="GameAssets"/> for why.
///
/// MUSIC AND EFFECTS are synthesised by <see cref="Jones.Audio.JonesAudioMixer"/>, which is
/// portable and shared with the Windows head, and pushed at the device through
/// <see cref="AudioTrackStream"/> - this head's equivalent of WaveOutStream. Nothing about
/// the timbres differs from Windows: both read the game's own AdLib bank out of
/// <c>patch.003</c>.
/// </summary>
public sealed class AndroidAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly AContent.Res.AssetManager _assets;


    // Built on a background thread, then read from the UI thread. A reference assignment
    // is atomic, and a caller that gets here a moment early simply finds null and does
    // nothing - which is the right answer, because there is no device to hear it on yet.
    private volatile Jones.Audio.JonesAudioMixer? _engine;
    private volatile AudioTrackStream? _stream;

    private readonly Thread? _startingAudio;

    // Speech is created and torn down per line from the UI thread, and its position is
    // polled from the UI thread, but Dispose can arrive from anywhere.
    private readonly object _speechGate = new();
    private AMedia.AudioTrack? _speech;
    private int _speechFrames;
    private int _speechRate = 11025;

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
                StopSpeech();
                _engine?.StopAll();
            }
        }
    }

    /// <param name="assets">The APK's assets, where the speech clips live.</param>
    /// <param name="assetRoot">The unpacked root, where the AdLib bank and sounds live.</param>
    public AndroidAudioPlayer(AContent.Res.AssetManager assets, string assetRoot)
    {
        _assets = assets;

        // NOTHING IS LOADED FROM DISK HERE ANY MORE, and that fixed a real bug. This used
        // to read Sierra's AdLib bank and `return` if it was missing or malformed - which
        // meant an unpack that half-succeeded left the phone with NO AUDIO AT ALL,
        // including the original soundtrack, which never needed those files. The music is
        // compiled into the assembly, so there is nothing here that can fail.

        // Off the startup path for the same reason as on Windows: standing up the two OPL
        // emulators is one-time JIT, and opening an output device is not instant on a
        // phone either. Nothing is lost by deferring - the device cannot make a sound
        // until it is open, so a bed asked for before then had nowhere to go anyway.
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
            // The six synthesised effects and the short stings are rendered here, on this
            // same off-startup thread, so the first button click of a session does not pay
            // for them. A few megabytes at worst, and only what the player triggers is kept.
            var bank = new Jones.Audio.Original.OriginalSoundBank(Jones.Audio.JonesAudioMixer.SampleRate);
            bank.PreRenderEffects();

            var engine = new Jones.Audio.JonesAudioMixer(bank);

            // STEREO, where this used to be mono. The AdLib path is still one mono chip up
            // the middle and sounds exactly as it did; the width is for the original
            // synthesiser, and on this head it is what headphones will actually show.
            _stream = new AudioTrackStream(
                Jones.Audio.JonesAudioMixer.SampleRate,
                (buffer, count) => engine.RenderStereo(buffer.AsSpan(0, count)),
                channels: 2);

            // Published last: the play methods check this, and there is no point accepting
            // a sound before there is somewhere to put it.
            _engine = engine;
        }
        catch (Exception e)
        {
            // Logged rather than swallowed: this runs on a background thread, so without a
            // line here a phone whose mixer refuses the output format is indistinguishable
            // from a phone whose speakers are simply turned down.
            AndroidLog.Error("audio device start (music and effects disabled)", e);
            _stream = null;
            _engine = null;
        }
    }

    // ------------------------------------------------------------------
    // Speech
    // ------------------------------------------------------------------

    public void PlaySpeech(int audioId)
    {
        if (!Enabled) return;

        try
        {
            // The original never overlaps two lines.
            StopSpeech();

            var wav = ReadAsset($"{GameAssets.SpeechAssetDir}/{audioId}.wav");
            if (wav is null) return;

            if (!TryDecode(wav, out var samples, out var rate) || samples.Length == 0) return;

            var track = new AMedia.AudioTrack.Builder()
                .SetAudioAttributes(new AMedia.AudioAttributes.Builder()!
                    .SetUsage(AMedia.AudioUsageKind.Game)!
                    .SetContentType(AMedia.AudioContentType.Speech)!
                    .Build()!)!
                .SetAudioFormat(new AMedia.AudioFormat.Builder()!
                    .SetEncoding(AMedia.Encoding.Pcm16bit)!
                    .SetSampleRate(rate)!
                    .SetChannelMask(AMedia.ChannelOut.Mono)!
                    .Build()!)!
                .SetBufferSizeInBytes(samples.Length * 2)!
                // STATIC, not STREAM: the whole clip is handed over once, which is what
                // makes PlaybackHeadPosition a straight read of how far in the line is.
                .SetTransferMode(AMedia.AudioTrackMode.Static)!
                .Build();

            if (track.State != AMedia.AudioTrackState.Initialized)
            {
                track.Release();
                return;
            }

            // In static mode the samples go in BEFORE Play; there is no pump thread.
            track.Write(samples, 0, samples.Length);
            track.Play();

            lock (_speechGate)
            {
                _speech = track;
                _speechFrames = samples.Length;
                _speechRate = rate;
            }
        }
        catch (Exception)
        {
            // A missing clip or a busy device must never take the game down.
        }
    }

    /// <summary>
    /// Where playback actually is. Null once the clip has finished or nothing is playing,
    /// which is how the caller knows to stop the mouth.
    /// </summary>
    public TimeSpan? SpeechPosition
    {
        get
        {
            lock (_speechGate)
            {
                if (_speech is not { } track) return null;

                try
                {
                    if (track.PlayState != AMedia.PlayState.Playing) return null;

                    var frames = track.PlaybackHeadPosition;

                    // A static track that has run out stops itself, but not always before
                    // the next poll, and the mouth must not be handed a position past the
                    // end of the clip.
                    if (frames >= _speechFrames) return null;

                    return TimeSpan.FromSeconds((double)frames / _speechRate);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }

    /// <summary>
    /// Cuts the line off mid-word, which is what every location's `(DoAudio audSTOP)` does
    /// as its dialog closes.
    /// </summary>
    public void StopSpeech()
    {
        AMedia.AudioTrack? track;
        lock (_speechGate)
        {
            track = _speech;
            _speech = null;
            _speechFrames = 0;
        }

        if (track is null) return;

        try
        {
            // Stop alone, with no Pause/Flush: on a STATIC track flush is not supported
            // and only logs a complaint, and stop already halts playback immediately -
            // which is what `(DoAudio audSTOP)` asks for.
            track.Stop();
        }
        catch (Exception)
        {
        }

        track.Release();
    }

    private byte[]? ReadAsset(string path)
    {
        try
        {
            using var stream = _assets.Open(path);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception)
        {
            // Ten of the 543 sync resources have no audio and the reverse happens too; a
            // clip that is not there is not an error.
            return null;
        }
    }

    /// <summary>
    /// Pulls mono 16-bit signed samples out of a RIFF/WAVE file, converting from 8-bit
    /// unsigned if that is what it holds - which for this game's speech it always is.
    ///
    /// Chunks are walked rather than assumed at fixed offsets: the extraction wrote plain
    /// 44-byte headers, but a WAV reader that trusts that is a reader that breaks the day
    /// someone re-exports the set with a LIST chunk in it.
    /// </summary>
    private static bool TryDecode(byte[] wav, out short[] samples, out int sampleRate)
    {
        samples = Array.Empty<short>();
        sampleRate = 11025;

        if (wav.Length < 12) return false;
        if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return false;
        if (wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E') return false;

        var channels = 1;
        var bits = 8;
        var haveFormat = false;

        var at = 12;
        while (at + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, at, 4);
            var size = BitConverter.ToInt32(wav, at + 4);
            var body = at + 8;
            if (size < 0 || body + size > wav.Length) size = wav.Length - body;

            if (id == "fmt " && size >= 16)
            {
                channels = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToInt16(wav, body + 14);
                haveFormat = true;
            }
            else if (id == "data" && haveFormat)
            {
                if (channels < 1) return false;

                if (bits == 8)
                {
                    // Unsigned, centred on 128. Shifting left by 8 puts it in the top byte
                    // of a signed 16-bit sample, which is the exact same waveform at the
                    // same level - no gain change, no dither, nothing invented.
                    var count = size / channels;
                    var result = new short[count];
                    for (var i = 0; i < count; i++)
                        result[i] = (short)((wav[body + i * channels] - 128) << 8);
                    samples = result;
                    return true;
                }

                if (bits == 16)
                {
                    var count = size / (2 * channels);
                    var result = new short[count];
                    for (var i = 0; i < count; i++)
                        result[i] = BitConverter.ToInt16(wav, body + i * 2 * channels);
                    samples = result;
                    return true;
                }

                return false;
            }

            // Chunks are word aligned.
            at = body + size + (size & 1);
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Music and effects - identical to the Windows head, because the engine is shared
    // ------------------------------------------------------------------

    /// <summary>`gASong playBed: n`.</summary>
    public void PlayMusic(int soundResource, bool loop = true)
    {
        if (!_enabled || _engine is null) return;
        _engine.PlayMusic(soundResource, loop);
    }

    /// <summary>`gASong fade:` - a fade, not a cut.</summary>
    public void StopMusic() => _engine?.StopMusic();

    /// <summary>`gASong stop:` - the cut.</summary>
    public void CutMusic() => _engine?.CutMusic();

    /// <summary>`gASong pause: 1` - the duck under a sting.</summary>
    public void PauseMusic() => _engine?.PauseMusic();

    /// <summary>`gASoundEffect play: n`.</summary>
    public void PlayEffect(int soundResource, bool loop = false, bool resumeMusicWhenDone = false)
    {
        if (!_enabled || _engine is null) return;
        _engine.PlayEffect(soundResource, loop, resumeMusicWhenDone);
    }

    /// <summary>`(gASoundEffect loop: 1)` - `lottoScript.sc:232`.</summary>
    public void EndEffectLoop() => _engine?.EndEffectLoop();

    /// <summary>`gASoundEffect2 play: n` - `room1.sc:1500` is its only caller.</summary>
    public void PlayEffect2(int soundResource)
    {
        if (!_enabled || _engine is null) return;
        _engine.PlayEffect2(soundResource);
    }

    /// <summary>`winnerScript.sc:67-68` clears both effect slots.</summary>
    public void StopEffects() => _engine?.StopEffects();

    public void Dispose()
    {
        StopSpeech();
        _engine?.StopAll();

        // The device may still be opening; tearing it down from under that would leave an
        // AudioTrack nobody owns.
        _startingAudio?.Join(TimeSpan.FromSeconds(10));

        _stream?.Dispose();
    }
}




