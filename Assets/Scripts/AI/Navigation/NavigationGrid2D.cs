using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The walkable cells of an area, read from its terrain sources minus its blocking tilemaps. Each movement profile
/// gets a cost field: flat arrays over <see cref="WalkableBounds"/> holding every cell's traversal cost and connected
/// region, baked on first use and rebaked when the profile is edited or the grid changes. While enabled it follows
/// tile changes on its source and blocking tilemaps, updating just those cells and bumping <see cref="Version"/>.
/// </summary>
public class NavigationGrid2D : MonoBehaviour
{
    private const int DefaultTraversalCost = 10;
    private const int BlockedCost = int.MaxValue / 4;

    private sealed class CellData
    {
        public readonly List<TerrainType2D> TerrainTypes = new();
        public int Priority = int.MinValue;
    }

    /// <summary>
    /// One profile's view of the grid, indexed like <see cref="TryGetIndex"/>: traversal cost (0 = blocked), the cost
    /// of entering the cell diagonally, and the 4-connected region (-1 = blocked).
    /// </summary>
    internal sealed class CostField
    {
        public CostField(int cellCount)
        {
            Costs = new int[cellCount];
            DiagonalCosts = new int[cellCount];
            Regions = new int[cellCount];
        }

        public readonly int[] Costs;
        public readonly int[] DiagonalCosts;
        public readonly int[] Regions;
        public int MinimumCost;
        public int MinimumDiagonalCost;
        public int ProfileVersion;
        public int GridVersion;
        public bool IsBaked;
    }

    [SerializeField] private List<NavigationTerrainSource2D> terrainSources = new();
    [SerializeField] private bool discoverSourcesInChildren = true;
    [Tooltip("Tilemaps whose cells are blocked for every terrain layer, such as the collision tilemap.")]
    [SerializeField] private List<Tilemap> blockingTilemaps = new();
    [SerializeField] private int nearestCellSearchRadius = 8;
    [Tooltip("Upper bound on cells a partial A* search toward an unreachable goal may expand before returning " +
        "the closest cell found so far. Searches toward reachable goals are never capped.")]
    [SerializeField, Min(1)] private int maxSearchCells = 4096;
    [Header("Debug")]
    [SerializeField] private bool drawGridBoundsGizmo = true;
    [SerializeField] private bool drawWalkableCellGizmos = false;
    [SerializeField, Min(1)] private int maxWalkableCellsToDraw = 400;
    [SerializeField] private Color gridBoundsColor = new(0.35f, 1f, 1f, 0.8f);
    [SerializeField] private Color walkableCellColor = new(0.25f, 1f, 0.3f, 0.35f);

    private readonly Dictionary<Vector3Int, CellData> cellsByPosition = new();
    private readonly List<NavigationTerrainSource2D> resolvedSources = new();
    private readonly List<NavigationTerrainSource2D> terrainLayers = new();
    private readonly List<Tilemap> watchedTilemaps = new();
    private readonly Dictionary<TerrainMovementProfile2D, CostField> fieldsByProfile = new();
    private CellData[] cells = Array.Empty<CellData>();
    private int[] regionQueue = Array.Empty<int>();
    private CostField defaultField;
    private GridAStarPathfinder2D pathfinder;
    private bool gizmoGridIsStale;
    private BoundsInt walkableBounds;
    private int width;
    private int height;
    private Tilemap referenceTilemap;

    public IPathfinder2D Pathfinder => pathfinder;
    /// <summary>
    /// Changes whenever cells change: a full <see cref="BuildGrid"/>, or tiles changing on a source or blocking
    /// tilemap. Path followers compare it to the version their path was built on.
    /// </summary>
    public int Version { get; private set; }
    public BoundsInt WalkableBounds => walkableBounds;
    public bool IsBuilt => pathfinder != null;

    public int MaxSearchCells
    {
        get => maxSearchCells;
        set => maxSearchCells = Mathf.Max(1, value);
    }
    public IReadOnlyList<NavigationTerrainSource2D> TerrainSources => terrainSources;
    public IReadOnlyList<Tilemap> BlockingTilemaps => blockingTilemaps;

    internal int CellCount => cells.Length;
    internal int Width => width;
    internal int Height => height;

    private void Awake()
    {
        BuildGrid();
    }

    private void OnEnable()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
    }

    private void OnDisable()
    {
        Tilemap.tilemapPositionsChanged -= HandleTilemapPositionsChanged;
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

    public void ConfigureBlockingTilemaps(params Tilemap[] tilemaps)
    {
        blockingTilemaps.Clear();
        if (tilemaps == null)
        {
            return;
        }

        for (int i = 0; i < tilemaps.Length; i++)
        {
            if (tilemaps[i] != null)
            {
                blockingTilemaps.Add(tilemaps[i]);
            }
        }
    }

    public void BuildGrid()
    {
        cellsByPosition.Clear();
        resolvedSources.Clear();
        terrainLayers.Clear();
        watchedTilemaps.Clear();
        fieldsByProfile.Clear();
        defaultField = null;
        cells = Array.Empty<CellData>();
        pathfinder = null;
        walkableBounds = default;
        width = 0;
        height = 0;
        referenceTilemap = null;
        Version++;

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

            if (source.IsDualGridRenderTilemap)
            {
                Debug.LogError($"NavigationGrid2D skipped terrain source '{source.name}' because it is on a dual-grid render tilemap, " +
                    "which sits half a cell off the data cells. Move it to the data tilemap that has the DualGridTilemapModule.", source);
                continue;
            }

            if (referenceTilemap == null)
            {
                referenceTilemap = dataTilemap;
            }

            terrainLayers.Add(source);
            watchedTilemaps.Add(dataTilemap);
            foreach (Vector3Int cell in dataTilemap.cellBounds.allPositionsWithin)
            {
                if (!dataTilemap.HasTile(cell))
                {
                    continue;
                }

                if (!cellsByPosition.TryGetValue(cell, out CellData cellData))
                {
                    cellData = new CellData();
                    cellsByPosition[cell] = cellData;
                }

                AddTerrain(cellData, source);
            }
        }

        if (referenceTilemap == null)
        {
            Debug.LogError("NavigationGrid2D could not build because none of its terrain sources were fully configured.", this);
            return;
        }

        for (int i = 0; i < blockingTilemaps.Count; i++)
        {
            Tilemap blockingTilemap = blockingTilemaps[i];
            if (blockingTilemap == null)
            {
                continue;
            }

            if (DualGridUtils.IsRenderTilemap(blockingTilemap.transform))
            {
                Debug.LogError($"NavigationGrid2D ignores blocking tilemap '{blockingTilemap.name}' because it is a dual-grid render tilemap, " +
                    "which sits half a cell off the data cells. List the data tilemap that has the DualGridTilemapModule instead.", blockingTilemap);
                continue;
            }

            watchedTilemaps.Add(blockingTilemap);
        }

        RemoveBlockedCells();
        walkableBounds = CalculateBounds();
        BakeCells();
        cellsByPosition.Clear();
        pathfinder = new GridAStarPathfinder2D(this);
    }

    /// <summary>
    /// Re-reads <paramref name="changedCells"/> from the source and blocking tilemaps after their tiles changed, and
    /// bumps <see cref="Version"/>. A cell outside <see cref="WalkableBounds"/> that gained terrain rebuilds the grid.
    /// Tile changes on those tilemaps already arrive here on their own while the grid is enabled.
    /// </summary>
    public void RefreshCells(IReadOnlyList<Vector3Int> changedCells)
    {
        if (!IsBuilt)
        {
            return;
        }

        for (int i = 0; i < changedCells.Count; i++)
        {
            if (!RefreshCell(changedCells[i]))
            {
                BuildGrid();
                return;
            }
        }

        Version++;
    }

    public bool IsCellWalkable(Vector3Int cell)
    {
        return IsCellWalkable(cell, null);
    }

    public bool IsCellWalkable(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        return TryGetIndex(cell, out int index) && GetCostField(movementProfile).Costs[index] > 0;
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

    public bool TryGetNearestWalkableCell(Vector3Int origin, out Vector3Int nearest, int maxRadius = -1)
    {
        return TryGetNearestWalkableCell(origin, null, out nearest, maxRadius);
    }

    public bool TryGetNearestWalkableCell(Vector3Int origin, TerrainMovementProfile2D movementProfile, out Vector3Int nearest, int maxRadius = -1)
    {
        CostField field = GetCostField(movementProfile);
        if (IsWalkable(field, origin))
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
                if (IsWalkable(field, top))
                {
                    nearest = top;
                    return true;
                }

                Vector3Int bottom = new(origin.x + dx, origin.y - dy, origin.z);
                if (IsWalkable(field, bottom))
                {
                    nearest = bottom;
                    return true;
                }
            }
        }

        nearest = origin;
        return false;
    }

    public int MovementCost(Vector3Int from, Vector3Int to)
    {
        return MovementCost(from, to, null);
    }

    public int MovementCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        return MovementCost(GetCostField(movementProfile), from, to);
    }

    public int HeuristicCost(Vector3Int from, Vector3Int to)
    {
        return HeuristicCost(from, to, null);
    }

    public int HeuristicCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        int baseTraversalCost = movementProfile != null ? movementProfile.GetMinimumTraversalCost() : DefaultTraversalCost;
        return OctileCost(Mathf.Abs(from.x - to.x), Mathf.Abs(from.y - to.y), baseTraversalCost, ScaleDiagonalCost(baseTraversalCost));
    }

    public bool AreCellsConnected(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        if (!IsBuilt || !TryGetIndex(from, out int fromIndex) || !TryGetIndex(to, out int toIndex))
        {
            return false;
        }

        int[] regions = GetCostField(movementProfile).Regions;
        return regions[fromIndex] >= 0 && regions[fromIndex] == regions[toIndex];
    }

    public void PrewarmRegions(TerrainMovementProfile2D movementProfile)
    {
        if (IsBuilt)
        {
            GetCostField(movementProfile);
        }
    }

    internal bool AreRegionsLabeled(TerrainMovementProfile2D movementProfile)
    {
        CostField field = defaultField;
        if (movementProfile != null)
        {
            fieldsByProfile.TryGetValue(movementProfile, out field);
        }

        int profileVersion = movementProfile != null ? movementProfile.Version : 0;
        return field != null && field.IsBaked && field.ProfileVersion == profileVersion && field.GridVersion == Version;
    }

    public bool TryGetLineCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile, out int cost)
    {
        cost = 0;
        CostField field = GetCostField(movementProfile);
        if (!IsWalkable(field, from) || !IsWalkable(field, to))
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
            if (!CanTraverse(field, previous, current))
            {
                return false;
            }

            cost += MovementCost(field, previous, current);
            previous = current;
        }

        return true;
    }

    /// <summary>
    /// The flat index of <paramref name="cell"/> in this grid's arrays, or false when it lies outside
    /// <see cref="WalkableBounds"/>.
    /// </summary>
    internal bool TryGetIndex(Vector3Int cell, out int index)
    {
        int x = cell.x - walkableBounds.xMin;
        int y = cell.y - walkableBounds.yMin;
        int z = cell.z - walkableBounds.zMin;
        if (x < 0 || y < 0 || z < 0 || x >= width || y >= height || z >= walkableBounds.size.z)
        {
            index = -1;
            return false;
        }

        index = ((z * height) + y) * width + x;
        return true;
    }

    internal Vector3Int GetCell(int index)
    {
        int x = index % width;
        int rest = index / width;
        return new Vector3Int(walkableBounds.xMin + x, walkableBounds.yMin + (rest % height), walkableBounds.zMin + (rest / height));
    }

    /// <summary>
    /// The cost field for <paramref name="movementProfile"/> (null = every terrain walkable at the default cost),
    /// baked on first use and rebaked when the profile's <see cref="TerrainMovementProfile2D.Version"/> changes.
    /// </summary>
    internal CostField GetCostField(TerrainMovementProfile2D movementProfile)
    {
        CostField field;
        if (movementProfile == null)
        {
            defaultField ??= new CostField(cells.Length);
            field = defaultField;
        }
        else if (!fieldsByProfile.TryGetValue(movementProfile, out field))
        {
            field = new CostField(cells.Length);
            fieldsByProfile[movementProfile] = field;
        }

        int profileVersion = movementProfile != null ? movementProfile.Version : 0;
        if (!field.IsBaked || field.ProfileVersion != profileVersion || field.GridVersion != Version)
        {
            BakeCostField(field, movementProfile);
            field.IsBaked = true;
            field.ProfileVersion = profileVersion;
            field.GridVersion = Version;
        }

        return field;
    }

    internal static int OctileCost(int dx, int dy, int straightCost, int diagonalCost)
    {
        int diagonal = Mathf.Min(dx, dy);
        int straight = Mathf.Max(dx, dy) - diagonal;
        return (diagonalCost * diagonal) + (straightCost * straight);
    }

    private bool IsWalkable(CostField field, Vector3Int cell)
    {
        return TryGetIndex(cell, out int index) && field.Costs[index] > 0;
    }

    private bool CanTraverse(CostField field, Vector3Int from, Vector3Int to)
    {
        if (!IsWalkable(field, to))
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
            return IsWalkable(field, new Vector3Int(from.x + dx, from.y, from.z))
                && IsWalkable(field, new Vector3Int(from.x, from.y + dy, from.z));
        }

        return true;
    }

    private int MovementCost(CostField field, Vector3Int from, Vector3Int to)
    {
        if (!TryGetIndex(to, out int index) || field.Costs[index] == 0)
        {
            return BlockedCost;
        }

        bool isDiagonal = Mathf.Abs(to.x - from.x) == 1 && Mathf.Abs(to.y - from.y) == 1;
        return isDiagonal ? field.DiagonalCosts[index] : field.Costs[index];
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

    /// <summary>
    /// Re-reads the changed cells when <paramref name="tilemap"/> is one of this grid's source or blocking tilemaps.
    /// Called by the grid itself while enabled, and by an editor listener outside Play Mode so the gizmos stay current.
    /// </summary>
    internal void HandleTilemapPositionsChanged(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        if (!IsBuilt || !watchedTilemaps.Contains(tilemap))
        {
            return;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            if (!RefreshCell(positions[i]))
            {
                BuildGrid();
                return;
            }
        }

        Version++;
    }

    /// <summary>
    /// Re-reads one cell into the baked array. False when the cell lies outside the baked bounds and now has terrain,
    /// which needs a full rebuild.
    /// </summary>
    private bool RefreshCell(Vector3Int cell)
    {
        bool isInBounds = TryGetIndex(cell, out int index);
        CellData cellData = ReadCell(cell, isInBounds ? cells[index] : null);
        if (!isInBounds)
        {
            return cellData == null;
        }

        cells[index] = cellData;
        return true;
    }

    private CellData ReadCell(Vector3Int cell, CellData reuse)
    {
        if (IsBlocked(cell))
        {
            return null;
        }

        CellData cellData = reuse;
        if (cellData != null)
        {
            cellData.TerrainTypes.Clear();
            cellData.Priority = int.MinValue;
        }

        for (int i = 0; i < terrainLayers.Count; i++)
        {
            NavigationTerrainSource2D source = terrainLayers[i];
            if (source == null || !source.DataTilemap.HasTile(cell))
            {
                continue;
            }

            cellData ??= new CellData();
            AddTerrain(cellData, source);
        }

        return cellData != null && cellData.TerrainTypes.Count > 0 ? cellData : null;
    }

    private bool IsBlocked(Vector3Int cell)
    {
        for (int i = 0; i < blockingTilemaps.Count; i++)
        {
            if (blockingTilemaps[i] != null && watchedTilemaps.Contains(blockingTilemaps[i]) && blockingTilemaps[i].HasTile(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds the terrain of <paramref name="source"/> to a cell: a higher priority replaces what is there, an equal one
    /// joins it, a lower one is hidden under it.
    /// </summary>
    private static void AddTerrain(CellData cellData, NavigationTerrainSource2D source)
    {
        if (source.Priority > cellData.Priority)
        {
            cellData.TerrainTypes.Clear();
            cellData.Priority = source.Priority;
        }

        if (source.Priority == cellData.Priority)
        {
            cellData.TerrainTypes.Add(source.TerrainType);
        }
    }

    private void RemoveBlockedCells()
    {
        for (int i = 0; i < blockingTilemaps.Count; i++)
        {
            Tilemap blockingTilemap = blockingTilemaps[i];
            if (blockingTilemap == null || !watchedTilemaps.Contains(blockingTilemap))
            {
                continue;
            }

            foreach (Vector3Int cell in blockingTilemap.cellBounds.allPositionsWithin)
            {
                if (blockingTilemap.HasTile(cell))
                {
                    cellsByPosition.Remove(cell);
                }
            }
        }
    }

    private void BakeCells()
    {
        width = walkableBounds.size.x;
        height = walkableBounds.size.y;
        cells = new CellData[width * height * walkableBounds.size.z];
        foreach (KeyValuePair<Vector3Int, CellData> entry in cellsByPosition)
        {
            if (TryGetIndex(entry.Key, out int index))
            {
                cells[index] = entry.Value;
            }
        }
    }

    private void BakeCostField(CostField field, TerrainMovementProfile2D movementProfile)
    {
        int[] costs = field.Costs;
        for (int i = 0; i < cells.Length; i++)
        {
            costs[i] = ResolveTraversalCost(cells[i], movementProfile);
        }

        int edgeCost = movementProfile != null ? movementProfile.EdgeCost : 0;
        if (edgeCost > 0)
        {
            AddEdgeCost(costs, edgeCost);
        }

        for (int i = 0; i < costs.Length; i++)
        {
            field.DiagonalCosts[i] = costs[i] > 0 ? ScaleDiagonalCost(costs[i]) : 0;
        }

        field.MinimumCost = movementProfile != null ? movementProfile.GetMinimumTraversalCost() : DefaultTraversalCost;
        field.MinimumDiagonalCost = ScaleDiagonalCost(field.MinimumCost);
        LabelRegions(field);
    }

    /// <summary>
    /// Adds <paramref name="edgeCost"/> to every walkable cell with a blocked or missing cell among its 8 neighbours.
    /// Costs only grow, so a cell's walkability does not change while the pass runs.
    /// </summary>
    private void AddEdgeCost(int[] costs, int edgeCost)
    {
        for (int index = 0; index < costs.Length; index++)
        {
            if (costs[index] > 0 && TouchesBlockedCell(costs, index))
            {
                costs[index] += edgeCost;
            }
        }
    }

    private bool TouchesBlockedCell(int[] costs, int index)
    {
        int x = index % width;
        int y = (index / width) % height;
        int layerStart = index - ((y * width) + x);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int neighborX = x + dx;
                int neighborY = y + dy;
                if (neighborX < 0 || neighborY < 0 || neighborX >= width || neighborY >= height)
                {
                    return true;
                }

                if (costs[layerStart + (neighborY * width) + neighborX] == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int ResolveTraversalCost(CellData cellData, TerrainMovementProfile2D movementProfile)
    {
        if (cellData == null || cellData.TerrainTypes.Count == 0)
        {
            return 0;
        }

        if (movementProfile == null)
        {
            return DefaultTraversalCost;
        }

        int highestCost = 0;
        for (int i = 0; i < cellData.TerrainTypes.Count; i++)
        {
            movementProfile.TryGetTraversal(cellData.TerrainTypes[i], out bool isWalkable, out int terrainCost);
            if (!isWalkable)
            {
                return 0;
            }

            highestCost = Mathf.Max(highestCost, terrainCost);
        }

        return highestCost > 0 ? highestCost : movementProfile.DefaultTraversalCost;
    }

    private void LabelRegions(CostField field)
    {
        int[] costs = field.Costs;
        int[] regions = field.Regions;
        Array.Fill(regions, -1);
        if (regionQueue.Length < costs.Length)
        {
            regionQueue = new int[costs.Length];
        }

        int nextRegion = 0;
        for (int seed = 0; seed < costs.Length; seed++)
        {
            if (costs[seed] == 0 || regions[seed] >= 0)
            {
                continue;
            }

            int head = 0;
            int tail = 0;
            regions[seed] = nextRegion;
            regionQueue[tail++] = seed;

            while (head < tail)
            {
                int current = regionQueue[head++];
                int x = current % width;
                int y = (current / width) % height;
                if (x > 0)
                {
                    tail = EnqueueRegionCell(current - 1, nextRegion, costs, regions, tail);
                }

                if (x < width - 1)
                {
                    tail = EnqueueRegionCell(current + 1, nextRegion, costs, regions, tail);
                }

                if (y > 0)
                {
                    tail = EnqueueRegionCell(current - width, nextRegion, costs, regions, tail);
                }

                if (y < height - 1)
                {
                    tail = EnqueueRegionCell(current + width, nextRegion, costs, regions, tail);
                }
            }

            nextRegion++;
        }
    }

    private int EnqueueRegionCell(int index, int region, int[] costs, int[] regions, int tail)
    {
        if (costs[index] == 0 || regions[index] >= 0)
        {
            return tail;
        }

        regions[index] = region;
        regionQueue[tail] = index;
        return tail + 1;
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

    /// <summary>
    /// Rebuilds the grid before its next gizmo draw outside Play Mode, after an inspector change to it or to one of its
    /// terrain sources. Tile painting is followed cell by cell instead.
    /// </summary>
    internal void MarkGizmoGridStale()
    {
        gizmoGridIsStale = true;
    }

    private void OnValidate()
    {
        gizmoGridIsStale = true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying && (drawGridBoundsGizmo || drawWalkableCellGizmos) && (!IsBuilt || gizmoGridIsStale))
        {
            gizmoGridIsStale = false;
            BuildGrid();
        }

        if (drawGridBoundsGizmo)
        {
            DrawGridBoundsGizmo();
        }

        if (!drawWalkableCellGizmos)
        {
            return;
        }

        Gizmos.color = walkableCellColor;
        int drawn = 0;
        for (int i = 0; i < cells.Length && drawn < maxWalkableCellsToDraw; i++)
        {
            if (cells[i] == null)
            {
                continue;
            }

            Gizmos.DrawCube(CellToWorldCenter(GetCell(i)), Vector3.one * 0.12f);
            drawn++;
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
