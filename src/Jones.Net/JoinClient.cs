using System.Net.Sockets;

namespace Jones.Net;

/// <summary>
/// The joiner's end: connects, says hello, then receives frames and sound cues and sends
/// input. Reconnects by itself, with the same token, whenever the link drops, so a Wi-Fi
/// blip costs a moment's frozen screen rather than the seat.
///
/// Events are raised on network threads; the app marshals them to the UI thread.
/// </summary>
public sealed class JoinClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private volatile NetLink? _link;
    private volatile bool _stopped;
    private readonly AutoResetEvent _dropped = new(false);

    public JoinClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    /// <summary>Stable for the life of this process, so a reconnect is recognised.</summary>
    public string Token { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The seat the host gave us, or -1 before the welcome / after a refusal.</summary>
    public int Seat { get; private set; } = -1;

    /// <summary>How long to wait between connection attempts.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public event Action<int>? Welcomed;

    /// <summary>The host turned us away. The client stops trying.</summary>
    public event Action<string?>? Refused;

    public event Action<FrameMsg>? Frame;
    public event Action<AudioMsg>? Audio;
    public event Action? Disconnected;

    /// <summary>Why the last attempt failed, for the log.</summary>
    public event Action<string>? Trouble;

    public void Start()
    {
        new Thread(ConnectLoop) { IsBackground = true, Name = "Jones.Net join" }.Start();
    }

    public void Send(InputMsg input) => _link?.Send(new Envelope { Input = input });

    private void ConnectLoop()
    {
        while (!_stopped)
        {
            try
            {
                var tcp = new TcpClient();
                tcp.Connect(_host, _port);

                var link = new NetLink(tcp);
                link.Received += OnReceived;
                link.Closed += _ => _dropped.Set();
                _link = link;
                link.Start();
                link.Send(new Envelope { Hello = new HelloMsg { Protocol = ProtocolInfo.Version, Token = Token } });

                _dropped.WaitOne();
                _link = null;
                if (!_stopped) Disconnected?.Invoke();
            }
            catch (SocketException ex)
            {
                Trouble?.Invoke(ex.Message);
            }

            if (!_stopped) Thread.Sleep(RetryDelay);
        }
    }

    private void OnReceived(NetLink link, Envelope env)
    {
        if (env.Welcome is { } welcome)
        {
            if (welcome.Seat < 0)
            {
                _stopped = true;
                Refused?.Invoke(welcome.Reason);
                link.Close();
                return;
            }

            Seat = welcome.Seat;
            Welcomed?.Invoke(welcome.Seat);
            return;
        }

        if (env.Frame is { } frame) Frame?.Invoke(frame);
        if (env.Audio is { } audio) Audio?.Invoke(audio);
    }

    public void Dispose()
    {
        _stopped = true;
        _link?.Close();
        _dropped.Set();
    }
}
