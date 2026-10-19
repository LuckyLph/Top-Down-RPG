using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chunk arithmetic of a world: which chunk holds a cell, where a chunk lies, and which chunks a rectangle of cells
/// overlaps. Cells and chunks are counted from the world's origin.
/// </summary>
public static class ChunkCoords
{
    public const int Size = 64;

    /// <summary>
    /// The chunk holding the world-local <paramref name="cell"/>.
    /// </summary>
    public static Vector2Int ChunkOf(Vector2Int cell)
    {
        return new Vector2Int(FloorDiv(cell.x, Size), FloorDiv(cell.y, Size));
    }

    /// <summary>
    /// The world-local cell holding <paramref name="localPosition"/>, a position in units from the world's origin.
    /// </summary>
    public static Vector2Int CellOf(Vector2 localPosition)
    {
        return new Vector2Int(Mathf.FloorToInt(localPosition.x), Mathf.FloorToInt(localPosition.y));
    }

    /// <summary>
    /// The world-local cells of the chunk at <paramref name="chunk"/>.
    /// </summary>
    public static RectInt Bounds(Vector2Int chunk)
    {
        return new RectInt(chunk.x * Size, chunk.y * Size, Size, Size);
    }

    /// <summary>
    /// The cells within <paramref name="extents"/> of <paramref name="centre"/> on each side, centre included.
    /// </summary>
    public static RectInt Around(Vector2Int centre, Vector2Int extents)
    {
        return new RectInt(centre.x - extents.x, centre.y - extents.y, 2 * extents.x + 1, 2 * extents.y + 1);
    }

    /// <summary>
    /// Adds to <paramref name="chunks"/> every chunk that <paramref name="cells"/> overlaps and that lies inside
    /// <paramref name="sizeInChunks"/>, skipping those it already holds. Allocates nothing once the set has room.
    /// </summary>
    public static void AddOverlapping(RectInt cells, Vector2Int sizeInChunks, HashSet<Vector2Int> chunks)
    {
        if (cells.width <= 0 || cells.height <= 0)
        {
            return;
        }

        Vector2Int min = ChunkOf(cells.min);
        Vector2Int max = ChunkOf(new Vector2Int(cells.xMax - 1, cells.yMax - 1));
        int xFrom = Mathf.Max(min.x, 0);
        int yFrom = Mathf.Max(min.y, 0);
        int xTo = Mathf.Min(max.x, sizeInChunks.x - 1);
        int yTo = Mathf.Min(max.y, sizeInChunks.y - 1);
        for (int y = yFrom; y <= yTo; y++)
        {
            for (int x = xFrom; x <= xTo; x++)
            {
                chunks.Add(new Vector2Int(x, y));
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="cells"/> overlaps the chunk at <paramref name="chunk"/>.
    /// </summary>
    public static bool Overlaps(RectInt cells, Vector2Int chunk)
    {
        return cells.Overlaps(Bounds(chunk));
    }

    /// <summary>
    /// The squared distance in cells from <paramref name="cell"/> to the nearest cell of the chunk at
    /// <paramref name="chunk"/>; 0 inside it.
    /// </summary>
    public static int SquaredDistance(Vector2Int cell, Vector2Int chunk)
    {
        RectInt bounds = Bounds(chunk);
        int dx = cell.x < bounds.xMin ? bounds.xMin - cell.x : cell.x >= bounds.xMax ? cell.x - bounds.xMax + 1 : 0;
        int dy = cell.y < bounds.yMin ? bounds.yMin - cell.y : cell.y >= bounds.yMax ? cell.y - bounds.yMax + 1 : 0;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// <paramref name="value"/> divided by <paramref name="divisor"/>, rounded down rather than towards zero.
    /// </summary>
    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
