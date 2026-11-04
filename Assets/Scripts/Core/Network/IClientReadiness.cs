/// <summary>
/// The host's view of what each connected client has loaded (Docs/WorldStreaming.md: Networking). The host's own client
/// always is ready.
/// </summary>
public interface IClientReadiness
{
    /// <summary>
    /// Whether client <paramref name="clientId"/> reported a chunk ready since its player's latest trip began, so it has
    /// ground under its player; always true for the host's own client. The host holds a client's player until then.
    /// </summary>
    bool IsClientReady(ulong clientId);

    /// <summary>
    /// Whether client <paramref name="clientId"/> reported <paramref name="chunk"/> ready during its player's current
    /// trip, so the chunk is up on its machine for where its player is now; always true for the host's own client. A
    /// respawn at a waystone waits for it, as the host waits for its own streaming to have the chunk loaded.
    /// </summary>
    bool IsClientReady(ulong clientId, ChunkId chunk);

    /// <summary>
    /// Host only: records that <paramref name="clientId"/>'s player set off on its trip number <paramref name="trip"/>
    /// into the world whose id is <paramref name="worldId"/>, so its reports from before the trip are ignored, the
    /// chunks it holds in other worlds are released and those of that world count as ready again once reported during
    /// the trip.
    /// </summary>
    void BeginTrip(ulong clientId, int trip, string worldId);
}
