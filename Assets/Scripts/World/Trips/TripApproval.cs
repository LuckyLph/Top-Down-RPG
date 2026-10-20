using UnityEngine;

/// <summary>
/// The trip's authority telling a player's machine to go: <see cref="Player"/> is now in <see cref="World"/> and
/// arrives at its entry <see cref="EntryName"/>, at <see cref="Position"/> in world units.
/// </summary>
public readonly struct TripApproval
{
    /// <summary>
    /// Creates the approval of <paramref name="player"/>'s trip to <paramref name="world"/>'s entry
    /// <paramref name="entryName"/> at <paramref name="position"/>.
    /// </summary>
    public TripApproval(PlayerHandle player, WorldDefinition world, string entryName, Vector2 position)
    {
        Player = player;
        World = world;
        EntryName = entryName;
        Position = position;
    }

    public PlayerHandle Player { get; }
    public WorldDefinition World { get; }
    public string EntryName { get; }
    public Vector2 Position { get; }
}
