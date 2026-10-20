/// <summary>
/// Which chunks a machine must keep loaded for others: on the host, every chunk some client still holds
/// (Docs/WorldStreaming.md: Networking). The streaming never unloads one, except when the whole session ends.
/// </summary>
public interface IChunkHolds
{
    /// <summary>
    /// Whether some other machine still holds <paramref name="chunk"/>.
    /// </summary>
    bool IsChunkHeld(ChunkId chunk);
}
