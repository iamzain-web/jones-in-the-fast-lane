using System;
using System.Collections.Specialized;
using Avalonia.Threading;
using Jones.App.ViewModels;
using Jones.Net;

namespace Jones.App.Net;

/// <summary>
/// Hosting: the one real game, shown to every joiner and driven by whoever owns the acting
/// seat. Started by <c>--host</c>.
///
/// <list type="bullet">
/// <item><b>Frames.</b> Any change to the screen's collections marks the screen dirty; a
/// 50ms tick captures it, and sends it only if it differs from the last one sent. Walking
/// rebuilds the board many times a second, so this is what caps the traffic.</item>
/// <item><b>Input.</b> A joiner's input is carried out only if the joiner owns
/// <see cref="MainViewModel.ActingSeat"/> at the moment it arrives. The host's own mouse and
/// keyboard are turned away by <see cref="InputGate"/> while a connected joiner owns it.</item>
/// <item><b>Sound.</b> <see cref="BroadcastAudioPlayer"/> replaces the player, so every cue
/// reaches the joiners too.</item>
/// </list>
/// </summary>
public sealed class NetHost : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly HostServer _server;
    private readonly DispatcherTimer _pump;

    private bool _dirty = true;
    private int _seq;
    private byte[]? _lastLine;
    private string? _lastJson;

    private NetHost(MainViewModel vm, int port)
    {
        _vm = vm;
        _server = new HostServer(port);

        MainViewModel.Sound = new BroadcastAudioPlayer(MainViewModel.Sound,
            msg => _server.Broadcast(new Envelope { Audio = msg }));

        InputGate.LocalMayAct = () => _server.Seats.HostMayAct(_vm.ActingSeat);

        NotifyCollectionChangedEventHandler dirty = (_, _) => _dirty = true;
        vm.Sprites.CollectionChanged += dirty;
        vm.Hotspots.CollectionChanged += dirty;
        vm.Texts.CollectionChanged += dirty;
        vm.MenuLines.CollectionChanged += dirty;
        vm.GoalTracks.CollectionChanged += dirty;
        vm.Balloon.CollectionChanged += dirty;
        vm.BalloonButtons.CollectionChanged += dirty;
        vm.PropertyChanged += (_, _) => _dirty = true;

        _server.Joined += seat => Dispatcher.UIThread.Post(() =>
        {
            StartupLog.Say($"network: player {seat + 1} joined");
            SendCurrentFrameTo(seat);
        });
        _server.Left += seat => Dispatcher.UIThread.Post(() =>
            StartupLog.Say($"network: player {seat + 1} left; the seat is kept, and played here until they return"));
        _server.Input += (seat, input) => Dispatcher.UIThread.Post(() => OnInput(seat, input));

        _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _pump.Tick += (_, _) => Pump();
    }

    /// <summary>Starts listening. Throws if the port cannot be bound.</summary>
    public static NetHost Start(MainViewModel vm, int port)
    {
        var host = new NetHost(vm, port);
        host._server.Start();
        host._pump.Start();
        StartupLog.Say($"network: hosting on port {host._server.Port}");
        return host;
    }

    public SeatTable Seats => _server.Seats;

    private void OnInput(int seat, InputMsg input)
    {
        if (!_server.Seats.JoinerMayAct(_vm.ActingSeat, seat)) return;

        _vm.ExecuteRemoteInput(input);

        // Answer at once rather than on the next tick: the joiner is waiting to see what
        // their own click did, and that is the one moment latency is felt.
        Pump();
    }

    private void Pump()
    {
        if (!_dirty || _server.ConnectedCount == 0) return;
        _dirty = false;

        var frame = _vm.CaptureFrame(0);

        // The sequence number goes on AFTER the comparison, so an unchanged screen is
        // recognised as unchanged.
        var json = System.Text.Json.JsonSerializer.Serialize(frame, Wire.Options);
        if (json == _lastJson) return;
        _lastJson = json;

        frame.Seq = ++_seq;
        _lastLine = Wire.Encode(new Envelope { Frame = frame });
        _server.BroadcastFrame(_lastLine);
    }

    /// <summary>
    /// The newcomer needs the screen as it is NOW, and the last frame sent may be stale — no
    /// frames go out while nobody is connected. A fresh capture goes to everyone, which is
    /// harmless: the others were about to be sent any change anyway.
    /// </summary>
    private void SendCurrentFrameTo(int seat)
    {
        _dirty = true;
        _lastJson = null;
        Pump();
        if (_lastLine is not null) _server.SendFrameTo(seat, _lastLine);
    }

    public void Dispose()
    {
        _pump.Stop();
        _server.Dispose();
    }
}
