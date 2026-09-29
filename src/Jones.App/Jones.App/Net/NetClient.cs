using System;
using System.Threading;
using Avalonia.Threading;
using Jones.App.ViewModels;
using Jones.Net;

namespace Jones.App.Net;

/// <summary>
/// Joining: a view model with no game of its own, mirroring the host's screen and sending
/// this machine's clicks and keys back. Started by <c>--join</c>.
///
/// <para>
/// FRAMES ARE COALESCED HERE TOO. If the UI thread is still drawing the last frame when the
/// next arrives, only the newest is kept — the same rule the link applies on the way out.
/// </para>
///
/// <para>
/// Connection progress goes to the startup log and nowhere else. The game has no text for
/// "connecting" or "the host has gone", and inventing some would break CLAUDE.md §1; the
/// screen simply stays as it was until the host is back.
/// </para>
/// </summary>
public static class NetClient
{
    public static MainViewModel Start(string host, int port)
    {
        var vm = MainViewModel.CreateRemoteView();
        var client = new JoinClient(host, port);

        vm.SendInput = client.Send;

        FrameMsg? pending = null;
        client.Frame += frame =>
        {
            if (Interlocked.Exchange(ref pending, frame) is not null) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (Interlocked.Exchange(ref pending, null) is { } latest) vm.ApplyFrame(latest);
            });
        };

        client.Audio += audio => Dispatcher.UIThread.Post(() => MainViewModel.PlayRemoteAudio(audio));

        client.Welcomed += seat => StartupLog.Say($"network: joined {host}:{port} as player {seat + 1}");
        client.Refused += why => StartupLog.Say($"network: {host}:{port} refused us: {why}");
        client.Disconnected += () => StartupLog.Say($"network: lost {host}:{port}; reconnecting");
        client.Trouble += why => StartupLog.Say($"network: cannot reach {host}:{port}: {why}");

        client.Start();
        StartupLog.Say($"network: joining {host}:{port}");
        return vm;
    }
}

/// <summary>
/// What the command line asked for. Set by the head before the app starts; read by
/// <see cref="App"/> when it builds the first view model.
///
/// <code>
/// --host [port]          host a game others can join
/// --join address[:port]  join someone else's
/// </code>
/// </summary>
public static class NetLaunch
{
    public static bool Host { get; private set; }
    public static string? JoinAddress { get; private set; }
    public static int Port { get; private set; } = ProtocolInfo.DefaultPort;

    public static void Parse(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host":
                    Host = true;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) { Port = p; i++; }
                    break;

                case "--join" when i + 1 < args.Length:
                    var target = args[++i];
                    var colon = target.LastIndexOf(':');
                    if (colon > 0 && int.TryParse(target[(colon + 1)..], out var jp))
                    {
                        Port = jp;
                        target = target[..colon];
                    }
                    JoinAddress = target;
                    break;
            }
        }
    }
}
