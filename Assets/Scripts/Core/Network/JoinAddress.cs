using System;
using System.Globalization;

/// <summary>
/// A host to join (Docs/UiRevamp.md, Core: JoinAddress): an IPv4 address or a host name, and a port. Parsed from what a
/// player types, <c>host</c> or <c>host:port</c>, checked before anything connects: an IPv4 address has four numbers
/// from 0 to 255, a host name is one the transport can resolve, and a port runs from 1 to 65535. IPv6 is not taken.
/// </summary>
public readonly struct JoinAddress : IEquatable<JoinAddress>
{
    public const int MaxHostLength = 253;

    /// <summary>
    /// Creates the address of <paramref name="host"/> on <paramref name="port"/>, unchecked.
    /// </summary>
    public JoinAddress(string host, ushort port)
    {
        Host = host ?? string.Empty;
        Port = port;
    }

    public string Host { get; }
    public ushort Port { get; }

    /// <summary>
    /// Reads <paramref name="text"/>, <c>host</c> or <c>host:port</c> with spaces around it ignored, the port
    /// <paramref name="defaultPort"/> when none is given. False when it is empty or malformed.
    /// </summary>
    public static bool TryParse(string text, ushort defaultPort, out JoinAddress address)
    {
        address = default;
        string trimmed = text?.Trim() ?? string.Empty;
        int colon = trimmed.IndexOf(':');
        if (trimmed.Length == 0 || colon != trimmed.LastIndexOf(':'))
        {
            return false;
        }

        string host = colon < 0 ? trimmed : trimmed.Substring(0, colon);
        ushort port = defaultPort;
        if (colon >= 0 && !TryParsePort(trimmed.Substring(colon + 1), out port))
        {
            return false;
        }

        if (!IsHost(host))
        {
            return false;
        }

        address = new JoinAddress(host, port);
        return true;
    }

    /// <summary>
    /// The host alone when the port is <paramref name="defaultPort"/>, else <c>host:port</c>: how a recent host is kept
    /// and shown.
    /// </summary>
    public string ToShortString(ushort defaultPort)
    {
        return Port == defaultPort ? Host : ToString();
    }

    /// <summary>
    /// <c>host:port</c>.
    /// </summary>
    public override string ToString()
    {
        return Host + ":" + Port.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether <paramref name="other"/> names the same host, whatever its case, on the same port.
    /// </summary>
    public bool Equals(JoinAddress other)
    {
        return Port == other.Port && string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether <paramref name="obj"/> is a <see cref="JoinAddress"/> equal to this one.
    /// </summary>
    public override bool Equals(object obj)
    {
        return obj is JoinAddress other && Equals(other);
    }

    /// <summary>
    /// A hash of the host, whatever its case, and the port.
    /// </summary>
    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Host) * 397 ^ Port;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a port: digits alone, from 1 to 65535.
    /// </summary>
    private static bool TryParsePort(string text, out ushort port)
    {
        port = 0;
        if (text.Length == 0 || text.Length > 5)
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9')
            {
                return false;
            }
        }

        int value = int.Parse(text, CultureInfo.InvariantCulture);
        if (value < 1 || value > ushort.MaxValue)
        {
            return false;
        }

        port = (ushort)value;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="host"/> is an IPv4 address (when it holds only digits and dots) or a host name.
    /// </summary>
    private static bool IsHost(string host)
    {
        if (host.Length == 0 || host.Length > MaxHostLength)
        {
            return false;
        }

        for (int i = 0; i < host.Length; i++)
        {
            if (host[i] != '.' && (host[i] < '0' || host[i] > '9'))
            {
                return Uri.CheckHostName(host) == UriHostNameType.Dns;
            }
        }

        return IsIpv4(host);
    }

    /// <summary>
    /// Whether <paramref name="host"/>, digits and dots alone, is four numbers from 0 to 255 of up to three digits.
    /// </summary>
    private static bool IsIpv4(string host)
    {
        string[] parts = host.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (part.Length == 0 || part.Length > 3 || int.Parse(part, CultureInfo.InvariantCulture) > 255)
            {
                return false;
            }
        }

        return true;
    }
}
