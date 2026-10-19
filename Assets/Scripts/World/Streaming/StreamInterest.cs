using UnityEngine;

/// <summary>
/// A player a machine streams chunks for: the world it is in and where it stands, in world units.
/// </summary>
public readonly struct StreamInterest
{
    /// <summary>
    /// Creates the interest of a player at <paramref name="position"/> in <paramref name="world"/>.
    /// </summary>
    public StreamInterest(WorldDefinition world, Vector2 position)
    {
        World = world;
        Position = position;
    }

    public WorldDefinition World { get; }
    public Vector2 Position { get; }
}
