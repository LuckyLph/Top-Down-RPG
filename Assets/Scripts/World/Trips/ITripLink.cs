using UnityEngine;

/// <summary>
/// A client's way to ask the host for its own player's trip (Docs/WorldStreaming.md: Networking): on the player object,
/// <see cref="PlayerWorldSync"/> sends it as an RPC. Tests fake it.
/// </summary>
public interface ITripLink
{
    /// <summary>
    /// Sends the host this machine's request to take its player, standing at <paramref name="position"/> here, through
    /// the exit whose persistent id is <paramref name="exitId"/>.
    /// </summary>
    void SendTripRequest(string exitId, Vector2 position);
}
