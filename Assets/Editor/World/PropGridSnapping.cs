using UnityEngine;

/// <summary>
/// Where a prop prefab's root goes on an area's grid. A blocking prop's footprint (the solid box collider on its root)
/// is centred on the fewest whole cells that hold it and stands on the bottom edge of the lowest of them, so the cells
/// <see cref="NavigationGrid2D"/> blocks are exactly the cells the footprint needs. A walk-through prop only snaps to
/// the pixel grid.
/// </summary>
public static class PropGridSnapping
{
    public const float PixelsPerUnit = 32f;

    /// <summary>
    /// The footprint of <paramref name="prop"/> in its root's local space: the solid box collider on its root. False,
    /// with an empty rect, for a walk-through prop.
    /// </summary>
    public static bool TryGetFootprint(GameObject prop, out Rect footprint)
    {
        if (prop == null || !prop.TryGetComponent(out BoxCollider2D box) || box.isTrigger)
        {
            footprint = default;
            return false;
        }

        footprint = new Rect(box.offset - box.size * 0.5f, box.size);
        return true;
    }

    /// <summary>
    /// The snapped root position for <paramref name="prop"/> placed at, or moved to, <paramref name="basePoint"/>:
    /// <see cref="SnapBlocking"/> for a prop with a footprint, <see cref="SnapToPixels"/> otherwise.
    /// </summary>
    public static Vector3 Snap(GridLayout grid, Vector2 basePoint, GameObject prop)
    {
        return TryGetFootprint(prop, out Rect footprint) ? SnapBlocking(grid, basePoint, footprint) : SnapToPixels(basePoint);
    }

    /// <summary>
    /// The root position that centres <paramref name="footprint"/> on the run of whole cells nearest to where it would
    /// be with the root at <paramref name="basePoint"/>, standing on the bottom edge of the cell its bottom falls in.
    /// Snapping a snapped position returns it unchanged.
    /// </summary>
    public static Vector3 SnapBlocking(GridLayout grid, Vector2 basePoint, Rect footprint)
    {
        Vector2 cell = grid.cellSize;
        Vector2 origin = grid.CellToWorld(Vector3Int.zero);
        int columns = CellsSpanned(footprint.width, cell.x);
        float centreInCells = (basePoint.x + footprint.center.x - origin.x) / cell.x;
        int left = Mathf.RoundToInt(centreInCells - columns * 0.5f);
        int bottom = Mathf.FloorToInt((basePoint.y + footprint.yMin - origin.y + NavigationGrid2D.ObstacleEdgeInset) / cell.y);
        return RootOnCells(grid, new Vector3Int(left, bottom, 0), footprint);
    }

    /// <summary>
    /// The root position that centres <paramref name="footprint"/> on the cells it needs starting at
    /// <paramref name="corner"/>, its bottom-left cell, and stands it on that cell's bottom edge: where
    /// <see cref="SnapBlocking"/> puts a footprint whose cells start there.
    /// </summary>
    public static Vector3 RootOnCells(GridLayout grid, Vector3Int corner, Rect footprint)
    {
        Vector2 cell = grid.cellSize;
        Vector2 origin = grid.CellToWorld(Vector3Int.zero);
        int columns = CellsSpanned(footprint.width, cell.x);
        float x = origin.x + (corner.x + columns * 0.5f) * cell.x - footprint.center.x;
        float y = origin.y + corner.y * cell.y - footprint.yMin;
        return new Vector3(x, y, 0f);
    }

    /// <summary>
    /// How many columns and rows of cells of <paramref name="cellSize"/> a snapped <paramref name="footprint"/> blocks:
    /// the size of <see cref="BlockedCells"/> once it is centred on its cells and stands on a bottom edge.
    /// </summary>
    public static Vector2Int CellsCovered(Rect footprint, Vector2 cellSize)
    {
        int rows = Mathf.FloorToInt((footprint.height - NavigationGrid2D.ObstacleEdgeInset) / cellSize.y) + 1;
        return new Vector2Int(CellsSpanned(footprint.width, cellSize.x), Mathf.Max(1, rows));
    }

    /// <summary>
    /// <paramref name="point"/> rounded to the nearest pixel at <see cref="PixelsPerUnit"/>, at z = 0.
    /// </summary>
    public static Vector3 SnapToPixels(Vector2 point)
    {
        return new Vector3(Mathf.Round(point.x * PixelsPerUnit) / PixelsPerUnit, Mathf.Round(point.y * PixelsPerUnit) / PixelsPerUnit, 0f);
    }

    /// <summary>
    /// How many cells of <paramref name="cellSize"/> a footprint <paramref name="size"/> long needs, at least one,
    /// allowing for the edge inset the navigation grid gives every obstacle.
    /// </summary>
    public static int CellsSpanned(float size, float cellSize)
    {
        return Mathf.Max(1, Mathf.CeilToInt((size - 2f * NavigationGrid2D.ObstacleEdgeInset) / cellSize));
    }

    /// <summary>
    /// The cells <see cref="NavigationGrid2D"/> blocks for <paramref name="footprint"/> with its root at
    /// <paramref name="rootPosition"/>: those its bounds touch once shrunk by the grid's edge inset.
    /// </summary>
    public static BoundsInt BlockedCells(GridLayout grid, Vector2 rootPosition, Rect footprint)
    {
        Vector2 inset = new(
            Mathf.Min(NavigationGrid2D.ObstacleEdgeInset, footprint.width * 0.5f),
            Mathf.Min(NavigationGrid2D.ObstacleEdgeInset, footprint.height * 0.5f));
        Vector3Int min = grid.WorldToCell(rootPosition + footprint.min + inset);
        Vector3Int max = grid.WorldToCell(rootPosition + footprint.max - inset);
        return new BoundsInt(min, new Vector3Int(max.x - min.x + 1, max.y - min.y + 1, 1));
    }
}
