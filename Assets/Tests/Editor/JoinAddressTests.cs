using NUnit.Framework;

/// <summary>
/// Tests for <see cref="JoinAddress"/> (Docs/UiRevamp.md, Phase 9): a host alone on the default port, a host with its
/// port, host names, and empty or malformed text refused before anything connects.
/// </summary>
public class JoinAddressTests
{
    private const ushort DefaultPort = 7777;

    /// <summary>
    /// An IPv4 address or a host name alone joins on the default port, spaces around it ignored.
    /// </summary>
    [TestCase("192.168.1.20", "192.168.1.20")]
    [TestCase("  100.101.102.103 ", "100.101.102.103")]
    [TestCase("localhost", "localhost")]
    [TestCase("ana-desktop.tail1a2b3.ts.net", "ana-desktop.tail1a2b3.ts.net")]
    [TestCase("0.0.0.0", "0.0.0.0")]
    public void AHostAlone_JoinsOnTheDefaultPort(string text, string host)
    {
        Assert.That(JoinAddress.TryParse(text, DefaultPort, out JoinAddress address), Is.True);
        Assert.That(address.Host, Is.EqualTo(host));
        Assert.That(address.Port, Is.EqualTo(DefaultPort));
        Assert.That(address.ToString(), Is.EqualTo(host + ":7777"));
        Assert.That(address.ToShortString(DefaultPort), Is.EqualTo(host), "Kept as the host alone.");
    }

    /// <summary>
    /// A host with a port joins on that port, and keeps it when shown or kept.
    /// </summary>
    [TestCase("192.168.1.20:7778", "192.168.1.20", 7778)]
    [TestCase("100.101.102.103:1", "100.101.102.103", 1)]
    [TestCase("host.example:65535", "host.example", 65535)]
    [TestCase("192.168.1.20:7777", "192.168.1.20", 7777)]
    public void AHostWithAPort_JoinsOnThatPort(string text, string host, int port)
    {
        Assert.That(JoinAddress.TryParse(text, DefaultPort, out JoinAddress address), Is.True);
        Assert.That(address.Host, Is.EqualTo(host));
        Assert.That(address.Port, Is.EqualTo(port));
        Assert.That(address.ToShortString(DefaultPort), Is.EqualTo(port == DefaultPort ? host : $"{host}:{port}"));
    }

    /// <summary>
    /// Nothing, or only spaces, is no address.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void AnEmptyAddress_IsRefused(string text)
    {
        Assert.That(JoinAddress.TryParse(text, DefaultPort, out _), Is.False);
    }

    /// <summary>
    /// A malformed address is refused: an IPv4 address with a number past 255 or the wrong count of numbers, a port
    /// outside 1 to 65535 or not a number, an empty host or port, a space inside, characters a host name cannot hold,
    /// and IPv6. A host name is what the transport resolves, so an underscore, which resolvers take, is not refused.
    /// </summary>
    [TestCase("192.168.1.300")]
    [TestCase("192.168.1")]
    [TestCase("192.168.1.20.5")]
    [TestCase("192.168..20")]
    [TestCase("192.168.1.20:0")]
    [TestCase("192.168.1.20:65536")]
    [TestCase("192.168.1.20:77777")]
    [TestCase("192.168.1.20:port")]
    [TestCase("192.168.1.20:")]
    [TestCase(":7777")]
    [TestCase("192.168.1 .20")]
    [TestCase("host!")]
    [TestCase("host name")]
    [TestCase("::1")]
    [TestCase("[::1]:7777")]
    public void AMalformedAddress_IsRefused(string text)
    {
        Assert.That(JoinAddress.TryParse(text, DefaultPort, out _), Is.False, text);
    }

    /// <summary>
    /// Two addresses naming the same host in another case on the same port are equal; another port is not.
    /// </summary>
    [Test]
    public void Equality_IgnoresTheHostsCase_ButNotThePort()
    {
        Assert.That(new JoinAddress("Host.Example", 7777), Is.EqualTo(new JoinAddress("host.example", 7777)));
        Assert.That(new JoinAddress("host.example", 7777), Is.Not.EqualTo(new JoinAddress("host.example", 7778)));
    }
}
