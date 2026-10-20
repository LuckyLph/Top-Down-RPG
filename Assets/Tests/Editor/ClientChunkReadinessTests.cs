using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="ClientChunkReadiness"/>, the host's per-chunk readiness and release handshake as a state
/// machine: joining shows the players, a chunk reported ready for the client's current trip is held and shown, a report
/// from before a trip is ignored, a release hides before it acknowledges, holds keep a chunk loaded, and leaving
/// forgets everything. Also <see cref="ChunkId"/>'s equality and its wire form.
/// </summary>
public class ClientChunkReadinessTests
{
    private const ulong Client = 3;
    private const ulong OtherClient = 5;

    private static readonly ChunkId Start = new("world-a", new Vector2Int(0, 0));
    private static readonly ChunkId East = new("world-a", new Vector2Int(1, 0));
    private static readonly ChunkId Interior = new("world-b", new Vector2Int(0, 0));

    private RecordingVisibility visibility;
    private ClientChunkReadiness readiness;

    /// <summary>
    /// A fresh record over a visibility that records its calls in order.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        visibility = new RecordingVisibility();
        readiness = new ClientChunkReadiness(visibility);
    }

    /// <summary>
    /// Joining once shows the client what it may see and joins it at trip 0, holding nothing and not settled; a second
    /// join changes nothing. A client that has not joined has its reports ignored.
    /// </summary>
    [Test]
    public void Joining_ShowsThePlayers_Once()
    {
        Assert.That(readiness.ReportReady(Client, 0, Start), Is.False, "A report before joining is ignored.");

        Assert.That(readiness.Join(Client), Is.True);
        Assert.That(readiness.Join(Client), Is.False);

        Assert.That(visibility.Calls, Is.EqualTo(new[] { "show 3" }));
        Assert.That(readiness.IsJoined(Client), Is.True);
        Assert.That(readiness.JoinedClients, Is.EquivalentTo(new[] { Client }));
        Assert.That(readiness.TripOf(Client), Is.Zero);
        Assert.That(readiness.IsSettled(Client), Is.False, "A joined client is held until it reports a chunk.");
        Assert.That(readiness.IsReady(Client, Start), Is.False);
    }

    /// <summary>
    /// A chunk reported ready for the current trip is held and shown once, and settles the client; reporting it again
    /// shows nothing more; an invalid chunk is ignored.
    /// </summary>
    [Test]
    public void AChunkReportedReady_IsHeldAndShown_AndSettlesTheClient()
    {
        readiness.Join(Client);
        visibility.Calls.Clear();

        Assert.That(readiness.ReportReady(Client, 0, Start), Is.True);
        Assert.That(readiness.ReportReady(Client, 0, Start), Is.True);
        Assert.That(readiness.ReportReady(Client, 0, default), Is.False);

        Assert.That(visibility.Calls, Is.EqualTo(new[] { "show 3" }));
        Assert.That(readiness.IsReady(Client, Start), Is.True);
        Assert.That(readiness.IsReady(Client, East), Is.False);
        Assert.That(readiness.IsSettled(Client), Is.True);
        Assert.That(readiness.IsHeld(Start), Is.True);
        Assert.That(readiness.IsHeld(East), Is.False);
    }

    /// <summary>
    /// Once the client's player sets off on a trip, a report tagged with the trip before it is ignored (it neither
    /// shows anything nor settles the client), while chunks held from before stay held; a report for the new trip
    /// counts. An older trip number changes nothing.
    /// </summary>
    [Test]
    public void AReportFromBeforeATrip_IsIgnored()
    {
        readiness.Join(Client);
        readiness.ReportReady(Client, 0, Start);
        visibility.Calls.Clear();

        readiness.BeginTrip(Client, 1);
        Assert.That(readiness.IsSettled(Client), Is.False, "A travelling client is held again.");
        Assert.That(readiness.IsReady(Client, Start), Is.True, "Its old world's chunks stay held until it releases them.");

        Assert.That(readiness.ReportReady(Client, 0, East), Is.False);
        Assert.That(readiness.IsReady(Client, East), Is.False);
        Assert.That(readiness.IsSettled(Client), Is.False);
        Assert.That(visibility.Calls, Is.Empty);

        Assert.That(readiness.ReportReady(Client, 1, Interior), Is.True);
        Assert.That(readiness.IsSettled(Client), Is.True);
        Assert.That(visibility.Calls, Is.EqualTo(new[] { "show 3" }));

        readiness.BeginTrip(Client, 0);
        Assert.That(readiness.TripOf(Client), Is.EqualTo(1));
        Assert.That(readiness.IsSettled(Client), Is.True);
    }

    /// <summary>
    /// Releasing a held chunk forgets it, hides what the client may no longer see and only then acknowledges; a chunk it
    /// did not hold (a report the host ignored) is acknowledged without hiding anything, so the client never waits for
    /// ever.
    /// </summary>
    [Test]
    public void AReleaseIsAcknowledged_OnlyAfterTheHide()
    {
        readiness.Join(Client);
        readiness.ReportReady(Client, 0, Start);
        visibility.Calls.Clear();

        Assert.That(readiness.Release(Client, Start), Is.True);
        Assert.That(visibility.Calls, Is.EqualTo(new[] { "hide 3", "ack 3 " + Start }));
        Assert.That(readiness.IsReady(Client, Start), Is.False);
        Assert.That(readiness.IsHeld(Start), Is.False);

        visibility.Calls.Clear();
        Assert.That(readiness.Release(Client, East), Is.False);
        Assert.That(visibility.Calls, Is.EqualTo(new[] { "ack 3 " + East }));
    }

    /// <summary>
    /// A chunk stays held while any client holds it, and a client leaving releases everything it held.
    /// </summary>
    [Test]
    public void TheHostKeepsAChunk_WhileAnyClientHoldsIt_AndLeavingReleasesIt()
    {
        readiness.Join(Client);
        readiness.Join(OtherClient);
        readiness.ReportReady(Client, 0, Start);
        readiness.ReportReady(OtherClient, 0, Start);

        readiness.Release(Client, Start);
        Assert.That(readiness.IsHeld(Start), Is.True, "The other client still holds it.");

        readiness.Leave(OtherClient);
        Assert.That(readiness.IsHeld(Start), Is.False);
        Assert.That(readiness.IsJoined(OtherClient), Is.False);
        Assert.That(readiness.IsReady(OtherClient, Start), Is.False);

        readiness.Clear();
        Assert.That(readiness.JoinedClients, Is.Empty);
    }

    /// <summary>
    /// Chunk ids compare by world id and coordinates, and survive their wire form.
    /// </summary>
    [Test]
    public void ChunkIds_CompareByValue_AndRoundTripTheWire()
    {
        HashSet<ChunkId> set = new() { new ChunkId("world-a", Vector2Int.zero) };
        Assert.That(set.Contains(Start), Is.True);
        Assert.That(Start.Equals(East), Is.False);
        Assert.That(Start.Equals(Interior), Is.False);
        Assert.That(default(ChunkId).IsValid, Is.False);
        Assert.That(new ChunkId(null, Vector2Int.zero).Equals(default(ChunkId)), Is.True);

        ChunkId far = new("45fbe595d915793439142f5a715ea480", new Vector2Int(15, -2));
        Assert.That(new NetworkChunkId(far).ToChunkId(), Is.EqualTo(far));
        Assert.That(new NetworkChunkId(far).IsValid, Is.True);
        Assert.That(default(NetworkChunkId).IsValid, Is.False);
    }

    /// <summary>
    /// Records each visibility call as text, in order.
    /// </summary>
    private sealed class RecordingVisibility : IClientVisibility
    {
        public List<string> Calls { get; } = new();

        /// <summary>
        /// Records a show for <paramref name="clientId"/>.
        /// </summary>
        public void ShowNowVisible(ulong clientId)
        {
            Calls.Add($"show {clientId}");
        }

        /// <summary>
        /// Records a hide for <paramref name="clientId"/>.
        /// </summary>
        public void HideNoLongerVisible(ulong clientId)
        {
            Calls.Add($"hide {clientId}");
        }

        /// <summary>
        /// Records an acknowledgement of <paramref name="chunk"/> to <paramref name="clientId"/>.
        /// </summary>
        public void AcknowledgeRelease(ulong clientId, ChunkId chunk)
        {
            Calls.Add($"ack {clientId} {chunk}");
        }
    }
}
