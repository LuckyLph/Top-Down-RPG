using System.Threading;
using UnityEngine;

/// <summary>
/// The per-chunk readiness and release handshake as the world streaming sees it (Docs/WorldStreaming.md: Networking).
/// A client reports each chunk ready once it is up, and releases each before unloading it, waiting for the host's
/// acknowledgement; the host keeps every chunk a client holds (<see cref="IChunkHolds"/>). Offline and on the host the
/// client half does nothing.
/// </summary>
public interface IChunkHandshake : IChunkHolds
{
    /// <summary>
    /// Client only: tells the host <paramref name="chunk"/> is up here, with its network prefabs registered, during the
    /// local player's trip number <paramref name="trip"/>.
    /// </summary>
    void ReportChunkReady(int trip, ChunkId chunk);

    /// <summary>
    /// Client only: tells the host this machine is about to unload <paramref name="chunk"/> and completes once the host
    /// has hidden its objects and acknowledged, or the session has ended. Completes at once offline and on the host.
    /// </summary>
    Awaitable ReleaseChunkAsync(ChunkId chunk, CancellationToken cancellation);
}
