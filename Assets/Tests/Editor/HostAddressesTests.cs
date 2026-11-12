using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="HostAddresses"/> (Docs/UiRevamp.md, Phase 6) over interfaces of its own: each address is named
/// by its network, the ones friends cannot reach are left out, the home network comes first and at most two show.
/// </summary>
public class HostAddressesTests
{
    private FakeNetworkInterfaces interfaces;
    private HostAddresses hostAddresses;
    private List<HostAddress> addresses;

    /// <summary>
    /// Reads no interfaces yet, for port 7777.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        interfaces = new FakeNetworkInterfaces();
        hostAddresses = new HostAddresses(interfaces, 7777);
        addresses = new List<HostAddress>();
    }

    /// <summary>
    /// A private address with a gateway is the home network; Tailscale's range, or an interface Tailscale names, is
    /// Tailscale; one ZeroTier names is ZeroTier; a public address with a gateway is the internet.
    /// </summary>
    [Test]
    public void EachAddress_IsNamedByItsNetwork()
    {
        Assert.That(Name("Wi-Fi", true, "192.168.1.20"), Is.EqualTo(HostAddresses.HomeNetwork));
        Assert.That(Name("Ethernet", true, "10.0.0.5"), Is.EqualTo(HostAddresses.HomeNetwork));
        Assert.That(Name("Ethernet", true, "172.20.1.2"), Is.EqualTo(HostAddresses.HomeNetwork));
        Assert.That(Name("Tailscale", false, "100.101.102.103"), Is.EqualTo(HostAddresses.Tailscale));
        Assert.That(Name("Wintun", false, "100.64.0.1"), Is.EqualTo(HostAddresses.Tailscale), "Tailscale's range, whatever the interface's name.");
        Assert.That(Name("ZeroTier One [abc]", false, "10.147.20.5"), Is.EqualTo(HostAddresses.ZeroTier));
        Assert.That(Name("Ethernet", true, "203.0.113.7"), Is.EqualTo(HostAddresses.Internet));
        Assert.That(hostAddresses.Port, Is.EqualTo((ushort)7777));
    }

    /// <summary>
    /// Interfaces that are down and loopbacks give none, nor do link-local addresses or a private address without a
    /// gateway (a virtual switch).
    /// </summary>
    [Test]
    public void AddressesFriendsCannotReach_AreLeftOut()
    {
        interfaces.Add("Wi-Fi", true, "192.168.1.20", down: true);
        interfaces.Add("Loopback", false, "127.0.0.1", loopback: true);
        interfaces.Add("Ethernet", true, "169.254.10.4");
        interfaces.Add("vEthernet (WSL)", false, "172.29.48.1");
        interfaces.Add("Ethernet", false, "203.0.113.7");

        hostAddresses.Read(addresses);

        Assert.That(addresses, Is.Empty);
    }

    /// <summary>
    /// The home network comes first, then the virtual LANs, then the internet; an address listed twice shows once; at
    /// most two show.
    /// </summary>
    [Test]
    public void TheHomeNetwork_ComesFirst_AndAtMostTwoShow()
    {
        interfaces.Add("Tailscale", false, "100.101.102.103");
        interfaces.Add("Ethernet", true, "203.0.113.7");
        interfaces.Add("Wi-Fi", true, "192.168.1.20");
        interfaces.Add("Wi-Fi 2", true, "192.168.1.20");

        hostAddresses.Read(addresses);

        Assert.That(addresses.Count, Is.EqualTo(2));
        Assert.That(addresses[0].Address, Is.EqualTo("192.168.1.20"));
        Assert.That(addresses[0].Network, Is.EqualTo(HostAddresses.HomeNetwork));
        Assert.That(addresses[1].Address, Is.EqualTo("100.101.102.103"));
        Assert.That(addresses[1].Network, Is.EqualTo(HostAddresses.Tailscale));

        interfaces.Clear();
        interfaces.Add("Ethernet", true, "203.0.113.7");
        hostAddresses.Read(addresses);
        Assert.That(addresses.Count, Is.EqualTo(1), "Read again from what the interfaces say now.");
        Assert.That(addresses[0].Network, Is.EqualTo(HostAddresses.Internet));
    }

    /// <summary>
    /// The network name of <paramref name="address"/> alone on an interface named <paramref name="name"/>, or null.
    /// </summary>
    private string Name(string name, bool hasGateway, string address)
    {
        interfaces.Clear();
        interfaces.Add(name, hasGateway, address);
        hostAddresses.Read(addresses);
        return addresses.Count == 1 ? addresses[0].Network : null;
    }
}
