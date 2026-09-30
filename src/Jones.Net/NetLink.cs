using System.Net.Sockets;
using System.Text;

namespace Jones.Net;

/// <summary>
/// One TCP connection carrying <see cref="Envelope"/> lines, in both directions.
///
/// <para>
/// Reading and writing each have their own background thread, so the UI thread never
/// blocks on the network: <see cref="Send"/> only queues. Events are raised ON THE READER
/// THREAD — the caller marshals to its own.
/// </para>
///
/// <para>
/// FRAMES COALESCE. A frame is a complete picture of the screen, so if a slow link has not
/// sent the last one yet, the new one replaces it rather than queueing behind it: a joiner
/// on a poor connection sees fewer frames, never an ever-growing delay. Everything else
/// (the handshake, input, sound cues) is ordered and never dropped.
/// </para>
/// </summary>
public sealed class NetLink : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;

    private readonly object _gate = new();
    private readonly Queue<byte[]> _ordered = new();
    private byte[]? _frame;
    private readonly AutoResetEvent _wake = new(false);

    private int _closed;

    public NetLink(TcpClient tcp)
    {
        _tcp = tcp;
        _tcp.NoDelay = true;
        _stream = tcp.GetStream();
    }

    /// <summary>Raised on the reader thread for each well-formed line.</summary>
    public event Action<NetLink, Envelope>? Received;

    /// <summary>Raised once, from whichever thread noticed the connection had gone.</summary>
    public event Action<NetLink>? Closed;

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public void Start()
    {
        new Thread(ReadLoop) { IsBackground = true, Name = "Jones.Net read" }.Start();
        new Thread(WriteLoop) { IsBackground = true, Name = "Jones.Net write" }.Start();
    }

    public void Send(Envelope e) => SendRaw(Wire.Encode(e));

    /// <summary>An already-encoded line, so one frame can be encoded once and sent to many.</summary>
    public void SendRaw(byte[] line)
    {
        if (!IsOpen) return;
        lock (_gate) _ordered.Enqueue(line);
        _wake.Set();
    }

    /// <summary>An encoded frame line. Replaces any frame not yet written.</summary>
    public void SendFrame(byte[] line)
    {
        if (!IsOpen) return;
        lock (_gate) _frame = line;
        _wake.Set();
    }

    private void ReadLoop()
    {
        try
        {
            using var reader = new StreamReader(_stream, Encoding.UTF8, false, 1 << 16, leaveOpen: true);
            while (reader.ReadLine() is { } line)
            {
                if (Wire.Decode(line) is { } env) Received?.Invoke(this, env);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }

        Close();
    }

    private void WriteLoop()
    {
        try
        {
            while (IsOpen)
            {
                _wake.WaitOne(500);

                while (true)
                {
                    byte[]? next;
                    lock (_gate)
                    {
                        if (_ordered.Count > 0) next = _ordered.Dequeue();
                        else { next = _frame; _frame = null; }
                    }

                    if (next is null) break;
                    _stream.Write(next, 0, next.Length);
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }

        Close();
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;

        try { _tcp.Close(); } catch { }
        _wake.Set();
        Closed?.Invoke(this);
    }

    public void Dispose() => Close();
}
