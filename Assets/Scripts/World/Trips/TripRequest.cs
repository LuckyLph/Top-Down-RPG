using UnityEngine;

/// <summary>
/// A player's machine asking the trip's authority to take <see cref="Player"/> through the exit whose persistent id is
/// <see cref="ExitId"/> (Docs/WorldStreaming.md: Travelling between worlds). A client's request crosses the network
/// with the position its own machine has for its player (<see cref="HasPosition"/>), since the host's copy of that
/// player lags behind it and clients own their movement; a local request has none and the player's body is read.
/// </summary>
public readonly struct TripRequest
{
    /// <summary>
    /// Creates the request of <paramref name="player"/> to take the exit <paramref name="exitId"/>, from where the
    /// player's body stands.
    /// </summary>
    public TripRequest(PlayerHandle player, string exitId)
    {
        Player = player;
        ExitId = exitId;
        Position = default;
        HasPosition = false;
    }

    /// <summary>
    /// Creates the request of <paramref name="player"/>, standing at <paramref name="position"/> on its own machine, to
    /// take the exit <paramref name="exitId"/>.
    /// </summary>
    public TripRequest(PlayerHandle player, string exitId, Vector2 position)
    {
        Player = player;
        ExitId = exitId;
        Position = position;
        HasPosition = true;
    }

    public PlayerHandle Player { get; }
    public string ExitId { get; }
    public Vector2 Position { get; }
    public bool HasPosition { get; }
}
