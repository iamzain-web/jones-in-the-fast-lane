namespace Jones.Net;

/// <summary>Parsing what a player types (or passes to <c>--join</c>) as the host to join.</summary>
public static class Address
{
    /// <summary>
    /// "host", "host:port", "[v6]", "[v6]:port" or a bare IPv6 address, into its parts. False
    /// for an empty or unparseable entry. The port defaults to
    /// <see cref="ProtocolInfo.DefaultPort"/>.
    /// </summary>
    public static bool TrySplit(string? address, out string host, out int port)
    {
        host = "";
        port = ProtocolInfo.DefaultPort;

        var text = address?.Trim() ?? "";
        if (text.Length == 0) return false;

        string? portText = null;

        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0) return false;
            var rest = text[(close + 1)..];
            if (rest.Length > 0)
            {
                if (!rest.StartsWith(':')) return false;
                portText = rest[1..];
            }
            text = text[1..close];
        }
        else if (text.Count(c => c == ':') == 1)
        {
            // host:port. Two or more colons with no brackets is a bare IPv6 address.
            var colon = text.IndexOf(':');
            portText = text[(colon + 1)..];
            text = text[..colon];
        }

        if (portText is not null && (!int.TryParse(portText, out port) || port is <= 0 or > 65535))
        {
            port = ProtocolInfo.DefaultPort;
            return false;
        }

        if (text.Any(char.IsWhiteSpace)) return false;

        host = text;
        return host.Length > 0;
    }

    /// <summary>The short form to remember and offer back: the port only when it is not the default.</summary>
    public static string Format(string host, int port)
    {
        var h = host.Contains(':') ? $"[{host}]" : host;
        return port == ProtocolInfo.DefaultPort ? h : $"{h}:{port}";
    }
}
