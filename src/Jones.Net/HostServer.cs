using System.Net;
using System.Net.Sockets;

namespace Jones.Net;

/// <summary>
/// The host's end: listens, seats joiners, hands their input up, and broadcasts frames and
/// sound cues down. Knows nothing about the game — the app layer decides what a frame is and
/// what an input does.
///
/// Events are raised on network threads; the app marshals them to the UI thread.
/// </summary>
public sealed class HostServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly object _gate = new();
    private readonly Dictionary<NetLink, int> _seatOf = new();
    private volatile bool _stopped;

    public HostServer(int port, IPAddress? bind = null)
    {
        _listener = new TcpListener(bind ?? IPAddress.Any, port);
    }

    public SeatTable Seats { get; } = new();

    /// <summary>The port actually bound — useful when constructed with port 0.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>A joiner has been seated. Send them the current frame.</summary>
    public event Action<int>? Joined;

    /// <summary>A joiner has gone. Their seat is kept for them.</summary>
    public event Action<int>? Left;

    /// <summary>A seated joiner did something. Whether it is allowed is the app's decision.</summary>
    public event Action<int, InputMsg>? Input;

    public void Start()
    {
        _listener.Start();
        new Thread(AcceptLoop) { IsBackground = true, Name = "Jones.Net accept" }.Start();
    }

    public int ConnectedCount
    {
        get { lock (_gate) return _seatOf.Count; }
    }

    private void AcceptLoop()
    {
        while (!_stopped)
        {
            TcpClient tcp;
            try
            {
                tcp = _listener.AcceptTcpClient();
            }
            catch (SocketException) { if (_stopped) return; continue; }
            catch (ObjectDisposedException) { return; }

            var link = new NetLink(tcp);
            link.Received += OnReceived;
            link.Closed += OnClosed;
            link.Start();
        }
    }

    private void OnReceived(NetLink link, Envelope env)
    {
        int seat;
        lock (_gate) seat = _seatOf.TryGetValue(link, out var s) ? s : -1;

        if (seat < 0)
        {
            // Nothing but a hello is accepted from a link that has not been seated.
            if (env.Hello is not { } hello) return;

            if (hello.Protocol != ProtocolInfo.Version)
            {
                Refuse(link, $"protocol {hello.Protocol}, host speaks {ProtocolInfo.Version}");
                return;
            }

            var claimed = Seats.Claim(hello.Token);
            if (claimed < 0)
            {
                Refuse(link, "no free seat");
                return;
            }

            lock (_gate) _seatOf[link] = claimed;
            link.Send(new Envelope { Welcome = new WelcomeMsg { Seat = claimed } });
            Joined?.Invoke(claimed);
            return;
        }

        if (env.Input is { } input) Input?.Invoke(seat, input);
    }

    private static void Refuse(NetLink link, string reason)
    {
        link.Send(new Envelope { Welcome = new WelcomeMsg { Seat = -1, Reason = reason } });

        // Give the writer a moment to get the refusal out before the socket goes.
        ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(250); link.Close(); });
    }

    private void OnClosed(NetLink link)
    {
        int seat;
        lock (_gate)
        {
            if (!_seatOf.Remove(link, out seat)) return;
        }

        Seats.Disconnect(seat);
        Left?.Invoke(seat);
    }

    private NetLink[] Links()
    {
        lock (_gate) return _seatOf.Keys.ToArray();
    }

    /// <summary>An encoded frame, to every seated joiner. Coalesced per link.</summary>
    public void BroadcastFrame(byte[] line)
    {
        foreach (var link in Links()) link.SendFrame(line);
    }

    /// <summary>An encoded frame, to one seat only — the catch-up for a new joiner.</summary>
    public void SendFrameTo(int seat, byte[] line)
    {
        foreach (var link in LinksFor(seat)) link.SendFrame(line);
    }

    /// <summary>An ordered message to every seated joiner.</summary>
    public void Broadcast(Envelope env)
    {
        var links = Links();
        if (links.Length == 0) return;

        var line = Wire.Encode(env);
        foreach (var link in links) link.SendRaw(line);
    }

    private NetLink[] LinksFor(int seat)
    {
        lock (_gate) return _seatOf.Where(p => p.Value == seat).Select(p => p.Key).ToArray();
    }

    public void Dispose()
    {
        _stopped = true;
        try { _listener.Stop(); } catch { }
        foreach (var link in Links()) link.Close();
    }
}
