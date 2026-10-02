using NUnit.Framework;

public class JoinApprovalTests
{
    [Test]
    public void SameVersionWithRoom_IsApproved()
    {
        Assert.That(JoinApproval.Evaluate(JoinApproval.CreatePayload("1.2"), "1.2", 3, 4), Is.Null);
    }

    [Test]
    public void FullParty_IsRefused()
    {
        string refusal = JoinApproval.Evaluate(JoinApproval.CreatePayload("1.2"), "1.2", 4, 4);

        Assert.That(refusal, Does.Contain("full"));
        Assert.That(refusal, Does.Contain("4 players"));
    }

    [Test]
    public void OtherVersion_IsRefusedNamingBothVersions()
    {
        string refusal = JoinApproval.Evaluate(JoinApproval.CreatePayload("1.1"), "1.2", 1, 4);

        Assert.That(refusal, Does.Contain("1.1"));
        Assert.That(refusal, Does.Contain("1.2"));
    }

    [TestCase(null)]
    [TestCase(new byte[0])]
    public void MissingPayload_IsRefusedAsAnUnknownVersion(byte[] payload)
    {
        Assert.That(JoinApproval.Evaluate(payload, "1.2", 1, 4), Does.Contain("unknown version"));
    }

    [Test]
    public void VersionIsCheckedBeforeRoom()
    {
        Assert.That(JoinApproval.Evaluate(JoinApproval.CreatePayload("1.1"), "1.2", 4, 4), Does.Contain("1.1"));
    }

    [Test]
    public void Refusal_RoundTripsThroughADisconnectReason()
    {
        string encoded = JoinApproval.EncodeRefusal("the game is full (4 players).");

        Assert.That(JoinApproval.TryDecodeRefusal(encoded, out string reason), Is.True);
        Assert.That(reason, Is.EqualTo("the game is full (4 players)."));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("[Disconnect Event][Client-1][TransportClientId-1][Disconnected] Gracefully disconnected.")]
    public void OtherDisconnectReasons_AreNotRefusals(string disconnectReason)
    {
        Assert.That(JoinApproval.TryDecodeRefusal(disconnectReason, out string reason), Is.False);
        Assert.That(reason, Is.Null);
    }
}
