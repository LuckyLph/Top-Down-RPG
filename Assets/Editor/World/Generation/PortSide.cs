using UnityEngine;

/// <summary>
/// The side of its region a port faces (Docs/WorldGeneration.md: Terms): the way its region's road runs out of the
/// outline there.
/// </summary>
public enum PortSide
{
    East,
    North,
    West,
    South
}

/// <summary>
/// The steps of a <see cref="PortSide"/>.
/// </summary>
public static class PortSides
{
    /// <summary>
    /// The step out of the region through a port on <paramref name="side"/>.
    /// </summary>
    public static Vector2Int Step(PortSide side)
    {
        return side switch
        {
            PortSide.East => Vector2Int.right,
            PortSide.North => Vector2Int.up,
            PortSide.West => Vector2Int.left,
            _ => Vector2Int.down
        };
    }
}
