/// <summary>
/// What the host does when a client's readiness changes (Docs/WorldStreaming.md: Networking): show it the spawned
/// objects it may now see, hide the ones it may no longer see, and acknowledge a chunk it is releasing.
/// <see cref="NetworkSession"/> does it with Netcode; tests record the calls.
/// </summary>
public interface IClientVisibility
{
    /// <summary>
    /// Shows <paramref name="clientId"/> every spawned object it may see now and does not see yet.
    /// </summary>
    void ShowNowVisible(ulong clientId);

    /// <summary>
    /// Hides from <paramref name="clientId"/> every spawned object it sees and may no longer see.
    /// </summary>
    void HideNoLongerVisible(ulong clientId);

    /// <summary>
    /// Tells <paramref name="clientId"/> it may unload <paramref name="chunk"/>: nothing of it is shown to it any more.
    /// </summary>
    void AcknowledgeRelease(ulong clientId, ChunkId chunk);
}
