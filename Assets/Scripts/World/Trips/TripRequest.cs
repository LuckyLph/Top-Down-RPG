/// <summary>
/// A player's machine asking the trip's authority to take <see cref="Player"/> through the exit whose persistent id is
/// <see cref="ExitId"/> (Docs/WorldStreaming.md: Travelling between worlds). Plain data, so Phase 7 can send it as an
/// RPC naming the exit's id.
/// </summary>
public readonly struct TripRequest
{
    /// <summary>
    /// Creates the request of <paramref name="player"/> to take the exit <paramref name="exitId"/>.
    /// </summary>
    public TripRequest(PlayerHandle player, string exitId)
    {
        Player = player;
        ExitId = exitId;
    }

    public PlayerHandle Player { get; }
    public string ExitId { get; }
}
