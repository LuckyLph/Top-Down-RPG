using System;
using System.Collections.Generic;

/// <summary>
/// The host's record of what each client can receive (Docs/WorldStreaming.md: Networking), and the readiness and
/// release handshake over it. A client joins once its Gameplay scene is up and may then see the players. It reports
/// each chunk ready once the chunk is up on its machine, tagged with its player's trip count; a report from before the
/// client's latest trip is ignored. The objects whose home chunk it holds become visible to it. A trip ends what the
/// client held before it (Docs/KnownGaps.md, Phase 4): the chunks of every other world than the trip's are released as
/// it sets off, their objects hidden from it, and those of the trip's world stay held, so the client keeps seeing them,
/// but count as ready for the trip only once reported during it. Before unloading a chunk the client releases it: the
/// host forgets the chunk, hides its objects from it and only then acknowledges, so a client never destroys a spawned
/// object itself. The host never unloads a chunk some client holds. Plain logic: the showing, hiding and acknowledging
/// go through an <see cref="IClientVisibility"/>, so it is tested without Netcode.
/// </summary>
public sealed class ClientChunkReadiness
{
    private sealed class ClientRecord
    {
        public int Trip { get; set; }
        public bool Settled { get; set; }
        public Dictionary<ChunkId, int> Chunks { get; } = new();
        public List<ChunkId> Leaving { get; } = new();
    }

    private readonly IClientVisibility visibility;
    private readonly Dictionary<ulong, ClientRecord> clients = new();

    /// <summary>
    /// Creates an empty record whose changes show, hide and acknowledge through <paramref name="clientVisibility"/>.
    /// </summary>
    public ClientChunkReadiness(IClientVisibility clientVisibility)
    {
        visibility = clientVisibility;
    }

    /// <summary>
    /// The clients that joined and have not left.
    /// </summary>
    public IReadOnlyCollection<ulong> JoinedClients => clients.Keys;

    /// <summary>
    /// Whether <paramref name="clientId"/> joined and has not left.
    /// </summary>
    public bool IsJoined(ulong clientId)
    {
        return clients.ContainsKey(clientId);
    }

    /// <summary>
    /// Records that <paramref name="clientId"/>'s Gameplay scene is up, at trip 0 and holding no chunk, and shows it
    /// what it may now see (the players). Returns false, changing nothing, when it had already joined.
    /// </summary>
    public bool Join(ulong clientId)
    {
        if (clients.ContainsKey(clientId))
        {
            return false;
        }

        clients.Add(clientId, new ClientRecord());
        visibility.ShowNowVisible(clientId);
        return true;
    }

    /// <summary>
    /// Forgets <paramref name="clientId"/> and every chunk it held, as it disconnects.
    /// </summary>
    public void Leave(ulong clientId)
    {
        clients.Remove(clientId);
    }

    /// <summary>
    /// Forgets every client, as a session starts or ends.
    /// </summary>
    public void Clear()
    {
        clients.Clear();
    }

    /// <summary>
    /// Records that <paramref name="clientId"/>'s player set off on its trip number <paramref name="trip"/> into the
    /// world whose id is <paramref name="worldId"/>: reports tagged with an earlier trip are ignored from now on, and the
    /// client is no longer settled until it reports a chunk for this trip. The chunks it holds in other worlds are
    /// released, what it may no longer see hidden from it; those of the trip's world stay held but are ready for the
    /// trip only once reported again. An older trip number changes nothing.
    /// </summary>
    public void BeginTrip(ulong clientId, int trip, string worldId)
    {
        if (!clients.TryGetValue(clientId, out ClientRecord record) || trip <= record.Trip)
        {
            return;
        }

        record.Trip = trip;
        record.Settled = false;
        record.Leaving.Clear();
        foreach (ChunkId chunk in record.Chunks.Keys)
        {
            if (!string.Equals(chunk.WorldId, worldId ?? string.Empty, StringComparison.Ordinal))
            {
                record.Leaving.Add(chunk);
            }
        }

        foreach (ChunkId chunk in record.Leaving)
        {
            record.Chunks.Remove(chunk);
        }

        if (record.Leaving.Count > 0)
        {
            record.Leaving.Clear();
            visibility.HideNoLongerVisible(clientId);
        }
    }

    /// <summary>
    /// The trip count the host last recorded for <paramref name="clientId"/>; 0 for a client that has not joined.
    /// </summary>
    public int TripOf(ulong clientId)
    {
        return clients.TryGetValue(clientId, out ClientRecord record) ? record.Trip : 0;
    }

    /// <summary>
    /// Takes <paramref name="clientId"/>'s report that <paramref name="chunk"/> is up on its machine, made during its
    /// trip <paramref name="trip"/>. A report from a client that has not joined, for another trip or for no chunk is
    /// ignored and returns false; otherwise the chunk is held and ready for this trip, the client is settled in its trip
    /// and, for a chunk it did not hold, it is shown what it may now see.
    /// </summary>
    public bool ReportReady(ulong clientId, int trip, ChunkId chunk)
    {
        if (!chunk.IsValid || !clients.TryGetValue(clientId, out ClientRecord record) || trip != record.Trip)
        {
            return false;
        }

        record.Settled = true;
        bool added = !record.Chunks.ContainsKey(chunk);
        record.Chunks[chunk] = trip;
        if (added)
        {
            visibility.ShowNowVisible(clientId);
        }

        return true;
    }

    /// <summary>
    /// Takes <paramref name="clientId"/>'s notice that it is about to unload <paramref name="chunk"/>: forgets the
    /// chunk, hides from the client what it may no longer see, and only then acknowledges, whether or not the chunk was
    /// held (a report the host ignored, or a chunk its trip released, still leaves the client waiting). Returns whether
    /// it was held.
    /// </summary>
    public bool Release(ulong clientId, ChunkId chunk)
    {
        bool held = clients.TryGetValue(clientId, out ClientRecord record) && record.Chunks.Remove(chunk);
        if (held)
        {
            visibility.HideNoLongerVisible(clientId);
        }

        visibility.AcknowledgeRelease(clientId, chunk);
        return held;
    }

    /// <summary>
    /// Whether <paramref name="clientId"/> holds <paramref name="chunk"/>, so it may see the objects whose home chunk
    /// that is, whichever trip it reported it in.
    /// </summary>
    public bool Holds(ulong clientId, ChunkId chunk)
    {
        return clients.TryGetValue(clientId, out ClientRecord record) && record.Chunks.ContainsKey(chunk);
    }

    /// <summary>
    /// Whether <paramref name="clientId"/> reported <paramref name="chunk"/> ready during its current trip: the chunk is
    /// up on its machine for where its player is now, as a respawn at a waystone there waits for.
    /// </summary>
    public bool IsReady(ulong clientId, ChunkId chunk)
    {
        return clients.TryGetValue(clientId, out ClientRecord record) && record.Chunks.TryGetValue(chunk, out int trip) && trip == record.Trip;
    }

    /// <summary>
    /// Whether <paramref name="clientId"/> reported a chunk ready during its current trip: it has ground to stand on
    /// where its player is, so the host stops holding that player.
    /// </summary>
    public bool IsSettled(ulong clientId)
    {
        return clients.TryGetValue(clientId, out ClientRecord record) && record.Settled;
    }

    /// <summary>
    /// Whether some client holds <paramref name="chunk"/>, so the host must keep it loaded.
    /// </summary>
    public bool IsHeld(ChunkId chunk)
    {
        foreach (ClientRecord record in clients.Values)
        {
            if (record.Chunks.ContainsKey(chunk))
            {
                return true;
            }
        }

        return false;
    }
}
