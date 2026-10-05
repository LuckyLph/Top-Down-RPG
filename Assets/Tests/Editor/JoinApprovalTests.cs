using NUnit.Framework;

/// <summary>
/// Tests for <see cref="JoinApproval"/>: the host's version and party size check on joining clients, and encoding
/// its refusal into a disconnect reason.
/// </summary>
public class JoinApprovalTests
{
    /// <summary>
    /// A client on the host's version joining a party with room is approved (no refusal).
    /// </summary>
    [Test]
    public void SameVersionWithRoom_IsApproved()
    {
        Assert.That(JoinApproval.Evaluate(JoinApproval.CreatePayload("1.2"), "1.2", 3, 4), Is.Null);
    }

    /// <summary>
    /// Joining a full party is refused with a reason that says it is full and names the player limit.
    /// </summary>
    [Test]
    public void FullParty_IsRefused()
    {
        string refusal = JoinApproval.Evaluate(JoinApproval.CreatePayload("1.2"), "1.2", 4, 4);

        Assert.That(refusal, Does.Contain("full"));
        Assert.That(refusal, Does.Contain("4 players"));
    }

    /// <summary>
    /// A client on another version is refused with a reason naming both the client's and the host's version.
    /// </summary>
    [Test]
    public void OtherVersion_IsRefusedNamingBothVersions()
    {
        string refusal = JoinApproval.Evaluate(JoinApproval.CreatePayload("1.1"), "1.2", 1, 4);

        Assert.That(refusal, Does.Contain("1.1"));
        Assert.That(refusal, Does.Contain("1.2"));
    }

    /// <summary>
    /// A null or empty connection payload is refused as an unknown version.
    /// </summary>
    [TestCase(null)]
    [TestCase(new byte[0])]
    public void MissingPayload_IsRefusedAsAnUnknownVersion(byte[] payload)
    {
        Assert.That(JoinApproval.Evaluate(payload, "1.2", 1, 4), Does.Contain("unknown version"));
    }

    /// <summary>
    /// A client on another version joining a full party is refused for its version, not for the party size.
    /// </summary>
    [Test]
    public void VersionIsCheckedBeforeRoom()
    {
        Assert.That(JoinApproval.Evaluate(JoinApproval.CreatePayload("1.1"), "1.2", 4, 4), Does.Contain("1.1"));
    }

    /// <summary>
    /// An encoded refusal decodes back to the same reason text.
    /// </summary>
    [Test]
    public void Refusal_RoundTripsThroughADisconnectReason()
    {
        string encoded = JoinApproval.EncodeRefusal("the game is full (4 players).");

        Assert.That(JoinApproval.TryDecodeRefusal(encoded, out string reason), Is.True);
        Assert.That(reason, Is.EqualTo("the game is full (4 players)."));
    }

    /// <summary>
    /// Null, empty and Netcode's own disconnect reasons are not decoded as refusals and yield a null reason.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("[Disconnect Event][Client-1][TransportClientId-1][Disconnected] Gracefully disconnected.")]
    public void OtherDisconnectReasons_AreNotRefusals(string disconnectReason)
    {
        Assert.That(JoinApproval.TryDecodeRefusal(disconnectReason, out string reason), Is.False);
        Assert.That(reason, Is.Null);
    }
}
