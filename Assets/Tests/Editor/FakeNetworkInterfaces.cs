using System.Collections.Generic;
using System.Net;

/// <summary>
/// <see cref="INetworkInterfaceSource"/> listing the interfaces tests add.
/// </summary>
public sealed class FakeNetworkInterfaces : INetworkInterfaceSource
{
    private readonly List<NetworkInterfaceInfo> interfaces = new();

    /// <summary>
    /// Adds an interface named <paramref name="name"/> holding <paramref name="address"/>, up unless
    /// <paramref name="down"/>.
    /// </summary>
    public void Add(string name, bool hasGateway, string address, bool down = false, bool loopback = false, string description = "")
    {
        interfaces.Add(new NetworkInterfaceInfo(name, description, !down, loopback, hasGateway, new[] { IPAddress.Parse(address) }));
    }

    /// <summary>
    /// Forgets every interface.
    /// </summary>
    public void Clear()
    {
        interfaces.Clear();
    }

    /// <summary>
    /// Fills <paramref name="into"/> with the interfaces added.
    /// </summary>
    public void Read(List<NetworkInterfaceInfo> into)
    {
        into.Clear();
        into.AddRange(interfaces);
    }
}
