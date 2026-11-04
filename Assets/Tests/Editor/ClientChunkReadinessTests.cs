using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="ClientChunkReadiness"/>, the host's per-chunk readiness and release handshake as a state
/// machine: joining shows the players, a chunk reported ready for the client's current trip is held and shown, a report
/// from before a trip is ignored, a held chunk is ready for a trip only once reported during it, a trip into another
/// world releases the world it leaves, a release hides before it acknowledges, holds keep a chunk loaded, and leaving
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
    /// Once the client's player sets off on a trip within its world, a report tagged with the trip before it is ignored
    /// (it neither shows anything nor settles the client); the chunks it held stay held, so it keeps seeing them, but
    /// count as ready for the trip only once reported during it (Docs/KnownGaps.md, Phase 4). An older trip number
    /// changes nothing.
    /// </summary>
    [Test]
    public void AReportFromBeforeATrip_IsIgnored_AndAHeldChunkIsReadyAgainOnlyOnceReported()
    {
        readiness.Join(Client);
        readiness.ReportReady(Client, 0, Start);
        visibility.Calls.Clear();

        readiness.BeginTrip(Client, 1, Start.WorldId);
        Assert.That(readiness.IsSettled(Client), Is.False, "A travelling client is held again.");
        Assert.That(readiness.Holds(Client, Start), Is.True, "Its world's chunks stay held, their objects visible to it.");
        Assert.That(readiness.IsHeld(Start), Is.True);
        Assert.That(readiness.IsReady(Client, Start), Is.False, "A chunk held from before the trip is not ready for it.");
        Assert.That(visibility.Calls, Is.Empty, "Nothing it holds in the trip's world is hidden.");

        Assert.That(readiness.ReportReady(Client, 0, East), Is.False);
        Assert.That(readiness.IsReady(Client, East), Is.False);
        Assert.That(readiness.IsSettled(Client), Is.False);
        Assert.That(visibility.Calls, Is.Empty);

        Assert.That(readiness.ReportReady(Client, 1, Start), Is.True);
        Assert.That(readiness.IsReady(Client, Start), Is.True, "Reported during the trip, it is ready for it.");
        Assert.That(readiness.IsSettled(Client), Is.True);
        Assert.That(visibility.Calls, Is.Empty, "A chunk it already held shows nothing new.");

        readiness.BeginTrip(Client, 0, Interior.WorldId);
        Assert.That(readiness.TripOf(Client), Is.EqualTo(1));
        Assert.That(readiness.IsSettled(Client), Is.True);
        Assert.That(readiness.IsReady(Client, Start), Is.True, "An older trip number releases nothing.");
    }

    /// <summary>
    /// A trip into another world releases what the client held in the world it leaves: those chunks are forgotten and
    /// their objects hidden from it, so the host may unload them, while its holds in the trip's world stay; a chunk of
    /// the new world reported during the trip is held and shown; and its later release of a chunk the trip already
    /// released is still acknowledged.
    /// </summary>
    [Test]
    public void ATripIntoAnotherWorld_ReleasesTheHoldsOfTheWorldItLeaves()
    {
        readiness.Join(Client);
        readiness.ReportReady(Client, 0, Start);
        readiness.ReportReady(Client, 0, East);
        visibility.Calls.Clear();

        readiness.BeginTrip(Client, 1, Interior.WorldId);
        Assert.That(visibility.Calls, Is.EqualTo(new[] { "hide 3" }), "The world it leaves is hidden from it at once.");
        Assert.That(readiness.Holds(Client, Start), Is.False);
        Assert.That(readiness.Holds(Client, East), Is.False);
        Assert.That(readiness.IsHeld(Start), Is.False, "The host may unload what it no longer holds.");

        visibility.Calls.Clear();
        Assert.That(readiness.ReportReady(Client, 1, Interior), Is.True);
        Assert.That(readiness.IsReady(Client, Interior), Is.True);
        Assert.That(visibility.Calls, Is.EqualTo(new[] { "show 3" }));

        visibility.Calls.Clear();
        Assert.That(readiness.Release(Client, Start), Is.False, "Its streaming releases the old chunk as it unloads it.");
        Assert.That(visibility.Calls, Is.EqualTo(new[] { "ack 3 " + Start }));
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
