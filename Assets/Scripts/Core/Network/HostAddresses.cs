using System;
using System.Collections.Generic;
using System.Net;

/// <summary>
/// The addresses friends join this machine's game at (Docs/UiRevamp.md, Core: HostAddresses), read from its network
/// interfaces when asked, and the port its sessions listen on. Each address is named by its network:
/// <list type="bullet">
/// <item>"Tailscale" for an address in Tailscale's range (100.64.0.0/10) or on an interface Tailscale names;</item>
/// <item>"ZeroTier" for one on an interface ZeroTier names;</item>
/// <item>"home network" for a private address (10/8, 172.16/12, 192.168/16) on an interface with a default
/// gateway;</item>
/// <item>"internet" for any other address on an interface with a default gateway.</item>
/// </list>
/// Interfaces that are down and loopbacks give none, nor do link-local addresses or a private address without a gateway
/// (the virtual switches of Hyper-V, WSL or VirtualBox, which friends cannot reach). The addresses come home network
/// first, then the virtual LANs, then the internet, at most <see cref="MaxShown"/>. Nothing is ever sent anywhere.
/// </summary>
public sealed class HostAddresses
{
    public const string HomeNetwork = "home network";
    public const string Tailscale = "Tailscale";
    public const string ZeroTier = "ZeroTier";
    public const string Internet = "internet";
    public const int MaxShown = 2;

    private static readonly string[] Ranked = { HomeNetwork, Tailscale, ZeroTier, Internet };

    private readonly INetworkInterfaceSource source;
    private readonly List<NetworkInterfaceInfo> interfaces = new();

    /// <summary>
    /// Reads the interfaces <paramref name="source"/> lists, for sessions listening on <paramref name="port"/>.
    /// </summary>
    public HostAddresses(INetworkInterfaceSource source, ushort port)
    {
        this.source = source;
        Port = port;
    }

    public ushort Port { get; }

    /// <summary>
    /// Clears <paramref name="addresses"/> and fills it with the addresses friends can join at now, in order.
    /// </summary>
    public void Read(List<HostAddress> addresses)
    {
        addresses.Clear();
        source.Read(interfaces);
        for (int rank = 0; rank < Ranked.Length; rank++)
        {
            for (int i = 0; i < interfaces.Count; i++)
            {
                NetworkInterfaceInfo item = interfaces[i];
                for (int a = 0; a < item.Addresses.Count; a++)
                {
                    if (addresses.Count >= MaxShown)
                    {
                        return;
                    }

                    if (TryName(item, item.Addresses[a], out string network) && network == Ranked[rank] && !Contains(addresses, item.Addresses[a].ToString()))
                    {
                        addresses.Add(new HostAddress(item.Addresses[a].ToString(), network));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The name of the network <paramref name="address"/> on <paramref name="item"/> is on; false for an address friends
    /// cannot join at.
    /// </summary>
    internal static bool TryName(NetworkInterfaceInfo item, IPAddress address, out string network)
    {
        network = null;
        if (!item.IsUp || item.IsLoopback || address == null || IPAddress.IsLoopback(address))
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4 || (bytes[0] == 169 && bytes[1] == 254) || bytes[0] == 0)
        {
            return false;
        }

        if ((bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) || Names(item, "Tailscale"))
        {
            network = Tailscale;
        }
        else if (Names(item, "ZeroTier"))
        {
            network = ZeroTier;
        }
        else if (item.HasGateway)
        {
            network = IsPrivate(bytes) ? HomeNetwork : Internet;
        }

        return network != null;
    }

    /// <summary>
    /// Whether an IPv4 address's <paramref name="bytes"/> lie in a private range: 10/8, 172.16/12 or 192.168/16.
    /// </summary>
    private static bool IsPrivate(byte[] bytes)
    {
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }

    /// <summary>
    /// Whether <paramref name="item"/>'s name or description holds <paramref name="word"/>, ignoring case.
    /// </summary>
    private static bool Names(NetworkInterfaceInfo item, string word)
    {
        return item.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
            || item.Description.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Whether <paramref name="addresses"/> already holds <paramref name="address"/>.
    /// </summary>
    private static bool Contains(List<HostAddress> addresses, string address)
    {
        for (int i = 0; i < addresses.Count; i++)
        {
            if (addresses[i].Address == address)
            {
                return true;
            }
        }

        return false;
    }
}
