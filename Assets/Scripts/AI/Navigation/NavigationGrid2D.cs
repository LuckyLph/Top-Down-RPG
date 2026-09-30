using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class NavigationGrid2D : MonoBehaviour
{
    private static readonly Vector3Int[] NeighborOffsets4 =
    {
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0)
    };

    private static readonly Vector3Int[] NeighborOffsets8 =
    {
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0),
        new(1, 1, 0),
        new(1, -1, 0),
        new(-1, 1, 0),
        new(-1, -1, 0)
    };

    private const int DefaultTraversalCost = 10;
    private const int DefaultDiagonalCost = 14;

    private sealed class CellData
    {
        public readonly List<TerrainType2D> TerrainTypes = new();
    }

    private sealed class RegionMap
    {
        public readonly Dictionary<Vector3Int, int> RegionByCell = new();
        public bool IsLabeled;
        public int ProfileVersion;
    }

    [SerializeField] private List<NavigationTerrainSource2D> terrainSources = new();
    [SerializeField] private bool discoverSourcesInChildren = true;
    [SerializeField] private int nearestCellSearchRadius = 8;
    [Header("Debug")]
    [SerializeField] private bool drawGridBoundsGizmo = true;
    [SerializeField] private bool drawWalkableCellGizmos = false;
    [SerializeField, Min(1)] private int maxWalkableCellsToDraw = 400;
    [SerializeField] private Color gridBoundsColor = new(0.35f, 1f, 1f, 0.8f);
    [SerializeField] private Color walkableCellColor = new(0.25f, 1f, 0.3f, 0.35f);

    private readonly Dictionary<Vector3Int, CellData> cellsByPosition = new();
    private readonly List<NavigationTerrainSource2D> resolvedSources = new();
    private readonly Dictionary<TerrainMovementProfile2D, RegionMap> regionsByProfile = new();
    private RegionMap defaultRegions;
    private GridAStarPathfinder2D pathfinder;
    private BoundsInt walkableBounds;
    private Tilemap referenceTilemap;

    public IPathfinder2D Pathfinder => pathfinder;
    public BoundsInt WalkableBounds => walkableBounds;
    public bool IsBuilt => pathfinder != null;
    public IReadOnlyList<NavigationTerrainSource2D> TerrainSources => terrainSources;

    private void Awake()
    {
        BuildGrid();
    }

    public void ConfigureSources(params NavigationTerrainSource2D[] sources)
    {
        terrainSources.Clear();
        if (sources == null)
        {
            return;
        }

        for (int i = 0; i < sources.Length; i++)
        {
            NavigationTerrainSource2D source = sources[i];
            if (source != null)
            {
                terrainSources.Add(source);
            }
        }
    }

    public void BuildGrid()
    {
        cellsByPosition.Clear();
        resolvedSources.Clear();
        regionsByProfile.Clear();
        defaultRegions = null;
        pathfinder = null;
        walkableBounds = default;
        referenceTilemap = null;

        ResolveTerrainSources();
        if (resolvedSources.Count == 0)
        {
            Debug.LogError("NavigationGrid2D could not find any NavigationTerrainSource2D components.", this);
            return;
        }

        for (int i = 0; i < resolvedSources.Count; i++)
        {
            NavigationTerrainSource2D source = resolvedSources[i];
            if (source == null)
            {
                continue;
            }

            Tilemap dataTilemap = source.DataTilemap;
            if (dataTilemap == null)
            {
                Debug.LogWarning($"NavigationGrid2D skipped terrain source '{source.name}' because it has no data tilemap.", source);
                continue;
            }

            if (source.TerrainType == null)
            {
                Debug.LogWarning($"NavigationGrid2D skipped terrain source '{source.name}' because it has no terrain type.", source);
                continue;
            }

            referenceTilemap ??= dataTilemap;
            Tilemap collisionTilemap = source.CollisionTilemap;
            BoundsInt bounds = dataTilemap.cellBounds;

            foreach (Vector3Int cell in bounds.allPositionsWithin)
            {
                if (!dataTilemap.HasTile(cell))
                {
                    continue;
                }

                if (collisionTilemap != null && collisionTilemap.HasTile(cell))
                {
                    continue;
                }

                if (!cellsByPosition.TryGetValue(cell, out CellData cellData))
                {
                    cellData = new CellData();
                    cellsByPosition[cell] = cellData;
                }

                cellData.TerrainTypes.Add(source.TerrainType);
            }
        }

        if (referenceTilemap == null)
        {
            Debug.LogError("NavigationGrid2D could not build because none of its terrain sources were fully configured.", this);
            return;
        }

        walkableBounds = CalculateBounds();
        pathfinder = new GridAStarPathfinder2D(this);
    }

    public bool IsCellWalkable(Vector3Int cell)
    {
        return IsCellWalkable(cell, null);
    }

    public bool IsCellWalkable(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        return TryResolveCellTraversal(cell, movementProfile, out _);
    }

    public Vector3Int WorldToCell(Vector3 worldPosition)
    {
        Tilemap tilemap = GetReferenceTilemap();
        return tilemap != null ? tilemap.WorldToCell(worldPosition) : Vector3Int.zero;
    }

    public Vector3 CellToWorldCenter(Vector3Int cell)
    {
        Tilemap tilemap = GetReferenceTilemap();
        return tilemap != null ? tilemap.GetCellCenterWorld(cell) : Vector3.zero;
    }

    public IEnumerable<Vector3Int> GetNeighbors4(Vector3Int cell)
    {
        return GetNeighbors4(cell, null);
    }

    public IEnumerable<Vector3Int> GetNeighbors4(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        for (int i = 0; i < NeighborOffsets4.Length; i++)
        {
            Vector3Int neighbor = cell + NeighborOffsets4[i];
            if (CanTraverse(cell, neighbor, movementProfile))
            {
                yield return neighbor;
            }
        }
    }

    public IEnumerable<Vector3Int> GetNeighbors8(Vector3Int cell)
    {
        return GetNeighbors8(cell, null);
    }

    public IEnumerable<Vector3Int> GetNeighbors8(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        for (int i = 0; i < NeighborOffsets8.Length; i++)
        {
            Vector3Int neighbor = cell + NeighborOffsets8[i];
            if (CanTraverse(cell, neighbor, movementProfile))
            {
                yield return neighbor;
            }
        }
    }

    public bool TryGetNearestWalkableCell(Vector3Int origin, out Vector3Int nearest, int maxRadius = -1)
    {
        return TryGetNearestWalkableCell(origin, null, out nearest, maxRadius);
    }

    public bool TryGetNearestWalkableCell(Vector3Int origin, TerrainMovementProfile2D movementProfile, out Vector3Int nearest, int maxRadius = -1)
    {
        if (IsCellWalkable(origin, movementProfile))
        {
            nearest = origin;
            return true;
        }

        int radiusLimit = maxRadius > 0 ? maxRadius : Mathf.Max(1, nearestCellSearchRadius);
        for (int radius = 1; radius <= radiusLimit; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int dy = radius - Mathf.Abs(dx);

                Vector3Int top = new(origin.x + dx, origin.y + dy, origin.z);
                if (IsCellWalkable(top, movementProfile))
                {
                    nearest = top;
                    return true;
                }

                Vector3Int bottom = new(origin.x + dx, origin.y - dy, origin.z);
                if (IsCellWalkable(bottom, movementProfile))
                {
                    nearest = bottom;
                    return true;
                }
            }
        }

        nearest = origin;
        return false;
    }

    public bool CanTraverse(Vector3Int from, Vector3Int to)
    {
        return CanTraverse(from, to, null);
    }

    public bool CanTraverse(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        if (!TryResolveCellTraversal(to, movementProfile, out _))
        {
            return false;
        }

        int dx = to.x - from.x;
        int dy = to.y - from.y;
        int absX = Mathf.Abs(dx);
        int absY = Mathf.Abs(dy);

        if ((absX == 0 && absY == 0) || absX > 1 || absY > 1)
        {
            return false;
        }

        if (absX == 1 && absY == 1)
        {
            // Prevent corner cutting. Both orthogonal cells must be traversable.
            Vector3Int orthogonalA = new(from.x + dx, from.y, from.z);
            Vector3Int orthogonalB = new(from.x, from.y + dy, from.z);
            return IsCellWalkable(orthogonalA, movementProfile) && IsCellWalkable(orthogonalB, movementProfile);
        }

        return true;
    }

    public int MovementCost(Vector3Int from, Vector3Int to)
    {
        return MovementCost(from, to, null);
    }

    public int MovementCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        if (!TryResolveCellTraversal(to, movementProfile, out int traversalCost))
        {
            return int.MaxValue / 4;
        }

        int dx = Mathf.Abs(to.x - from.x);
        int dy = Mathf.Abs(to.y - from.y);
        return (dx == 1 && dy == 1) ? ScaleDiagonalCost(traversalCost) : traversalCost;
    }

    public int HeuristicCost(Vector3Int from, Vector3Int to)
    {
        return HeuristicCost(from, to, null);
    }

    public int HeuristicCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        int baseTraversalCost = movementProfile != null ? movementProfile.GetMinimumTraversalCost() : DefaultTraversalCost;
        int diagonalCost = ScaleDiagonalCost(baseTraversalCost);
        int dx = Mathf.Abs(from.x - to.x);
        int dy = Mathf.Abs(from.y - to.y);
        int diagonal = Mathf.Min(dx, dy);
        int straight = Mathf.Max(dx, dy) - diagonal;
        return (diagonalCost * diagonal) + (baseTraversalCost * straight);
    }

    public bool AreCellsConnected(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        if (!IsBuilt)
        {
            return false;
        }

        RegionMap regions = GetRegionMap(movementProfile);
        return regions.RegionByCell.TryGetValue(from, out int fromRegion)
            && regions.RegionByCell.TryGetValue(to, out int toRegion)
            && fromRegion == toRegion;
    }

    public bool HasLineOfSightCells(Vector3Int from, Vector3Int to)
    {
        return HasLineOfSightCells(from, to, null);
    }

    public bool HasLineOfSightCells(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        return TryGetLineCost(from, to, movementProfile, out _);
    }

    // Walks the straight cell line between two cells, summing movement costs. Fails if any step is not traversable.
    public bool TryGetLineCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile, out int cost)
    {
        cost = 0;
        if (!IsCellWalkable(from, movementProfile) || !IsCellWalkable(to, movementProfile))
        {
            return false;
        }

        int x0 = from.x;
        int y0 = from.y;
        int x1 = to.x;
        int y1 = to.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        Vector3Int previous = new(x0, y0, from.z);
        while (x0 != x1 || y0 != y1)
        {
            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }

            Vector3Int current = new(x0, y0, from.z);
            if (!CanTraverse(previous, current, movementProfile))
            {
                return false;
            }

            cost += MovementCost(previous, current, movementProfile);
            previous = current;
        }

        return true;
    }

    private void ResolveTerrainSources()
    {
        HashSet<NavigationTerrainSource2D> uniqueSources = new();
        for (int i = 0; i < terrainSources.Count; i++)
        {
            NavigationTerrainSource2D source = terrainSources[i];
            if (source != null && uniqueSources.Add(source))
            {
                resolvedSources.Add(source);
            }
        }

        if (!discoverSourcesInChildren)
        {
            return;
        }

        NavigationTerrainSource2D[] childSources = GetComponentsInChildren<NavigationTerrainSource2D>(true);
        for (int i = 0; i < childSources.Length; i++)
        {
            NavigationTerrainSource2D source = childSources[i];
            if (source != null && uniqueSources.Add(source))
            {
                resolvedSources.Add(source);
            }
        }
    }

    private bool TryResolveCellTraversal(Vector3Int cell, TerrainMovementProfile2D movementProfile, out int traversalCost)
    {
        traversalCost = DefaultTraversalCost;
        if (!cellsByPosition.TryGetValue(cell, out CellData cellData) || cellData.TerrainTypes.Count == 0)
        {
            return false;
        }

        if (movementProfile == null)
        {
            return true;
        }

        int highestCost = 0;
        for (int i = 0; i < cellData.TerrainTypes.Count; i++)
        {
            TerrainType2D terrainType = cellData.TerrainTypes[i];
            movementProfile.TryGetTraversal(terrainType, out bool isWalkable, out int terrainCost);
            if (!isWalkable)
            {
                traversalCost = terrainCost;
                return false;
            }

            highestCost = Mathf.Max(highestCost, terrainCost);
        }

        traversalCost = highestCost > 0 ? highestCost : movementProfile.DefaultTraversalCost;
        return true;
    }

    private RegionMap GetRegionMap(TerrainMovementProfile2D movementProfile)
    {
        int profileVersion = movementProfile != null ? movementProfile.Version : 0;
        RegionMap regions;
        if (movementProfile == null)
        {
            regions = defaultRegions ??= new RegionMap();
        }
        else if (!regionsByProfile.TryGetValue(movementProfile, out regions))
        {
            regions = new RegionMap();
            regionsByProfile[movementProfile] = regions;
        }

        if (regions.IsLabeled && regions.ProfileVersion == profileVersion)
        {
            return regions;
        }

        LabelRegions(regions, movementProfile);
        regions.IsLabeled = true;
        regions.ProfileVersion = profileVersion;
        return regions;
    }

    // Flood-fills walkable cells into connected regions. Orthogonal adjacency is sufficient because
    // diagonal moves are only allowed when both orthogonal cells are walkable (no corner cutting).
    private void LabelRegions(RegionMap regions, TerrainMovementProfile2D movementProfile)
    {
        regions.RegionByCell.Clear();
        Queue<Vector3Int> frontier = new();
        int nextRegion = 0;

        foreach (Vector3Int seed in cellsByPosition.Keys)
        {
            if (regions.RegionByCell.ContainsKey(seed) || !IsCellWalkable(seed, movementProfile))
            {
                continue;
            }

            regions.RegionByCell[seed] = nextRegion;
            frontier.Enqueue(seed);

            while (frontier.Count > 0)
            {
                Vector3Int current = frontier.Dequeue();
                for (int i = 0; i < NeighborOffsets4.Length; i++)
                {
                    Vector3Int neighbor = current + NeighborOffsets4[i];
                    if (!regions.RegionByCell.ContainsKey(neighbor) && IsCellWalkable(neighbor, movementProfile))
                    {
                        regions.RegionByCell[neighbor] = nextRegion;
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            nextRegion++;
        }
    }

    private BoundsInt CalculateBounds()
    {
        if (cellsByPosition.Count == 0)
        {
            return default;
        }

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int minZ = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        int maxZ = int.MinValue;

        foreach (Vector3Int cell in cellsByPosition.Keys)
        {
            minX = Mathf.Min(minX, cell.x);
            minY = Mathf.Min(minY, cell.y);
            minZ = Mathf.Min(minZ, cell.z);
            maxX = Mathf.Max(maxX, cell.x);
            maxY = Mathf.Max(maxY, cell.y);
            maxZ = Mathf.Max(maxZ, cell.z);
        }

        return new BoundsInt(minX, minY, minZ, maxX - minX + 1, maxY - minY + 1, maxZ - minZ + 1);
    }

    private static int ScaleDiagonalCost(int traversalCost)
    {
        return Mathf.Max(1, Mathf.RoundToInt(traversalCost * 1.4f));
    }

    private Tilemap GetReferenceTilemap()
    {
        if (referenceTilemap != null)
        {
            return referenceTilemap;
        }

        resolvedSources.Clear();
        ResolveTerrainSources();
        for (int i = 0; i < resolvedSources.Count; i++)
        {
            if (resolvedSources[i] != null && resolvedSources[i].DataTilemap != null)
            {
                referenceTilemap = resolvedSources[i].DataTilemap;
                break;
            }
        }

        return referenceTilemap;
    }

    private void OnDrawGizmosSelected()
    {
        if (drawGridBoundsGizmo)
        {
            DrawGridBoundsGizmo();
        }

        if (!drawWalkableCellGizmos)
        {
            return;
        }

        if (!Application.isPlaying && !IsBuilt)
        {
            BuildGrid();
        }

        Gizmos.color = walkableCellColor;
        int drawn = 0;
        foreach (Vector3Int cell in cellsByPosition.Keys)
        {
            Vector3 center = CellToWorldCenter(cell);
            Gizmos.DrawCube(center, Vector3.one * 0.12f);
            drawn++;
            if (drawn >= maxWalkableCellsToDraw)
            {
                break;
            }
        }
    }

    private void DrawGridBoundsGizmo()
    {
        Tilemap tilemap = GetReferenceTilemap();
        if (tilemap == null || walkableBounds.size == Vector3Int.zero)
        {
            return;
        }

        Vector3 min = tilemap.CellToWorld(walkableBounds.min);
        Vector3 max = tilemap.CellToWorld(walkableBounds.max);
        Vector3 center = (min + max) * 0.5f;
        Vector3 size = max - min;
        if (size.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Gizmos.color = gridBoundsColor;
        Gizmos.DrawWireCube(center, size);
    }
}
