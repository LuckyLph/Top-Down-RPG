using UnityEngine;

/// <summary>
/// A client's way to ask the host for its own player's trip and to tell it the trip is over (Docs/WorldStreaming.md:
/// Networking): on the player object, <see cref="PlayerWorldSync"/> sends each as an RPC. Tests fake it.
/// </summary>
public interface ITripLink
{
    /// <summary>
    /// Sends the host this machine's request to take its player, standing at <paramref name="position"/> here, through
    /// the exit whose persistent id is <paramref name="exitId"/>.
    /// </summary>
    void SendTripRequest(string exitId, Vector2 position);

    /// <summary>
    /// Tells the host this machine faded in at the end of its player's trip number <paramref name="trip"/>, which ends
    /// the protection the host gave the player for it.
    /// </summary>
    void SendTripArrived(int trip);
}
