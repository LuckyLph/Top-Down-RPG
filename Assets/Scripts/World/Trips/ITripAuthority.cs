using System;

/// <summary>
/// Where a player's machine sends its trip requests, and hears back which trips go (Docs/WorldStreaming.md: Travelling
/// between worlds). The authority checks each request, prepares the target world, puts the player in it, then raises
/// <see cref="Approved"/> for that player's machine to fade and arrive. Offline it is <see cref="LocalTripAuthority"/>;
/// Phase 7 puts a network message between the request and the check, and between the approval and the player's machine.
/// </summary>
public interface ITripAuthority
{
    /// <summary>
    /// Raised on the player's machine once its trip is approved and the player is in the target world.
    /// </summary>
    event Action<TripApproval> Approved;

    /// <summary>
    /// Asks for <paramref name="request"/>'s trip. A refused request is logged and changes nothing.
    /// </summary>
    void Request(TripRequest request);

    /// <summary>
    /// Tells the authority that <paramref name="player"/>'s machine faded in at the end of its trip number
    /// <paramref name="trip"/>, which ends the protection the trip gave it (<see cref="TripProtection"/>).
    /// </summary>
    void ReportArrived(PlayerHandle player, int trip);
}
