using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Jones.App.Desktop;

/// <summary>
/// Pushes 16-bit mono PCM at a sound device through winmm's waveOut, pulling it from a
/// callback on a dedicated thread.
///
/// WHY NOT MCI, which this head already uses for speech: MCI plays a finished FILE. The
/// music here is not a file â€” it is an OPL2 being driven in real time, so that a music
/// bed can loop at its own marked loop point, fade out over the five seconds the scripts
/// ask for, and have a button click land over the top of it without either one being
/// re-rendered. That needs a stream, and waveOut is the streaming interface.
///
/// WHY NOT waveOut's own CALLBACK_FUNCTION: callbacks from winmm run in a restricted
/// context where most of the API is off limits, and a managed delegate there is a source
/// of hard-to-find deadlocks. Polling WHDR_DONE from an ordinary thread has none of those
/// rules and the poll interval is far shorter than a buffer.
///
/// Windows only, which is why it lives in the Desktop head and not in Jones.App.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WaveOutStream : IDisposable
{
    // Four buffers of 1024 frames is about 82 ms of queued audio at the OPL's rate, so a
    // button click reaches the speakers within roughly one frame of the screen updating.
    private const int BufferCount = 4;
    private const int FramesPerBuffer = 1024;

    private const int WaveMapper = -1;
    private const int CallbackNull = 0x00000000;
    private const int WhdrDone = 0x00000001;
    private const int WhdrPrepared = 0x00000002;
    private const int MmsyserrNoerror = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public short wFormatTag;
        public short nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public short nBlockAlign;
        public short wBitsPerSample;
        public short cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr lpData;
        public int dwBufferLength;
        public int dwBytesRecorded;
        public IntPtr dwUser;
        public int dwFlags;
        public int dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr hwo, int deviceId, ref WaveFormatEx format,
        IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hwo, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr hwo);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hwo);

    private readonly Action<short[], int> _fill;
    private readonly short[] _staging;
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _data = new IntPtr[BufferCount];
    private readonly int _headerSize = Marshal.SizeOf<WaveHdr>();

    private IntPtr _device;
    private Thread? _pump;
    private volatile bool _running;

    /// <summary>True when the device opened and audio is actually flowing.</summary>
    public bool IsOpen => _device != IntPtr.Zero;

    /// <summary>Samples per buffer: frames x channels.</summary>
    private readonly int _samplesPerBuffer;

    /// <param name="sampleRate">Samples per second; the OPL's native rate is used as-is.</param>
    /// <param name="fill">Called on the pump thread to fill <c>count</c> samples.</param>
    /// <param name="channels">
    /// 1 for the mono AdLib path this stream was written for, 2 for the interleaved stereo
    /// the original synthesiser produces — its chorus and its delay are stereo effects and
    /// folding them down throws away most of what they do. The mono path is unchanged and
    /// still the default.
    /// </param>
    public WaveOutStream(int sampleRate, Action<short[], int> fill, int channels = 1)
    {
        if (channels is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channels));

        _fill = fill;
        _samplesPerBuffer = FramesPerBuffer * channels;
        _staging = new short[_samplesPerBuffer];

        var blockAlign = (short)(channels * 2);
        var format = new WaveFormatEx
        {
            wFormatTag = 1,          // WAVE_FORMAT_PCM
            nChannels = (short)channels,
            nSamplesPerSec = sampleRate,
            nAvgBytesPerSec = sampleRate * blockAlign,
            nBlockAlign = blockAlign,
            wBitsPerSample = 16,
            cbSize = 0,
        };

        if (waveOutOpen(out _device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull)
            != MmsyserrNoerror)
        {
            // No device, or one that will not take this rate. The game goes on in silence.
            _device = IntPtr.Zero;
            return;
        }

        for (var i = 0; i < BufferCount; i++)
        {
            _data[i] = Marshal.AllocHGlobal(_samplesPerBuffer * 2);
            _headers[i] = Marshal.AllocHGlobal(_headerSize);
            var hdr = new WaveHdr { lpData = _data[i], dwBufferLength = _samplesPerBuffer * 2 };
            Marshal.StructureToPtr(hdr, _headers[i], false);
            waveOutPrepareHeader(_device, _headers[i], _headerSize);
            // Mark done so the first pass through the pump fills and queues all of them.
            MarkDone(i);
        }

        _running = true;
        _pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = "jones-opl",
            // Above normal so a busy UI cannot starve the buffer queue into a dropout,
            // but not real-time: a glitch in the music must never cost input latency.
            Priority = ThreadPriority.AboveNormal,
        };
        _pump.Start();
    }

    private void MarkDone(int i)
    {
        var hdr = Marshal.PtrToStructure<WaveHdr>(_headers[i]);
        hdr.dwFlags |= WhdrDone;
        Marshal.StructureToPtr(hdr, _headers[i], false);
    }

    private void Pump()
    {
        while (_running)
        {
            var queued = false;

            for (var i = 0; i < BufferCount && _running; i++)
            {
                var hdr = Marshal.PtrToStructure<WaveHdr>(_headers[i]);
                if ((hdr.dwFlags & WhdrDone) == 0) continue;

                try
                {
                    _fill(_staging, _samplesPerBuffer);
                }
                catch (Exception)
                {
                    // A fault in the synthesiser must not take the audio thread, and with
                    // it the process, down. Push the silence we have and carry on.
                    Array.Clear(_staging);
                }

                Marshal.Copy(_staging, 0, _data[i], _samplesPerBuffer);

                hdr.dwFlags = WhdrPrepared;   // clears DONE and INQUEUE

                // BYTES, so frames x channels x 2 — `_samplesPerBuffer * 2`, exactly as the
                // allocation at init uses. This read `FramesPerBuffer * 2`, which is the same
                // number ONLY in mono; in stereo it is half the buffer, so every re-queue
                // played the first 512 frames of 1024 and then jumped to the next buffer,
                // truncating mid-frame and swapping which channel led. That is why the music
                // sounded broken in the game while the rendered WAVs — which never touch this
                // path — were fine.
                hdr.dwBufferLength = _samplesPerBuffer * 2;
                Marshal.StructureToPtr(hdr, _headers[i], false);

                if (waveOutWrite(_device, _headers[i], _headerSize) != MmsyserrNoerror)
                    MarkDone(i);
                else
                    queued = true;
            }

            // Every buffer is still in flight: wait a fraction of one buffer's duration.
            if (!queued) Thread.Sleep(5);
        }
    }

    public void Dispose()
    {
        _running = false;
        _pump?.Join(500);
        _pump = null;

        if (_device == IntPtr.Zero) return;

        waveOutReset(_device);

        for (var i = 0; i < BufferCount; i++)
        {
            if (_headers[i] != IntPtr.Zero)
            {
                waveOutUnprepareHeader(_device, _headers[i], _headerSize);
                Marshal.FreeHGlobal(_headers[i]);
                _headers[i] = IntPtr.Zero;
            }
            if (_data[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_data[i]);
                _data[i] = IntPtr.Zero;
            }
        }

        waveOutClose(_device);
        _device = IntPtr.Zero;
    }
}

