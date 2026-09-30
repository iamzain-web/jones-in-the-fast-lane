using System.Net;
using Jones.Net;

namespace Jones.Tests;

/// <summary>
/// Network play's plumbing: the seat rules, the wire format, and a real host and joiner
/// talking over loopback. The app-side half (what a frame contains, what an input does)
/// lives in Jones.App, which this project does not reference.
/// </summary>
public class SeatTableTests
{
    [Fact]
    public void JoinersTakeSeatsOneTwoThreeInOrder()
    {
        var seats = new SeatTable();
        Assert.Equal(1, seats.Claim("a"));
        Assert.Equal(2, seats.Claim("b"));
        Assert.Equal(3, seats.Claim("c"));
        Assert.Equal(-1, seats.Claim("d"));
    }

    [Fact]
    public void SeatZeroIsAlwaysTheHosts()
    {
        var seats = new SeatTable();
        seats.Claim("a");
        Assert.False(seats.IsRemote(0));
        Assert.True(seats.HostMayAct(0));
        Assert.False(seats.JoinerMayAct(0, 1));
    }

    [Fact]
    public void OnlyTheOwnerActsForARemoteSeat()
    {
        var seats = new SeatTable();
        var a = seats.Claim("a");
        var b = seats.Claim("b");

        Assert.False(seats.HostMayAct(a));
        Assert.True(seats.JoinerMayAct(a, a));
        Assert.False(seats.JoinerMayAct(a, b));
    }

    [Fact]
    public void AnUnclaimedSeatIsPlayedAtTheHost()
    {
        var seats = new SeatTable();
        seats.Claim("a");
        Assert.True(seats.HostMayAct(2));
        Assert.False(seats.JoinerMayAct(2, 1));
    }

    [Fact]
    public void ADisconnectedSeatIsKeptAndPlayedAtTheHostMeanwhile()
    {
        var seats = new SeatTable();
        var a = seats.Claim("a");
        seats.Disconnect(a);

        Assert.True(seats.HostMayAct(a));
        Assert.False(seats.JoinerMayAct(a, a));

        // A newcomer does not take it while a free seat exists…
        Assert.Equal(2, seats.Claim("b"));

        // …and the original joiner gets it back.
        Assert.Equal(a, seats.Claim("a"));
        Assert.False(seats.HostMayAct(a));
    }

    [Fact]
    public void ARestartedJoinerTakesOverAnAbandonedSeatWhenThereIsNoOther()
    {
        var seats = new SeatTable();
        seats.Claim("a");
        seats.Claim("b");
        seats.Claim("c");
        seats.Disconnect(2);

        Assert.Equal(2, seats.Claim("new-token"));
    }

    [Fact]
    public void TheSameTokenCannotBeSeatedTwiceAtOnce()
    {
        var seats = new SeatTable();
        seats.Claim("a");
        Assert.Equal(-1, seats.Claim("a"));
    }
}

public class WireTests
{
    [Fact]
    public void AFrameRoundTripsWithValueEquality()
    {
        var frame = new FrameMsg
        {
            Seq = 7,
            Screen = 4,
            Acting = 1,
            CanGoals = true,
            Sprites = [new SpriteDto(10, 20, ArtRef.Cel(250, 8, 2), "Bank", true)],
            Texts = [new TextDto("Week # 3", 5, 6, 8, "#000000", false, null, -1, 0, null)],
            Lines = [new LineDto("Cook", 1, 2, 0, 6, 100, 7, null, true, false, 3, "$4 Hr.")],
            Balloon = [new PartDto(1, 2, 3, 4, "#FFF8E898", null), new PartDto(1, 2, 12, 12, null, ArtRef.Text(1, "Hi", 0xFF000000, null, null))],
            BalloonButtons = [new RectDto(5, 5, 20, 10, "Yes")],
        };

        var line = System.Text.Encoding.UTF8.GetString(Wire.Encode(new Envelope { Frame = frame }));
        Assert.EndsWith("\n", line);
        Assert.DoesNotContain("\n", line.TrimEnd('\n'));

        var back = Wire.Decode(line.TrimEnd('\n'))!.Frame!;
        Assert.Equal(7, back.Seq);
        Assert.Equal(1, back.Acting);
        Assert.True(back.CanGoals);
        Assert.Equal(frame.Sprites, back.Sprites);
        Assert.Equal(frame.Texts, back.Texts);
        Assert.Equal(frame.Lines, back.Lines);
        Assert.Equal(frame.Balloon, back.Balloon);
        Assert.Equal(frame.BalloonButtons, back.BalloonButtons);
    }

    /// <summary>
    /// The -1 "transparent" background must survive: it is not the default, so it is written,
    /// and a 0 that was omitted must come back as 0 rather than as something else.
    /// </summary>
    [Fact]
    public void NonDefaultAndDefaultValuesBothSurviveTheShortForm()
    {
        var t = new TextDto("x", 0, 0, 8, "#000000", false, 10, -1, 0, null);
        var line = System.Text.Encoding.UTF8.GetString(Wire.Encode(new Envelope { Frame = new FrameMsg { Texts = [t] } }));
        Assert.Equal(t, Wire.Decode(line)!.Frame!.Texts[0]);
    }

    [Fact]
    public void GarbageDecodesToNullRatherThanThrowing()
    {
        Assert.Null(Wire.Decode("{not json"));
    }

    [Fact]
    public void IdentityIgnoresTheIndexSoARebuiltScreenStillMatches()
    {
        var a = new SpriteDto(10, 20, ArtRef.Cel(250, 8, 2), "Bank", true);
        var b = a with { };
        Assert.Equal(Identity.Of(a), Identity.Of(b));
        Assert.NotEqual(Identity.Of(a), Identity.Of(a with { X = 11 }));
    }
}

/// <summary>What a player types into the join box, or passes to <c>--join</c>.</summary>
public class AddressTests
{
    [Theory]
    [InlineData("100.64.1.2", "100.64.1.2", ProtocolInfo.DefaultPort)]
    [InlineData(" 100.64.1.2 ", "100.64.1.2", ProtocolInfo.DefaultPort)]
    [InlineData("pc.tail1234.ts.net:7200", "pc.tail1234.ts.net", 7200)]
    [InlineData("192.168.1.5:7117", "192.168.1.5", 7117)]
    [InlineData("fd7a:115c:a1e0::1", "fd7a:115c:a1e0::1", ProtocolInfo.DefaultPort)]
    [InlineData("[fd7a:115c:a1e0::1]", "fd7a:115c:a1e0::1", ProtocolInfo.DefaultPort)]
    [InlineData("[fd7a:115c:a1e0::1]:9000", "fd7a:115c:a1e0::1", 9000)]
    public void GoodAddressesSplit(string typed, string host, int port)
    {
        Assert.True(Address.TrySplit(typed, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("host:")]
    [InlineData("host:abc")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData("two words")]
    public void BadAddressesAreRefused(string? typed)
    {
        Assert.False(Address.TrySplit(typed, out _, out _));
    }

    [Fact]
    public void FormatRoundTripsThroughTrySplit()
    {
        foreach (var (host, port) in new[] { ("10.0.0.1", ProtocolInfo.DefaultPort), ("10.0.0.1", 8000), ("::1", 8000) })
        {
            Assert.True(Address.TrySplit(Address.Format(host, port), out var h, out var p));
            Assert.Equal((host, port), (h, p));
        }

        Assert.Equal("10.0.0.1", Address.Format("10.0.0.1", ProtocolInfo.DefaultPort));
    }
}

/// <summary>A real host and joiner over loopback.</summary>
public class LoopbackTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static HostServer NewHost()
    {
        var host = new HostServer(0, IPAddress.Loopback);
        host.Start();
        return host;
    }

    [Fact]
    public void AJoinerIsSeatedReceivesFramesAndSendsInput()
    {
        using var host = NewHost();

        var joined = new TaskCompletionSource<int>();
        var input = new TaskCompletionSource<(int, InputMsg)>();
        host.Joined += s => joined.TrySetResult(s);
        host.Input += (s, m) => input.TrySetResult((s, m));

        using var client = new JoinClient("127.0.0.1", host.Port);
        var welcomed = new TaskCompletionSource<int>();
        var frame = new TaskCompletionSource<FrameMsg>();
        var audio = new TaskCompletionSource<AudioMsg>();
        client.Welcomed += s => welcomed.TrySetResult(s);
        client.Frame += f => frame.TrySetResult(f);
        client.Audio += a => audio.TrySetResult(a);
        client.Start();

        Assert.True(welcomed.Task.Wait(Wait));
        Assert.Equal(1, welcomed.Task.Result);
        Assert.True(joined.Task.Wait(Wait));
        Assert.Equal(1, joined.Task.Result);

        host.BroadcastFrame(Wire.Encode(new Envelope { Frame = new FrameMsg { Seq = 42 } }));
        Assert.True(frame.Task.Wait(Wait));
        Assert.Equal(42, frame.Task.Result.Seq);

        host.Broadcast(new Envelope { Audio = new AudioMsg { Op = AudioOp.Music, Id = 5, Loop = true } });
        Assert.True(audio.Task.Wait(Wait));
        Assert.Equal(5, audio.Task.Result.Id);

        client.Send(new InputMsg { Kind = InputKind.Hotspot, Index = 3, Id = "x" });
        Assert.True(input.Task.Wait(Wait));
        Assert.Equal(1, input.Task.Result.Item1);
        Assert.Equal(InputKind.Hotspot, input.Task.Result.Item2.Kind);
        Assert.Equal(3, input.Task.Result.Item2.Index);

        Assert.True(host.Seats.IsRemote(1));
    }

    [Fact]
    public void TwoJoinersGetSeatsOneAndTwo()
    {
        using var host = NewHost();

        using var a = new JoinClient("127.0.0.1", host.Port);
        using var b = new JoinClient("127.0.0.1", host.Port);
        var seatA = new TaskCompletionSource<int>();
        var seatB = new TaskCompletionSource<int>();
        a.Welcomed += s => seatA.TrySetResult(s);
        b.Welcomed += s => seatB.TrySetResult(s);

        a.Start();
        Assert.True(seatA.Task.Wait(Wait));
        b.Start();
        Assert.True(seatB.Task.Wait(Wait));

        Assert.Equal(1, seatA.Task.Result);
        Assert.Equal(2, seatB.Task.Result);
    }

    [Fact]
    public void AJoinerWhoLeavesFreesTheSeatForTheHostToPlay()
    {
        using var host = NewHost();
        var left = new TaskCompletionSource<int>();
        host.Left += s => left.TrySetResult(s);

        var client = new JoinClient("127.0.0.1", host.Port);
        var welcomed = new TaskCompletionSource<int>();
        client.Welcomed += s => welcomed.TrySetResult(s);
        client.Start();
        Assert.True(welcomed.Task.Wait(Wait));

        client.Dispose();
        Assert.True(left.Task.Wait(Wait));
        Assert.True(host.Seats.HostMayAct(1));
    }

    [Fact]
    public void AMismatchedProtocolIsRefused()
    {
        using var host = NewHost();

        using var tcp = new System.Net.Sockets.TcpClient("127.0.0.1", host.Port);
        using var link = new NetLink(tcp);
        var answer = new TaskCompletionSource<WelcomeMsg>();
        link.Received += (_, e) => { if (e.Welcome is { } w) answer.TrySetResult(w); };
        link.Start();
        link.Send(new Envelope { Hello = new HelloMsg { Protocol = ProtocolInfo.Version + 1, Token = "t" } });

        Assert.True(answer.Task.Wait(Wait));
        Assert.Equal(-1, answer.Task.Result.Seat);
        Assert.False(host.Seats.IsRemote(1));
    }

    [Fact]
    public void InputBeforeTheHelloIsIgnored()
    {
        using var host = NewHost();
        var got = false;
        host.Input += (_, _) => got = true;

        using var tcp = new System.Net.Sockets.TcpClient("127.0.0.1", host.Port);
        using var link = new NetLink(tcp);
        link.Start();
        link.Send(new Envelope { Input = new InputMsg { Kind = InputKind.EndTurn } });

        Thread.Sleep(300);
        Assert.False(got);
    }
}
