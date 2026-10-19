using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The rules of raised ground shared by the terrain renderer, navigation and the scene generator: what a cell of a
/// raised tilemap is (<see cref="KindOf"/>), which steps the cliff between raised and level ground stops
/// (<see cref="CanCross"/>), where its face is (<see cref="IsOnFace(Tilemap, Vector3Int, int)"/>,
/// <see cref="OffTheFace"/>) and which cells are the plateau's rim (<see cref="IsCliff"/>). Plain rules over tilemap
/// reads, so every machine that loads the same scene stops units at the same lines.
/// </summary>
public static class RaisedGround
{
    public const byte Level = 0;
    public const byte Raised = 1;
    public const byte Ramp = 2;

    private const float RowTolerance = 0.001f;

    /// <summary>
    /// A source of cell kinds (<see cref="Level"/>, <see cref="Raised"/>, <see cref="Ramp"/>) for the face rules.
    /// </summary>
    public interface IKinds
    {
        /// <summary>
        /// What <paramref name="cell"/> is.
        /// </summary>
        byte KindAt(Vector3Int cell);
    }

    /// <summary>
    /// The kinds of the cells of one raised tilemap (<see cref="RaisedGround.KindAt(Tilemap, Vector3Int)"/>).
    /// </summary>
    public readonly struct TilemapKinds : IKinds
    {
        private readonly Tilemap raised;

        /// <summary>
        /// Reads the kinds of <paramref name="raisedTilemap"/>.
        /// </summary>
        public TilemapKinds(Tilemap raisedTilemap)
        {
            raised = raisedTilemap;
        }

        /// <summary>
        /// What <paramref name="cell"/> of the tilemap is.
        /// </summary>
        public byte KindAt(Vector3Int cell)
        {
            return RaisedGround.KindAt(raised, cell);
        }
    }

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
    /// Whether a unit may step from a cell of kind <paramref name="from"/> to the neighbour <paramref name="step"/> away
    /// (one cell along x or y), of kind <paramref name="to"/>. The cliff runs between raised ground, ramps included, and
    /// level ground, so a step between them is refused, except between a ramp and the level cell right below it, where
    /// the ramp slopes down. Raised ground and ramps mix freely, as level cells do.
    /// </summary>
    public static bool CanCross(byte from, byte to, Vector3Int step)
    {
        if ((from == Level) == (to == Level))
        {
            return true;
        }

        return (from == Ramp && step == Vector3Int.down) || (to == Ramp && step == Vector3Int.up);
    }

    /// <summary>
    /// How many rows of cells a cliff face <paramref name="faceHeight"/> cells tall covers whole, from its foot up.
    /// </summary>
    public static int RowsCovered(float faceHeight)
    {
        return Mathf.Max(0, Mathf.FloorToInt(faceHeight + RowTolerance));
    }

    /// <summary>
    /// How many rows of cells a cliff face <paramref name="faceHeight"/> cells tall reaches into, whole or in part, from
    /// its foot up.
    /// </summary>
    public static int RowsReached(float faceHeight)
    {
        return Mathf.Max(0, Mathf.CeilToInt(faceHeight - RowTolerance));
    }

    /// <summary>
    /// Whether <paramref name="cell"/> of <paramref name="raised"/> lies in the bottom <paramref name="rows"/> rows of a
    /// cliff face: raised ground with level ground at most that many cells below it, raised all the way down, whose
    /// lowest raised cell (the face's foot) is no ramp. Above a ramp the face is the ramp's slope instead.
    /// </summary>
    public static bool IsOnFace(Tilemap raised, Vector3Int cell, int rows)
    {
        return IsOnFace(new TilemapKinds(raised), cell, rows);
    }

    /// <summary>
    /// The same rule as <see cref="IsOnFace(Tilemap, Vector3Int, int)"/> over any source of cell kinds, such as
    /// navigation pages reading raised ground across chunk borders.
    /// </summary>
    public static bool IsOnFace<TKinds>(TKinds kinds, Vector3Int cell, int rows) where TKinds : IKinds
    {
        cell.z = 0;
        for (int up = 0; up < rows; up++)
        {
            Vector3Int foot = cell + (Vector3Int.down * up);
            if (kinds.KindAt(foot) == Level)
            {
                return false;
            }

            if (kinds.KindAt(foot + Vector3Int.down) == Level)
            {
                return kinds.KindAt(foot) == Raised;
            }
        }

        return false;
    }

    /// <summary>
    /// The same rule as <see cref="IsOnFace(Tilemap, Vector3Int, int)"/> over cell sets, for layouts that are not on a
    /// tilemap yet: <paramref name="raised"/> holds every raised cell, ramps included, and <paramref name="ramps"/> the
    /// ramps.
    /// </summary>
    public static bool IsOnFace(ISet<Vector3Int> raised, ISet<Vector3Int> ramps, Vector3Int cell, int rows)
    {
        for (int up = 0; up < rows; up++)
        {
            Vector3Int foot = cell + (Vector3Int.down * up);
            if (!raised.Contains(foot))
            {
                return false;
            }

            if (!raised.Contains(foot + Vector3Int.down))
            {
                return !ramps.Contains(foot);
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="point"/> moved off the cliff face of <paramref name="raised"/>: the face rises
    /// <paramref name="faceHeight"/> cells from the bottom of the lowest cell of raised ground that is no ramp with level
    /// ground below it, through the raised cells above it, and a point on it moves straight up to its top. Any other
    /// point, one above a ramp included, or any point without a face height, is returned as it is.
    /// </summary>
    public static Vector2 OffTheFace(Tilemap raised, Vector2 point, float faceHeight)
    {
        return OffTheFace(new TilemapKinds(raised), raised, point, faceHeight);
    }

    /// <summary>
    /// The same as <see cref="OffTheFace(Tilemap, Vector2, float)"/> over any source of cell kinds, with
    /// <paramref name="layout"/> converting between world positions and its cells.
    /// </summary>
    public static Vector2 OffTheFace<TKinds>(TKinds kinds, GridLayout layout, Vector2 point, float faceHeight) where TKinds : IKinds
    {
        Vector3Int cell = layout.WorldToCell(point);
        cell.z = 0;
        int rows = RowsReached(faceHeight);
        if (!IsOnFace(kinds, cell, rows))
        {
            return point;
        }

        Vector3Int foot = cell;
        while (kinds.KindAt(foot + Vector3Int.down) != Level)
        {
            foot += Vector3Int.down;
        }

        float bottom = layout.CellToWorld(foot).y;
        float top = bottom + faceHeight * (layout.CellToWorld(foot + Vector3Int.up).y - bottom);
        return point.y < top ? new Vector2(point.x, top) : point;
    }

    /// <summary>
    /// Whether <paramref name="cell"/> is on the plateau's rim in a layout that is not on a tilemap yet: raised ground
    /// that is no ramp with level ground among its 8 neighbours, where the cliff face and the ledge are drawn. The
    /// generator keeps mobs and props off it and, counting it as blocked, checks what the spawn reaches.
    /// <paramref name="raised"/> holds every raised cell, ramps included, and <paramref name="ramps"/> the ramps.
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
}
