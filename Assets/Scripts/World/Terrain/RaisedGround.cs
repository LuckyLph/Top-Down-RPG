using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The rules of raised ground shared by the terrain renderer, navigation and the scene generator: what a cell of a
/// raised tilemap is (<see cref="KindOf"/>) and which cells are cliff, where no unit walks (<see cref="IsCliff"/>).
/// Plain rules over tilemap reads, so every machine that loads the same scene blocks the same cells.
/// </summary>
public static class RaisedGround
{
    public const byte Level = 0;
    public const byte Raised = 1;
    public const byte Ramp = 2;

    private static readonly Vector3Int[] Neighbours =
    {
        new(-1, -1, 0), new(0, -1, 0), new(1, -1, 0), new(-1, 0, 0), new(1, 0, 0), new(-1, 1, 0), new(0, 1, 0), new(1, 1, 0)
    };

    /// <summary>
    /// What a cell holding <paramref name="tile"/> on a raised tilemap is: <see cref="Level"/> without a tile,
    /// <see cref="Ramp"/> for a <see cref="RaisedGroundTile"/> that is a ramp, else <see cref="Raised"/>.
    /// </summary>
    public static byte KindOf(TileBase tile)
    {
        return tile == null ? Level : tile is RaisedGroundTile { IsRamp: true } ? Ramp : Raised;
    }

    /// <summary>
    /// What <paramref name="cell"/> of <paramref name="raised"/> is (<see cref="KindOf"/>), z = 0.
    /// </summary>
    public static byte KindAt(Tilemap raised, Vector3Int cell)
    {
        return KindOf(raised.GetTile(new Vector3Int(cell.x, cell.y, 0)));
    }

    /// <summary>
    /// Whether <paramref name="cell"/> of <paramref name="raised"/> is cliff: raised ground that is no ramp with level
    /// ground among its 8 neighbours. The plateau's rim is cliff all round, its cliff face along the bottom and its
    /// edge along the sides and back, so units move between raised and level ground only over a ramp.
    /// </summary>
    public static bool IsCliff(Tilemap raised, Vector3Int cell)
    {
        if (KindAt(raised, cell) != Raised)
        {
            return false;
        }

        foreach (Vector3Int offset in Neighbours)
        {
            if (KindAt(raised, cell + offset) == Level)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The same rule as <see cref="IsCliff(Tilemap, Vector3Int)"/> over cell sets, for layouts that are not on a
    /// tilemap yet: <paramref name="raised"/> holds every raised cell, ramps included, and <paramref name="ramps"/> the
    /// ramps.
    /// </summary>
    public static bool IsCliff(ISet<Vector3Int> raised, ISet<Vector3Int> ramps, Vector3Int cell)
    {
        if (!raised.Contains(cell) || ramps.Contains(cell))
        {
            return false;
        }

        foreach (Vector3Int offset in Neighbours)
        {
            if (!raised.Contains(cell + offset))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The 8 neighbours of a cell as offsets, for callers that re-read the cells around a changed one.
    /// </summary>
    public static IReadOnlyList<Vector3Int> NeighbourOffsets => Neighbours;
}
