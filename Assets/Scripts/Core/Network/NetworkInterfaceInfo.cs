using System.Collections.Generic;
using System.Net;

/// <summary>
/// What <see cref="HostAddresses"/> reads of one of this machine's network interfaces: its name and description, whether
/// it is up, a loopback or has a default gateway, and its IPv4 addresses.
/// </summary>
public readonly struct NetworkInterfaceInfo
{
    /// <summary>
    /// Describes an interface named <paramref name="name"/>.
    /// </summary>
    public NetworkInterfaceInfo(string name, string description, bool isUp, bool isLoopback, bool hasGateway, IReadOnlyList<IPAddress> addresses)
    {
        Name = name ?? string.Empty;
        Description = description ?? string.Empty;
        IsUp = isUp;
        IsLoopback = isLoopback;
        HasGateway = hasGateway;
        Addresses = addresses ?? new IPAddress[0];
    }

    public string Name { get; }
    public string Description { get; }
    public bool IsUp { get; }
    public bool IsLoopback { get; }
    public bool HasGateway { get; }
    public IReadOnlyList<IPAddress> Addresses { get; }
}
