namespace Jones.Net;

/// <summary>
/// Who owns each of the four player seats.
///
/// <para>
/// A SEAT IS A PLAYER NUMBER, 0-based: seat 1 is the game's player 2. Seat 0 always belongs
/// to the host. Joiners take seats 1, 2, 3 in the order they arrive. Any seat nobody has
/// joined into is played at the host's machine, which is ordinary hot-seat play, so a
/// three-player game can be two people on the host and one remote.
/// </para>
///
/// <para>
/// A DISCONNECTED JOINER KEEPS THEIR SEAT, identified by the token they sent in their hello,
/// and gets it back by reconnecting. While they are away the host may act for that seat, so
/// the game is never stuck waiting for someone who has gone.
/// </para>
///
/// Thread-safe: the host's accept and read threads and the UI thread all consult it.
/// </summary>
public sealed class SeatTable
{
    public const int Count = 4;

    private readonly object _gate = new();
    private readonly string?[] _token = new string?[Count];
    private readonly bool[] _connected = new bool[Count];

    /// <summary>
    /// Seats a joiner. Their own seat if the token is known; otherwise the lowest seat nobody
    /// has claimed; otherwise the lowest seat whose owner is disconnected (a joiner whose app
    /// was restarted has a new token and would otherwise be locked out). -1 if every remote
    /// seat is taken by someone who is still here.
    /// </summary>
    public int Claim(string token)
    {
        if (string.IsNullOrEmpty(token)) return -1;

        lock (_gate)
        {
            for (var s = 1; s < Count; s++)
            {
                if (_token[s] != token) continue;
                if (_connected[s]) return -1;       // the same joiner twice at once
                _connected[s] = true;
                return s;
            }

            for (var s = 1; s < Count; s++)
            {
                if (_token[s] is not null) continue;
                _token[s] = token;
                _connected[s] = true;
                return s;
            }

            for (var s = 1; s < Count; s++)
            {
                if (_connected[s]) continue;
                _token[s] = token;
                _connected[s] = true;
                return s;
            }

            return -1;
        }
    }

    public void Disconnect(int seat)
    {
        if (seat <= 0 || seat >= Count) return;
        lock (_gate) _connected[seat] = false;
    }

    /// <summary>True when a joiner who is connected right now owns the seat.</summary>
    public bool IsRemote(int seat)
    {
        if (seat <= 0 || seat >= Count) return false;
        lock (_gate) return _connected[seat];
    }

    /// <summary>The host's own mouse and keyboard may act for this seat.</summary>
    public bool HostMayAct(int seat) => !IsRemote(seat);

    /// <summary>The joiner in <paramref name="joinerSeat"/> may act for <paramref name="seat"/>.</summary>
    public bool JoinerMayAct(int seat, int joinerSeat) => seat == joinerSeat && IsRemote(seat);
}
