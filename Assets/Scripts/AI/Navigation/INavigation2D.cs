using UnityEngine;

/// <summary>
/// The cells a walker paths on: an area's <see cref="NavigationGrid2D"/>, a single page, or a world's
/// <see cref="WorldNavigation"/>, the pages of its loaded chunks joined along their borders. Paths, mobs and players
/// use only this, so they work the same in both.
/// </summary>
public interface INavigation2D
{
    /// <summary>
    /// Whether there is anything to path on: a built grid, or a world with a loaded page.
    /// </summary>
    bool IsBuilt { get; }

    /// <summary>
    /// Changes whenever cells change (a build, a tile edit, a page loading or unloading), so path followers check their
    /// path again.
    /// </summary>
    int Version { get; }

    IPathfinder2D Pathfinder { get; }

    /// <summary>
    /// The cell holding <paramref name="worldPosition"/>.
    /// </summary>
    Vector3Int WorldToCell(Vector3 worldPosition);

    /// <summary>
    /// The world-space centre of <paramref name="cell"/>.
    /// </summary>
    Vector3 CellToWorldCenter(Vector3Int cell);

    /// <summary>
    /// Whether <paramref name="movementProfile"/> (null = every terrain walkable) can stand on <paramref name="cell"/>.
    /// </summary>
    bool IsCellWalkable(Vector3Int cell, TerrainMovementProfile2D movementProfile);

    /// <summary>
    /// The walkable cell nearest <paramref name="origin"/>, searching rings of growing Manhattan distance up to
    /// <paramref name="maxRadius"/> (the navigation's own radius when not positive).
    /// </summary>
    bool TryGetNearestWalkableCell(Vector3Int origin, TerrainMovementProfile2D movementProfile, out Vector3Int nearest, int maxRadius = -1);

    /// <summary>
    /// The cost of one step from <paramref name="from"/> into the adjacent <paramref name="to"/>, prohibitive when blocked.
    /// </summary>
    int MovementCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile);

    /// <summary>
    /// The octile distance between two cells at the cheapest cost the profile allows.
    /// </summary>
    int HeuristicCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile);

    /// <summary>
    /// Whether both cells are walkable and joined by a path, in constant time.
    /// </summary>
    bool AreCellsConnected(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile);

    /// <summary>
    /// The summed step costs of the Bresenham line between two cells; false when any step of it is blocked.
    /// </summary>
    bool TryGetLineCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile, out int cost);

    /// <summary>
    /// Whether the Bresenham line between two cells crosses the cliff of raised ground.
    /// </summary>
    bool CrossesCliff(Vector3Int from, Vector3Int to);

    /// <summary>
    /// <paramref name="point"/> moved off the cliff face to its top; unchanged elsewhere.
    /// </summary>
    Vector2 MoveOffCliffFace(Vector2 point);

    /// <summary>
    /// Bakes the costs and regions of <paramref name="movementProfile"/> now, so the first path does not pay for them.
    /// </summary>
    void PrewarmRegions(TerrainMovementProfile2D movementProfile);
}
