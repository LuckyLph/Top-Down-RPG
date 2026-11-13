using NUnit.Framework;

/// <summary>
/// Tests for <see cref="JoinApproval"/>: the join payload carrying the version and the player id, the host's version
/// and party size check on joining clients, its refusals' kinds (another build with the host's version, a full party
/// with its size), and encoding a refusal into a disconnect reason.
/// </summary>
public class JoinApprovalTests
{
    /// <summary>
    /// The payload carries the version, then the player id, and reads back as both.
    /// </summary>
    [Test]
    public void Payload_CarriesTheVersionThenThePlayerId()
    {
        JoinApproval.ReadPayload(JoinApproval.CreatePayload("1.3", "0f8e2c7a9b"), out string version, out string playerId);

        Assert.That(version, Is.EqualTo("1.3"));
        Assert.That(playerId, Is.EqualTo("0f8e2c7a9b"));
    }

    /// <summary>
    /// A payload with only a version, as a build before player ids sent, reads with an empty player id, and a missing
    /// player id stays empty; line breaks never leak from one part into the other.
    /// </summary>
    [Test]
    public void Payload_WithoutAPlayerId_ReadsAnEmptyOne()
    {
        JoinApproval.ReadPayload(System.Text.Encoding.UTF8.GetBytes("1.2"), out string oldVersion, out string oldId);
        JoinApproval.ReadPayload(JoinApproval.CreatePayload("1.3", null), out string version, out string playerId);
        JoinApproval.ReadPayload(JoinApproval.CreatePayload("1.3\nx", "a\r\nb"), out string cleanVersion, out string cleanId);
        JoinApproval.ReadPayload(null, out string noVersion, out string noId);

        Assert.That(oldVersion, Is.EqualTo("1.2"));
        Assert.That(oldId, Is.Empty);
        Assert.That(version, Is.EqualTo("1.3"));
        Assert.That(playerId, Is.Empty);
        Assert.That(cleanVersion, Is.EqualTo("1.3x"));
        Assert.That(cleanId, Is.EqualTo("ab"));
        Assert.That(noVersion, Is.Empty);
        Assert.That(noId, Is.Empty);
    }

    /// <summary>
    /// A client on the host's version joining a party with room is approved (no refusal), whatever its player id.
    /// </summary>
    [Test]
    public void TheVersionIsReadFromAPayloadWithAPlayerId()
    {
        Assert.That(JoinApproval.Evaluate(JoinApproval.CreatePayload("1.2", string.Empty), "1.2", 1, 4), Is.Null);
        Assert.That(JoinApproval.Evaluate(JoinApproval.CreatePayload("1.2", "1.2"), "1.3", 1, 4), Is.EqualTo(JoinApproval.OtherBuild("1.3")));
    }

    /// <summary>
    /// A client on the host's version joining a party with room is approved (no refusal).
    /// </summary>
    [Test]
    public void SameVersionWithRoom_IsApproved()
    {
        Assert.That(JoinApproval.Evaluate(JoinApproval.CreatePayload("1.2", "player"), "1.2", 3, 4), Is.Null);
    }

    /// <summary>
    /// Joining a full party is refused as full, naming the player limit, and as nothing else.
    /// </summary>
    [Test]
    public void FullParty_IsRefused()
    {
        string refusal = JoinApproval.Evaluate(JoinApproval.CreatePayload("1.2", "player"), "1.2", 4, 4);

        Assert.That(JoinApproval.IsFull(refusal, out int maxPlayers), Is.True);
        Assert.That(maxPlayers, Is.EqualTo(4));
        Assert.That(JoinApproval.IsOtherBuild(refusal, out _), Is.False);
    }

    /// <summary>
    /// A client on another version is refused as another build, naming the host's version (the client knows its own),
    /// and as nothing else.
    /// </summary>
    [Test]
    public void OtherVersion_IsRefusedNamingTheHostsVersion()
    {
        string refusal = JoinApproval.Evaluate(JoinApproval.CreatePayload("1.1", "player"), "1.2", 1, 4);

        Assert.That(JoinApproval.IsOtherBuild(refusal, out string hostVersion), Is.True);
        Assert.That(hostVersion, Is.EqualTo("1.2"));
        Assert.That(JoinApproval.IsFull(refusal, out _), Is.False);
    }

    /// <summary>
    /// A null or empty connection payload is refused as another build.
    /// </summary>
    [TestCase(null)]
    [TestCase(new byte[0])]
    public void MissingPayload_IsRefusedAsAnUnknownVersion(byte[] payload)
    {
        Assert.That(JoinApproval.Evaluate(payload, "1.2", 1, 4), Is.EqualTo(JoinApproval.OtherBuild("1.2")));
    }

    /// <summary>
    /// A client on another version joining a full party is refused for its version, not for the party size.
    /// </summary>
    [Test]
    public void VersionIsCheckedBeforeRoom()
    {
        Assert.That(JoinApproval.Evaluate(JoinApproval.CreatePayload("1.1", "player"), "1.2", 4, 4), Is.EqualTo(JoinApproval.OtherBuild("1.2")));
    }

    /// <summary>
    /// An encoded refusal decodes back to the same refusal, which keeps its kind.
    /// </summary>
    [Test]
    public void Refusal_RoundTripsThroughADisconnectReason()
    {
        string encoded = JoinApproval.EncodeRefusal(JoinApproval.Full(4));

        Assert.That(JoinApproval.TryDecodeRefusal(encoded, out string reason), Is.True);
        Assert.That(reason, Is.EqualTo(JoinApproval.Full(4)));
        Assert.That(JoinApproval.IsFull(reason, out int maxPlayers) && maxPlayers == 4, Is.True);
    }

    /// <summary>
    /// A refusal of a kind this build does not know, or none, is neither full nor another build.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("the game is full (4 players).")]
    [TestCase("full:many")]
    public void OtherRefusals_AreNeitherFullNorAnotherBuild(string refusal)
    {
        Assert.That(JoinApproval.IsFull(refusal, out _), Is.False);
        Assert.That(JoinApproval.IsOtherBuild(refusal, out _), Is.False);
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
