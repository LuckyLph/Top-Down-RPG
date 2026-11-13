/// <summary>
/// An address friends can join this machine's game at, and the name of the network it is on ("home network",
/// "Tailscale").
/// </summary>
public readonly struct HostAddress
{
    /// <summary>
    /// The address <paramref name="address"/> on the network named <paramref name="network"/>.
    /// </summary>
    public HostAddress(string address, string network)
    {
        Address = address;
        Network = network;
    }

    public string Address { get; }
    public string Network { get; }
}
