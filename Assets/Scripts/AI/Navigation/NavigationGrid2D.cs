using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The walkable cells of an area, read from its terrain sources minus its blocking tilemaps, the cells under
/// colliders on its obstacle layers (blocking props) and the cells the cliff face of its raised tilemap covers whole,
/// and the cliff itself: a line along the sides between raised and level cells that steps cross only at a ramp's foot
/// (<see cref="RaisedGround.CanCross"/>). Each
/// movement profile gets a cost field: flat arrays over <see cref="WalkableBounds"/> holding every cell's traversal cost
/// and connected region, baked on first use and rebaked when the profile is edited or the grid changes. While enabled
/// it follows tile changes on its source, blocking and raised tilemaps, updating just those cells and bumping
/// <see cref="Version"/>.
/// </summary>
public class NavigationGrid2D : MonoBehaviour
{
    private const int DefaultTraversalCost = 10;
    private const int BlockedCost = int.MaxValue / 4;
    internal const float ObstacleEdgeInset = 0.01f;
    internal const byte LeftSide = 1;
    internal const byte RightSide = 2;
    internal const byte DownSide = 4;
    internal const byte UpSide = 8;

    private static readonly Vector3Int[] SideSteps = { Vector3Int.left, Vector3Int.right, Vector3Int.down, Vector3Int.up };

    /// <summary>
    /// What keeps a movement profile off a cell (<see cref="GetBlocker"/>): nothing, no terrain, terrain the profile
    /// cannot walk, a tile on a blocking tilemap, a collider on the obstacle layers, or the cliff face of raised ground
    /// covering the whole cell (<see cref="RaisedGround.IsOnFace(Tilemap, Vector3Int, int)"/>). The cliff's line
    /// blocks steps across it rather than cells (<see cref="GetCliffSides"/>).
    /// </summary>
    internal enum Blocker
    {
        None,
        NoGround,
        Terrain,
        Wall,
        Obstacle,
        CliffFace,
    }

    /// <summary>
    /// A walkable cell's terrain: the terrain types of its highest-priority terrain layer, and that priority.
    /// </summary>
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
        /// <summary>
        /// Allocates the arrays for <paramref name="cellCount"/> cells, not baked yet.
        /// </summary>
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
    [Tooltip("Optional: the area's raised ground. Its cliff, the line between raised and level cells, is crossed only at a ramp's foot, so units move between raised and level ground only over a ramp; the cells the cliff face the terrain renderer draws covers whole are blocked, and a goal on the face moves up to its top.")]
    [SerializeField] private Tilemap raisedTilemap;
    [Tooltip("Layers whose colliders block every cell they overlap, such as the footprints of blocking props. Read " +
        "when the grid builds; colliders on tilemaps and triggers are ignored.")]
    [SerializeField] private LayerMask obstacleLayers;
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
    private readonly HashSet<Vector3Int> obstacleCells = new();
    private readonly List<Collider2D> obstacleHits = new();
    private readonly Dictionary<TerrainMovementProfile2D, CostField> fieldsByProfile = new();
    private CellData[] cells = Array.Empty<CellData>();
    private byte[] cliffSides = Array.Empty<byte>();
    private float cliffFaceHeight;
    private int cliffFaceRows;
    private int[] regionQueue = Array.Empty<int>();
    private CostField defaultField;
    private GridAStarPathfinder2D pathfinder;
    private bool gizmoGridIsStale;
    private bool gizmoBuildFailed;
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
    public Tilemap RaisedTilemap => raisedTilemap;
    public LayerMask ObstacleLayers => obstacleLayers;

    internal int CellCount => cells.Length;
    internal bool HasCliffSides { get; private set; }
    internal int Width => width;
    internal int Height => height;

    /// <summary>
    /// Builds the grid from its terrain sources and blocking tilemaps.
    /// </summary>
    private void Awake()
    {
        BuildGrid();
    }

    /// <summary>
    /// Starts following tile changes on the source and blocking tilemaps.
    /// </summary>
    private void OnEnable()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
    }

    /// <summary>
    /// Stops following tile changes.
    /// </summary>
    private void OnDisable()
    {
        Tilemap.tilemapPositionsChanged -= HandleTilemapPositionsChanged;
    }

    /// <summary>
    /// Replaces the listed terrain sources with the non-null <paramref name="sources"/>, for grids set up in code.
    /// Takes effect on the next <see cref="BuildGrid"/>.
    /// </summary>
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

    /// <summary>
    /// Replaces the blocking tilemaps with the non-null <paramref name="tilemaps"/>, for grids set up in code. Takes
    /// effect on the next <see cref="BuildGrid"/>.
    /// </summary>
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

    /// <summary>
    /// Sets the raised tilemap whose cliff stops steps (null for none), for grids set up in code. Takes effect on the next
    /// <see cref="BuildGrid"/>.
    /// </summary>
    public void ConfigureRaisedTilemap(Tilemap tilemap)
    {
        raisedTilemap = tilemap;
    }

    /// <summary>
    /// Replaces the obstacle layers, for grids set up in code. Takes effect on the next <see cref="BuildGrid"/>.
    /// </summary>
    public void ConfigureObstacleLayers(LayerMask layers)
    {
        obstacleLayers = layers;
    }

    /// <summary>
    /// Rebuilds the grid from scratch and bumps <see cref="Version"/>: collects the cells of every usable terrain
    /// source (listed and, optionally, found in the children), reads the cliff face's height, removes cells with a tile
    /// on a blocking tilemap, under a collider on the obstacle layers or covered whole by the cliff face, bakes them over
    /// <see cref="WalkableBounds"/> with the cliff sides of the raised tilemap, drops every cost field and creates a new
    /// pathfinder. Logs an error and stays unbuilt when no source is usable.
    /// </summary>
    public void BuildGrid()
    {
        cellsByPosition.Clear();
        resolvedSources.Clear();
        terrainLayers.Clear();
        watchedTilemaps.Clear();
        obstacleCells.Clear();
        fieldsByProfile.Clear();
        defaultField = null;
        cells = Array.Empty<CellData>();
        cliffSides = Array.Empty<byte>();
        HasCliffSides = false;
        cliffFaceHeight = 0f;
        cliffFaceRows = 0;
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

        if (raisedTilemap != null)
        {
            watchedTilemaps.Add(raisedTilemap);
        }

        cliffFaceHeight = ResolveCliffFaceHeight();
        cliffFaceRows = RaisedGround.RowsCovered(cliffFaceHeight);
        CollectObstacleCells();
        RemoveBlockedCells();
        walkableBounds = CalculateBounds();
        BakeCells();
        BakeCliffSides();
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

    /// <summary>
    /// Whether <paramref name="cell"/> is inside the grid and has terrain, without a movement profile (every terrain
    /// walkable).
    /// </summary>
    public bool IsCellWalkable(Vector3Int cell)
    {
        return IsCellWalkable(cell, null);
    }

    /// <summary>
    /// Whether <paramref name="cell"/> is inside the grid and walkable for <paramref name="movementProfile"/> (null =
    /// every terrain walkable).
    /// </summary>
    public bool IsCellWalkable(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        return TryGetIndex(cell, out int index) && GetCostField(movementProfile).Costs[index] > 0;
    }

    /// <summary>
    /// What keeps <paramref name="movementProfile"/> (null = every terrain walkable) off <paramref name="cell"/>, or
    /// <see cref="Blocker.None"/> when it can walk there. A cell without terrain, including one outside
    /// <see cref="WalkableBounds"/>, is <see cref="Blocker.NoGround"/> unless a wall or an obstacle is on it or the
    /// cliff face covers it.
    /// </summary>
    internal Blocker GetBlocker(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        if (TryGetIndex(cell, out int index) && cells[index] != null)
        {
            return GetCostField(movementProfile).Costs[index] > 0 ? Blocker.None : Blocker.Terrain;
        }

        Blocker blocker = BlockerOf(cell);
        return blocker != Blocker.None ? blocker : Blocker.NoGround;
    }

    /// <summary>
    /// The sides of <paramref name="cell"/> the cliff runs along, as <see cref="LeftSide"/>, <see cref="RightSide"/>,
    /// <see cref="DownSide"/> and <see cref="UpSide"/> bits; none outside <see cref="WalkableBounds"/>.
    /// </summary>
    internal byte GetCliffSides(Vector3Int cell)
    {
        return TryGetIndex(cell, out int index) ? cliffSides[index] : (byte)0;
    }

    /// <summary>
    /// Whether the Bresenham line from <paramref name="from"/> to <paramref name="to"/> crosses the cliff of raised
    /// ground, a diagonal step counting when either way round its corner does (<see cref="IsCliffStep"/>); steps with an
    /// end outside <see cref="WalkableBounds"/> cross nothing. For movement off the paths, such as crowd separation.
    /// </summary>
    public bool CrossesCliff(Vector3Int from, Vector3Int to)
    {
        int x = from.x;
        int y = from.y;
        int dx = Mathf.Abs(to.x - x);
        int dy = Mathf.Abs(to.y - y);
        int sx = x < to.x ? 1 : -1;
        int sy = y < to.y ? 1 : -1;
        int err = dx - dy;
        Vector3Int previous = from;
        while (x != to.x || y != to.y)
        {
            int e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y += sy;
            }

            Vector3Int current = new(x, y, from.z);
            if (StepCrossesCliff(previous, current))
            {
                return true;
            }

            previous = current;
        }

        return false;
    }

    /// <summary>
    /// <paramref name="point"/> moved off the cliff face the terrain renderer draws inside raised ground's bottom cells
    /// (<see cref="RaisedGround.OffTheFace"/>), so a goal set on the face, the cells it covers whole included, is reached
    /// on its top; unchanged without raised ground, before the grid is built, or where there is no face.
    /// </summary>
    public Vector2 MoveOffCliffFace(Vector2 point)
    {
        return raisedTilemap != null ? RaisedGround.OffTheFace(raisedTilemap, point, cliffFaceHeight) : point;
    }

    /// <summary>
    /// Whether one step from the cell at flat index <paramref name="index"/> to its neighbour (<paramref name="dx"/>,
    /// <paramref name="dy"/>), one of its 8 neighbours inside the grid, crosses the cliff: a straight step across a cliff
    /// side, a diagonal one when either way round its corner does, so no diagonal cuts past the cliff.
    /// </summary>
    internal bool IsCliffStep(int index, int dx, int dy)
    {
        if (dx == 0 || dy == 0)
        {
            return (cliffSides[index] & SideToward(dx, dy)) != 0;
        }

        byte horizontal = SideToward(dx, 0);
        byte vertical = SideToward(0, dy);
        return (cliffSides[index] & (horizontal | vertical)) != 0
            || (cliffSides[index + dx] & vertical) != 0
            || (cliffSides[index + (dy * width)] & horizontal) != 0;
    }

    /// <summary>
    /// The data cell containing <paramref name="worldPosition"/> on the reference tilemap, or the zero cell when there
    /// is no source tilemap.
    /// </summary>
    public Vector3Int WorldToCell(Vector3 worldPosition)
    {
        Tilemap tilemap = GetReferenceTilemap();
        return tilemap != null ? tilemap.WorldToCell(worldPosition) : Vector3Int.zero;
    }

    /// <summary>
    /// The world-space center of <paramref name="cell"/> on the reference tilemap, or the world origin when there is
    /// no source tilemap.
    /// </summary>
    public Vector3 CellToWorldCenter(Vector3Int cell)
    {
        Tilemap tilemap = GetReferenceTilemap();
        return tilemap != null ? tilemap.GetCellCenterWorld(cell) : Vector3.zero;
    }

    /// <summary>
    /// The nearest cell to <paramref name="origin"/> that has terrain, without a movement profile; see the profile
    /// overload.
    /// </summary>
    public bool TryGetNearestWalkableCell(Vector3Int origin, out Vector3Int nearest, int maxRadius = -1)
    {
        return TryGetNearestWalkableCell(origin, null, out nearest, maxRadius);
    }

    /// <summary>
    /// Finds the cell walkable for <paramref name="movementProfile"/> nearest to <paramref name="origin"/>, searching
    /// outward in rings of growing Manhattan distance up to <paramref name="maxRadius"/> (the grid's nearest-cell
    /// search radius when not positive). False, with <paramref name="nearest"/> left at the origin, when none is found.
    /// </summary>
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

    /// <summary>
    /// The cost of stepping from <paramref name="from"/> into the adjacent <paramref name="to"/> without a movement
    /// profile; see the profile overload.
    /// </summary>
    public int MovementCost(Vector3Int from, Vector3Int to)
    {
        return MovementCost(from, to, null);
    }

    /// <summary>
    /// The cost of stepping from <paramref name="from"/> into the adjacent <paramref name="to"/> for
    /// <paramref name="movementProfile"/>: the target's diagonal cost for a diagonal step, its straight cost otherwise,
    /// or a prohibitive cost when it is blocked.
    /// </summary>
    public int MovementCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        return MovementCost(GetCostField(movementProfile), from, to);
    }

    /// <summary>
    /// The octile distance estimate between two cells at the default traversal cost.
    /// </summary>
    public int HeuristicCost(Vector3Int from, Vector3Int to)
    {
        return HeuristicCost(from, to, null);
    }

    /// <summary>
    /// The octile distance between two cells at the cheapest traversal cost <paramref name="movementProfile"/> allows,
    /// ignoring the cells in between, so it never overestimates a path's cost.
    /// </summary>
    public int HeuristicCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        int baseTraversalCost = movementProfile != null ? movementProfile.GetMinimumTraversalCost() : DefaultTraversalCost;
        return OctileCost(Mathf.Abs(from.x - to.x), Mathf.Abs(from.y - to.y), baseTraversalCost, ScaleDiagonalCost(baseTraversalCost));
    }

    /// <summary>
    /// Whether both cells are walkable for <paramref name="movementProfile"/> and in the same 4-connected region, in
    /// constant time. False before the grid is built or for cells outside it.
    /// </summary>
    public bool AreCellsConnected(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        if (!IsBuilt || !TryGetIndex(from, out int fromIndex) || !TryGetIndex(to, out int toIndex))
        {
            return false;
        }

        int[] regions = GetCostField(movementProfile).Regions;
        return regions[fromIndex] >= 0 && regions[fromIndex] == regions[toIndex];
    }

    /// <summary>
    /// Bakes the cost field for <paramref name="movementProfile"/> now so the first path query does not pay for it.
    /// Does nothing before the grid is built.
    /// </summary>
    public void PrewarmRegions(TerrainMovementProfile2D movementProfile)
    {
        if (IsBuilt)
        {
            GetCostField(movementProfile);
        }
    }

    /// <summary>
    /// Whether the cost field for <paramref name="movementProfile"/> is baked and current, without baking it.
    /// </summary>
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

    /// <summary>
    /// Walks the Bresenham line from <paramref name="from"/> to <paramref name="to"/> and sums its step costs into
    /// <paramref name="cost"/>. False when either end or any step is blocked, including a diagonal step that would cut
    /// a blocked corner.
    /// </summary>
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

    /// <summary>
    /// The cell at flat index <paramref name="index"/>; the inverse of <see cref="TryGetIndex"/>.
    /// </summary>
    internal Vector3Int GetCell(int index)
    {
        int x = index % width;
        int rest = index / width;
        return new Vector3Int(walkableBounds.xMin + x, walkableBounds.yMin + (rest % height), walkableBounds.zMin + (rest / height));
    }

    /// <summary>
    /// The cost field for <paramref name="movementProfile"/> (null = every terrain walkable at the default cost),
    /// baked on first use and rebaked when the profile's <see cref="TerrainMovementProfile2D.Version"/> or the grid's
    /// <see cref="Version"/> changes.
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

    /// <summary>
    /// The cost of a move of <paramref name="dx"/> by <paramref name="dy"/> cells taking as many diagonal steps as
    /// possible, then straight ones.
    /// </summary>
    internal static int OctileCost(int dx, int dy, int straightCost, int diagonalCost)
    {
        int diagonal = Mathf.Min(dx, dy);
        int straight = Mathf.Max(dx, dy) - diagonal;
        return (diagonalCost * diagonal) + (straightCost * straight);
    }

    /// <summary>
    /// Whether <paramref name="cell"/> is inside the grid and has a nonzero cost in <paramref name="field"/>.
    /// </summary>
    private bool IsWalkable(CostField field, Vector3Int cell)
    {
        return TryGetIndex(cell, out int index) && field.Costs[index] > 0;
    }

    /// <summary>
    /// Whether one step from <paramref name="from"/> to <paramref name="to"/> is allowed in <paramref name="field"/>:
    /// the cells are adjacent, the target is walkable, a diagonal step does not cut a blocked corner, and the step does
    /// not cross the cliff.
    /// </summary>
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

        if (absX == 1 && absY == 1
            && (!IsWalkable(field, new Vector3Int(from.x + dx, from.y, from.z)) || !IsWalkable(field, new Vector3Int(from.x, from.y + dy, from.z))))
        {
            return false;
        }

        return !StepCrossesCliff(from, to);
    }

    /// <summary>
    /// Whether the step from <paramref name="from"/> to <paramref name="to"/>, one of its 8 neighbours, crosses the
    /// cliff (<see cref="IsCliffStep"/>). False for cells that are not neighbours or lie outside the grid.
    /// </summary>
    private bool StepCrossesCliff(Vector3Int from, Vector3Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        if ((dx == 0 && dy == 0) || Mathf.Abs(dx) > 1 || Mathf.Abs(dy) > 1 || !TryGetIndex(to, out _))
        {
            return false;
        }

        return TryGetIndex(from, out int index) && IsCliffStep(index, dx, dy);
    }

    /// <summary>
    /// The side of a cell facing the neighbour (<paramref name="dx"/>, <paramref name="dy"/>) away along one axis.
    /// </summary>
    private static byte SideToward(int dx, int dy)
    {
        return dx > 0 ? RightSide : dx < 0 ? LeftSide : dy > 0 ? UpSide : DownSide;
    }

    /// <summary>
    /// The cost of entering <paramref name="to"/> from <paramref name="from"/> in <paramref name="field"/>: its
    /// diagonal cost for a diagonal step, its straight cost otherwise, or a prohibitive cost when it is blocked or the
    /// step crosses the cliff.
    /// </summary>
    private int MovementCost(CostField field, Vector3Int from, Vector3Int to)
    {
        if (!TryGetIndex(to, out int index) || field.Costs[index] == 0 || StepCrossesCliff(from, to))
        {
            return BlockedCost;
        }

        bool isDiagonal = Mathf.Abs(to.x - from.x) == 1 && Mathf.Abs(to.y - from.y) == 1;
        return isDiagonal ? field.DiagonalCosts[index] : field.Costs[index];
    }

    /// <summary>
    /// Adds the listed terrain sources and, when discovery is on, those in the children (inactive ones included) to the
    /// resolved sources, skipping nulls and duplicates.
    /// </summary>
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
    /// Re-reads the changed cells when <paramref name="tilemap"/> is one of this grid's source or blocking tilemaps, or
    /// the cliff sides around them and the cells whose face they decide when it is the raised tilemap. Called by the grid
    /// itself while enabled, and by an editor listener outside Play Mode so the gizmos stay current.
    /// </summary>
    internal void HandleTilemapPositionsChanged(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        if (!IsBuilt || !watchedTilemaps.Contains(tilemap))
        {
            return;
        }

        bool raised = tilemap == raisedTilemap;
        for (int i = 0; i < positions.Length; i++)
        {
            if (raised)
            {
                RefreshCliffSides(positions[i]);
            }

            if ((raised && !RefreshFaceColumn(positions[i])) || (!raised && !RefreshCell(positions[i])))
            {
                BuildGrid();
                return;
            }
        }

        Version++;
    }

    /// <summary>
    /// Re-reads <paramref name="changed"/>, a cell of the raised tilemap, and the cells above it up to the rows the cliff
    /// face covers, whose face it decides (<see cref="RaisedGround.IsOnFace(Tilemap, Vector3Int, int)"/>); nothing when
    /// the face covers no whole row. False when a cell needs a full rebuild (<see cref="RefreshCell"/>).
    /// </summary>
    private bool RefreshFaceColumn(Vector3Int changed)
    {
        if (cliffFaceRows == 0)
        {
            return true;
        }

        for (int z = walkableBounds.zMin; z < walkableBounds.zMax; z++)
        {
            for (int up = 0; up <= cliffFaceRows; up++)
            {
                if (!RefreshCell(new Vector3Int(changed.x, changed.y + up, z)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Re-reads the cliff sides of <paramref name="changed"/>, a cell of the raised tilemap, and of its 4 neighbours, on
    /// every layer of the grid.
    /// </summary>
    private void RefreshCliffSides(Vector3Int changed)
    {
        for (int z = walkableBounds.zMin; z < walkableBounds.zMax; z++)
        {
            Vector3Int cell = new(changed.x, changed.y, z);
            if (TryGetIndex(cell, out int index))
            {
                cliffSides[index] = ReadCliffSides(cell);
            }

            for (int side = 0; side < SideSteps.Length; side++)
            {
                if (TryGetIndex(cell + SideSteps[side], out int neighbour))
                {
                    cliffSides[neighbour] = ReadCliffSides(cell + SideSteps[side]);
                }
            }
        }
    }

    /// <summary>
    /// The sides of <paramref name="cell"/> a step across which <see cref="RaisedGround.CanCross"/> refuses, read from
    /// the raised tilemap, as <see cref="LeftSide"/>, <see cref="RightSide"/>, <see cref="DownSide"/> and
    /// <see cref="UpSide"/> bits.
    /// </summary>
    private byte ReadCliffSides(Vector3Int cell)
    {
        byte kind = RaisedGround.KindAt(raisedTilemap, cell);
        byte sides = 0;
        for (int side = 0; side < SideSteps.Length; side++)
        {
            Vector3Int step = SideSteps[side];
            if (!RaisedGround.CanCross(kind, RaisedGround.KindAt(raisedTilemap, cell + step), step))
            {
                sides |= (byte)(1 << side);
            }
        }

        return sides;
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

    /// <summary>
    /// Reads a cell's terrain from the terrain layers into <paramref name="reuse"/> (or a new cell data), or returns
    /// null when the cell is blocked or has no terrain.
    /// </summary>
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

    /// <summary>
    /// Whether <paramref name="cell"/> lies under an obstacle collider, any watched blocking tilemap has a tile there, or
    /// the cliff face covers it whole.
    /// </summary>
    private bool IsBlocked(Vector3Int cell)
    {
        return BlockerOf(cell) != Blocker.None;
    }

    /// <summary>
    /// What blocks <paramref name="cell"/> for every terrain layer, checked in this order: a collider on the obstacle
    /// layers, a tile on a watched blocking tilemap, the cliff face covering it whole; <see cref="Blocker.None"/> when
    /// nothing does.
    /// </summary>
    private Blocker BlockerOf(Vector3Int cell)
    {
        if (obstacleCells.Contains(cell))
        {
            return Blocker.Obstacle;
        }

        for (int i = 0; i < blockingTilemaps.Count; i++)
        {
            if (blockingTilemaps[i] != null && watchedTilemaps.Contains(blockingTilemaps[i]) && blockingTilemaps[i].HasTile(cell))
            {
                return Blocker.Wall;
            }
        }

        return IsCoveredByCliffFace(cell) ? Blocker.CliffFace : Blocker.None;
    }

    /// <summary>
    /// Whether the cliff face of the raised tilemap covers <paramref name="cell"/> whole: one of the rows of the face it
    /// covers whole (<see cref="RaisedGround.RowsCovered"/>); never without a raised tilemap or a face height.
    /// </summary>
    private bool IsCoveredByCliffFace(Vector3Int cell)
    {
        return cliffFaceRows > 0 && raisedTilemap != null && RaisedGround.IsOnFace(raisedTilemap, cell, cliffFaceRows);
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

    /// <summary>
    /// Records every cell that a collider on the obstacle layers overlaps within the collected cells' area: the cells
    /// its bounds touch, shrunk a little so a collider ending on a cell edge does not block the next cell. Colliders on
    /// tilemaps (the collision tilemap's own collider) and triggers are skipped, since blocking tilemaps already cover
    /// their cells.
    /// </summary>
    private void CollectObstacleCells()
    {
        if (obstacleLayers.value == 0 || cellsByPosition.Count == 0)
        {
            return;
        }

        BoundsInt area = CalculateBounds();
        Vector2 areaMin = referenceTilemap.CellToWorld(area.min);
        Vector2 areaMax = referenceTilemap.CellToWorld(new Vector3Int(area.xMax, area.yMax, area.zMin));
        ContactFilter2D filter = new() { useTriggers = false };
        filter.SetLayerMask(obstacleLayers);
        Physics2D.SyncTransforms();
        obstacleHits.Clear();
        Physics2D.OverlapArea(areaMin, areaMax, filter, obstacleHits);

        for (int i = 0; i < obstacleHits.Count; i++)
        {
            Collider2D hit = obstacleHits[i];
            if (hit.TryGetComponent(out Tilemap _))
            {
                continue;
            }

            Bounds bounds = hit.bounds;
            Vector3 inset = new(Mathf.Min(ObstacleEdgeInset, bounds.extents.x), Mathf.Min(ObstacleEdgeInset, bounds.extents.y), 0f);
            Vector3Int min = referenceTilemap.WorldToCell(bounds.min + inset);
            Vector3Int max = referenceTilemap.WorldToCell(bounds.max - inset);
            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    obstacleCells.Add(new Vector3Int(x, y, area.zMin));
                }
            }
        }

        obstacleHits.Clear();
    }

    /// <summary>
    /// Removes every cell with a tile on a watched blocking tilemap, under an obstacle collider or covered whole by the
    /// cliff face from the cells collected for the build.
    /// </summary>
    private void RemoveBlockedCells()
    {
        foreach (Vector3Int cell in obstacleCells)
        {
            cellsByPosition.Remove(cell);
        }

        if (cliffFaceRows > 0 && raisedTilemap != null)
        {
            foreach (Vector3Int cell in raisedTilemap.cellBounds.allPositionsWithin)
            {
                if (IsCoveredByCliffFace(cell))
                {
                    cellsByPosition.Remove(cell);
                }
            }
        }

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

    /// <summary>
    /// Sizes the flat cell array to <see cref="WalkableBounds"/> and copies the collected cells into it.
    /// </summary>
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

    /// <summary>
    /// Sizes the cliff sides to the cells and reads each cell's from the raised tilemap (<see cref="ReadCliffSides"/>);
    /// none without a raised tilemap, which <see cref="HasCliffSides"/> tells A* so it skips the check.
    /// </summary>
    private void BakeCliffSides()
    {
        cliffSides = new byte[cells.Length];
        HasCliffSides = raisedTilemap != null;
        if (raisedTilemap == null)
        {
            return;
        }

        for (int i = 0; i < cliffSides.Length; i++)
        {
            cliffSides[i] = ReadCliffSides(GetCell(i));
        }
    }

    /// <summary>
    /// The height of the cliff face as a fraction of a cell: the cliff height of the style of the terrain renderer on this
    /// object that draws the same raised tilemap, over the style's pixels per cell; 0 without one.
    /// </summary>
    private float ResolveCliffFaceHeight()
    {
        return raisedTilemap != null && TryGetComponent(out TerrainBlendRenderer renderer)
            && renderer.RaisedTilemap == raisedTilemap && renderer.Style != null
            ? renderer.Style.CliffHeight / TerrainStyle.PixelsPerCell
            : 0f;
    }

    /// <summary>
    /// Fills <paramref name="field"/> for <paramref name="movementProfile"/>: traversal costs plus the profile's edge
    /// cost, diagonal costs, the minimum costs the heuristic uses, and the regions.
    /// </summary>
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
    /// Adds <paramref name="edgeCost"/> to every walkable cell with a blocked or missing cell among its 8 neighbours or
    /// the cliff along one of its sides, on either side of it. Costs only grow, so a cell's walkability does not change
    /// while the pass runs.
    /// </summary>
    private void AddEdgeCost(int[] costs, int edgeCost)
    {
        for (int index = 0; index < costs.Length; index++)
        {
            if (costs[index] > 0 && (cliffSides[index] != 0 || TouchesBlockedCell(costs, index)))
            {
                costs[index] += edgeCost;
            }
        }
    }

    /// <summary>
    /// Whether the cell at <paramref name="index"/> has a blocked cell among its 8 neighbours, counting the edge of the
    /// grid as blocked.
    /// </summary>
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

    /// <summary>
    /// A cell's traversal cost under <paramref name="movementProfile"/>: 0 when it has no terrain or any of its terrain
    /// types is unwalkable, else the highest cost among them. Without a profile every terrain costs the default.
    /// </summary>
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

    /// <summary>
    /// Labels the 4-connected regions of walkable cells in <paramref name="field"/> with a flood fill that does not cross
    /// the cliff; blocked cells get -1.
    /// </summary>
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
                if (x > 0 && (cliffSides[current] & LeftSide) == 0)
                {
                    tail = EnqueueRegionCell(current - 1, nextRegion, costs, regions, tail);
                }

                if (x < width - 1 && (cliffSides[current] & RightSide) == 0)
                {
                    tail = EnqueueRegionCell(current + 1, nextRegion, costs, regions, tail);
                }

                if (y > 0 && (cliffSides[current] & DownSide) == 0)
                {
                    tail = EnqueueRegionCell(current - width, nextRegion, costs, regions, tail);
                }

                if (y < height - 1 && (cliffSides[current] & UpSide) == 0)
                {
                    tail = EnqueueRegionCell(current + width, nextRegion, costs, regions, tail);
                }
            }

            nextRegion++;
        }
    }

    /// <summary>
    /// Labels the cell at <paramref name="index"/> with <paramref name="region"/> and queues it when it is walkable and
    /// not labeled yet. Returns the new queue tail.
    /// </summary>
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

    /// <summary>
    /// The smallest bounds holding every collected cell, or empty bounds when there are none.
    /// </summary>
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

    /// <summary>
    /// The cost of entering a cell diagonally: its straight cost times 1.4, rounded, and at least 1.
    /// </summary>
    private static int ScaleDiagonalCost(int traversalCost)
    {
        return Mathf.Max(1, Mathf.RoundToInt(traversalCost * 1.4f));
    }

    /// <summary>
    /// The tilemap that world/cell conversions use: the first usable source's data tilemap once built, else the first
    /// source that has a data tilemap. Null when there is none.
    /// </summary>
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

    /// <summary>
    /// Marks the gizmo grid stale after an inspector change so it rebuilds on the next gizmo draw.
    /// </summary>
    private void OnValidate()
    {
        gizmoGridIsStale = true;
    }

    /// <summary>
    /// Outside Play Mode, builds the grid for its gizmos when it is stale, or unbuilt and not failed already: a failed
    /// build is tried again only once the grid goes stale, so a broken setup logs its error once rather than on every
    /// repaint.
    /// </summary>
    internal void BuildGizmoGridIfStale()
    {
        if (Application.isPlaying || (!gizmoGridIsStale && (IsBuilt || gizmoBuildFailed)))
        {
            return;
        }

        gizmoGridIsStale = false;
        BuildGrid();
        gizmoBuildFailed = !IsBuilt;
    }

    /// <summary>
    /// Builds the gizmo grid when it is stale (<see cref="BuildGizmoGridIfStale"/>); then draws the walkable bounds and,
    /// when enabled, up to a capped number of walkable cells.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (drawGridBoundsGizmo || drawWalkableCellGizmos)
        {
            BuildGizmoGridIfStale();
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

    /// <summary>
    /// Draws <see cref="WalkableBounds"/> as a wire box in world space.
    /// </summary>
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
