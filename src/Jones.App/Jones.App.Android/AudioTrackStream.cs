using System;
using System.Threading;
using AMedia = global::Android.Media;

namespace Jones.App.Android;

/// <summary>
/// Pushes 16-bit mono PCM at the device through an AudioTrack in STREAM mode, pulling it
/// from a callback on a dedicated thread.
///
/// This is the Android twin of the Windows head's <c>WaveOutStream</c>, and it exists for
/// the same reason: the music is not a file but an OPL2 being driven in real time, so a
/// bed can loop at its own marked loop point, fade over the five seconds the scripts ask
/// for, and have a button click land on top without either being re-rendered. That needs
/// a stream.
///
/// SIMPLER THAN THE WINDOWS ONE, in the one way that matters: AudioTrack.Write blocks
/// until the device has taken the samples, so the pump paces itself. WaveOutStream has to
/// poll WHDR_DONE and sleep because waveOutWrite returns immediately.
/// </summary>
internal sealed class AudioTrackStream : IDisposable
{
    // 1024 frames at 48 kHz is 21ms. Four of them queued is about 85ms, the same depth
    // the Windows head uses, so a button click reaches the speakers within a frame or so
    // of the screen updating.
    private const int FramesPerBuffer = 1024;
    private const int BufferCount = 4;

    private readonly Action<short[], int> _fill;
    private readonly short[] _staging = new short[FramesPerBuffer];

    private AMedia.AudioTrack? _track;
    private Thread? _pump;
    private volatile bool _running;

    /// <summary>True when the device opened and audio is actually flowing.</summary>
    public bool IsOpen => _track is not null;

    /// <param name="sampleRate">Samples per second; the synth's own rate is used as-is.</param>
    /// <param name="fill">Called on the pump thread to fill <c>count</c> mono samples.</param>
    public AudioTrackStream(int sampleRate, Action<short[], int> fill)
    {
        _fill = fill;

        // The device's own minimum, which varies by phone; never go below it or Write
        // underruns and the music stutters.
        var minBytes = AMedia.AudioTrack.GetMinBufferSize(
            sampleRate, AMedia.ChannelOut.Mono, AMedia.Encoding.Pcm16bit);
        if (minBytes <= 0) minBytes = FramesPerBuffer * BufferCount * 2;

        var bufferBytes = Math.Max(minBytes, FramesPerBuffer * BufferCount * 2);

        try
        {
            _track = new AMedia.AudioTrack.Builder()
                .SetAudioAttributes(new AMedia.AudioAttributes.Builder()!
                    // Game, not Media: the OS then ducks it for a call or a notification
                    // rather than treating it as something the user is listening to.
                    .SetUsage(AMedia.AudioUsageKind.Game)!
                    .SetContentType(AMedia.AudioContentType.Music)!
                    .Build()!)!
                .SetAudioFormat(new AMedia.AudioFormat.Builder()!
                    .SetEncoding(AMedia.Encoding.Pcm16bit)!
                    .SetSampleRate(sampleRate)!
                    .SetChannelMask(AMedia.ChannelOut.Mono)!
                    .Build()!)!
                .SetBufferSizeInBytes(bufferBytes)!
                .SetTransferMode(AMedia.AudioTrackMode.Stream)!
                .Build();

            if (_track.State != AMedia.AudioTrackState.Initialized)
            {
                _track.Release();
                _track = null;
                return;
            }

            _track.Play();
        }
        catch (Exception)
        {
            // A device that will not take this rate still gets a playable game, exactly as
            // on Windows.
            _track?.Release();
            _track = null;
            return;
        }

        _running = true;
        _pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = "jones-opl",
            // Above normal so a busy UI cannot starve the buffer queue into a dropout, but
            // not real-time: a glitch in the music must never cost input latency.
            Priority = ThreadPriority.AboveNormal,
        };
        _pump.Start();
    }

    private void Pump()
    {
        var track = _track;
        if (track is null) return;

        while (_running)
        {
            try
            {
                _fill(_staging, FramesPerBuffer);
            }
            catch (Exception)
            {
                // A fault in the synthesiser must not take the audio thread, and with it
                // the process, down. Push the silence we have and carry on.
                Array.Clear(_staging, 0, _staging.Length);
            }

            int written;
            try
            {
                // Blocking by default, which is the pacing: this returns when the device
                // has room for the next buffer.
                written = track.Write(_staging, 0, FramesPerBuffer);
            }
            catch (Exception)
            {
                return;
            }

            // Negative is an AudioTrack error code (ERROR_INVALID_OPERATION and friends);
            // there is nothing useful to do but stop feeding a dead track.
            if (written < 0) return;
        }
    }

    public void Dispose()
    {
        _running = false;

        var track = _track;
        _track = null;

        if (track is not null)
        {
            try
            {
                // Pause first: it makes a blocked Write return, so the pump is not still
                // sitting in the device when Release runs.
                track.Pause();
                track.Flush();
            }
            catch (Exception)
            {
            }
        }

        _pump?.Join(500);
        _pump = null;

        if (track is null) return;

        try
        {
            track.Stop();
        }
        catch (Exception)
        {
        }

        track.Release();
    }
}
