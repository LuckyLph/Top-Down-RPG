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
    /// Whether client <paramref name="clientId"/> holds <paramref name="chunk"/>, so it may see the objects whose home
    /// chunk that is; always true for the host's own client.
    /// </summary>
    bool IsClientReady(ulong clientId, ChunkId chunk);

    /// <summary>
    /// Host only: records that <paramref name="clientId"/>'s player set off on its trip number <paramref name="trip"/>,
    /// so its reports from before the trip are ignored.
    /// </summary>
    void BeginTrip(ulong clientId, int trip);
}
