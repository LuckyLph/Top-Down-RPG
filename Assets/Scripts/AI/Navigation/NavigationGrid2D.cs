using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The walkable cells of an area or of one chunk of a world, read from its terrain sources minus its blocking
/// tilemaps, the cells under colliders on its obstacle layers (blocking props) and the cells the cliff face of its
/// raised tilemap covers whole, and the cliff itself: a line along the sides between raised and level cells that steps
/// cross only at a ramp's foot (<see cref="RaisedGround.CanCross"/>). It builds a <see cref="NavigationPage"/> of them.
/// On an area it is the area's navigation: a <see cref="NavigationMap2D"/> of that one page over its
/// <see cref="WalkableBounds"/>, built in <c>Awake</c>. On a chunk's grid (one with a <see cref="WorldChunk"/>) its page
/// covers the chunk's cells and joins its world's <see cref="WorldNavigation"/> when the chunk's scope adds it; outside
/// Play Mode it builds that page on its own for the gizmos. Each movement profile gets a cost field, baked on first use
/// and rebaked when the profile is edited or the cells change. While enabled it follows tile changes on its source,
/// blocking and raised tilemaps, updating just those cells and bumping <see cref="Version"/>.
/// </summary>
public class NavigationGrid2D : MonoBehaviour, INavigation2D
{
    internal const float ObstacleEdgeInset = 0.01f;
    internal const byte LeftSide = 1;
    internal const byte RightSide = 2;
    internal const byte DownSide = 4;
    internal const byte UpSide = 8;

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

    [SerializeField] private List<NavigationTerrainSource2D> terrainSources = new();
    [SerializeField] private bool discoverSourcesInChildren = true;
    [Tooltip("Tilemaps whose cells are blocked for every terrain layer, such as the collision tilemap.")]
    [SerializeField] private List<Tilemap> blockingTilemaps = new();
    [Tooltip("Optional: the area's raised ground. Its cliff, the line between raised and level cells, is crossed only at a ramp's foot, so units move between raised and level ground only over a ramp; the cells the cliff face the terrain renderer draws covers whole are blocked, and a goal on the face moves up to its top.")]
    [SerializeField] private Tilemap raisedTilemap;
    [Tooltip("Layers whose colliders block every cell they overlap, such as the footprints of blocking props. Read " +
        "when the grid builds; colliders on tilemaps and triggers are ignored.")]
    [SerializeField] private LayerMask obstacleLayers;
    [Tooltip("How far, in cells, the search for the nearest walkable cell looks when its caller gives no radius: A*'s " +
        "start and goal, and the mobs and players moving on this grid. A chunk's grid uses its world's instead.")]
    [SerializeField] private int nearestCellSearchRadius = NavigationMap2D.DefaultNearestCellSearchRadius;
    [Tooltip("Upper bound on cells a partial A* search toward an unreachable goal may expand before returning " +
        "the closest cell found so far. Searches toward reachable goals are never capped. A chunk's grid uses its " +
        "world's instead.")]
    [SerializeField, Min(1)] private int maxSearchCells = NavigationMap2D.DefaultMaxSearchCells;
    [Header("Debug")]
    [SerializeField] private bool drawGridBoundsGizmo = true;
    [SerializeField] private bool drawWalkableCellGizmos = false;
    [SerializeField, Min(1)] private int maxWalkableCellsToDraw = 400;
    [SerializeField] private Color gridBoundsColor = new(0.35f, 1f, 1f, 0.8f);
    [SerializeField] private Color walkableCellColor = new(0.25f, 1f, 0.3f, 0.35f);

    private readonly List<NavigationTerrainSource2D> resolvedSources = new();
    private readonly List<NavigationTerrainSource2D> terrainLayers = new();
    private readonly List<Tilemap> usableBlockingTilemaps = new();
    private readonly List<Tilemap> watchedTilemaps = new();
    private readonly HashSet<Vector3Int> obstacleCells = new();
    private readonly List<Collider2D> obstacleHits = new();
    private NavigationMap2D map;
    private NavigationPage page;
    private bool ownsMap;
    private int versionBase;
    private float cliffFaceHeight;
    private bool gizmoGridIsStale;
    private bool gizmoBuildFailed;
    private Tilemap referenceTilemap;

    /// <summary>
    /// The pathfinder of the map its page is in; null before the grid is built.
    /// </summary>
    public IPathfinder2D Pathfinder => map != null ? map.Pathfinder : null;

    /// <summary>
    /// Changes whenever its cells change: a full <see cref="BuildGrid"/>, tiles changing on a source, blocking or
    /// raised tilemap, or, on a chunk, a neighbouring chunk's page loading or unloading. Path followers and the gizmos
    /// compare it to the version they read.
    /// </summary>
    public int Version => versionBase + (page != null ? page.Changes : 0);

    /// <summary>
    /// The cells its page covers: the smallest bounds holding every walkable cell on an area, the chunk's cells on a
    /// chunk; empty before it is built.
    /// </summary>
    public BoundsInt WalkableBounds => page != null && page.CellCount > 0
        ? new BoundsInt(page.Rect.xMin, page.Rect.yMin, page.Z, page.Width, page.Height, 1)
        : default;

    public bool IsBuilt => this != null && page != null && map != null;

    public int MaxSearchCells
    {
        get => maxSearchCells;
        set
        {
            maxSearchCells = Mathf.Max(1, value);
            if (ownsMap && map != null)
            {
                map.MaxSearchCells = maxSearchCells;
            }
        }
    }

    public IReadOnlyList<NavigationTerrainSource2D> TerrainSources => terrainSources;
    public IReadOnlyList<Tilemap> BlockingTilemaps => blockingTilemaps;
    public Tilemap RaisedTilemap => raisedTilemap;
    public LayerMask ObstacleLayers => obstacleLayers;

    internal int CellCount => page != null ? page.CellCount : 0;
    internal bool HasCliffSides => map != null && map.HasCliffSides;
    internal int Width => page != null ? page.Width : 0;
    internal int Height => page != null ? page.Height : 0;
    internal NavigationMap2D Map => map;

    /// <summary>
    /// Builds an area's grid. A chunk's grid waits for its world's navigation to add its page.
    /// </summary>
    private void Awake()
    {
        if (!TryGetComponent(out WorldChunk _))
        {
            BuildGrid();
        }
    }

    /// <summary>
    /// Starts following tile changes on the source, blocking and raised tilemaps.
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
    /// Rebuilds the grid from scratch and bumps <see cref="Version"/>. An area's grid (or a chunk's outside a world)
    /// collects the cells of every usable terrain source (listed and, optionally, found in the children), reads the
    /// cliff face's height and the obstacle colliders, and builds a page over <see cref="WalkableBounds"/> in a map of
    /// its own, with a new pathfinder; a chunk's grid in a world builds its page again in place. Logs an error and stays
    /// unbuilt when no source is usable.
    /// </summary>
    public void BuildGrid()
    {
        if (!ownsMap && map != null && page != null)
        {
            PlaceIn(map, page.Rect);
            return;
        }

        Unbuild();
        if (!ResolveConfiguration())
        {
            return;
        }

        if (TryGetComponent(out WorldChunk chunk))
        {
            CollectObstacleCells(chunk.Cells);
            BuildOwnMap(chunk.Cells);
            return;
        }

        RectInt candidate = CollectSourceBounds();
        CollectObstacleCells(candidate);
        BuildOwnMap(candidate);
        RectInt walkable = WalkableRect();
        if (!walkable.Equals(candidate))
        {
            BuildOwnMap(walkable);
        }
    }

    /// <summary>
    /// Re-reads <paramref name="changedCells"/> from the source and blocking tilemaps after their tiles changed, and
    /// bumps <see cref="Version"/>. A cell outside an area's <see cref="WalkableBounds"/> that gained terrain rebuilds the
    /// grid. Tile changes on those tilemaps already arrive here on their own while the grid is enabled.
    /// </summary>
    public void RefreshCells(IReadOnlyList<Vector3Int> changedCells)
    {
        if (!IsBuilt)
        {
            return;
        }

        for (int i = 0; i < changedCells.Count; i++)
        {
            if (!map.RefreshCell(changedCells[i]))
            {
                BuildGrid();
                return;
            }
        }
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
        return map != null && map.IsCellWalkable(cell, movementProfile);
    }

    /// <summary>
    /// What keeps <paramref name="movementProfile"/> (null = every terrain walkable) off <paramref name="cell"/>, or
    /// <see cref="Blocker.None"/> when it can walk there. A cell without terrain, including one outside
    /// <see cref="WalkableBounds"/> (or, on a chunk, in a chunk not loaded), is <see cref="Blocker.NoGround"/> unless a
    /// wall or an obstacle is on it or the cliff face covers it.
    /// </summary>
    internal Blocker GetBlocker(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        return map != null ? map.GetBlocker(cell, movementProfile) : Blocker.NoGround;
    }

    /// <summary>
    /// The sides of <paramref name="cell"/> the cliff runs along, as <see cref="LeftSide"/>, <see cref="RightSide"/>,
    /// <see cref="DownSide"/> and <see cref="UpSide"/> bits; none outside the loaded pages.
    /// </summary>
    internal byte GetCliffSides(Vector3Int cell)
    {
        return map != null ? map.GetCliffSides(cell) : (byte)0;
    }

    /// <summary>
    /// Whether the Bresenham line from <paramref name="from"/> to <paramref name="to"/> crosses the cliff of raised
    /// ground, a diagonal step counting when either way round its corner does; steps with an end outside the grid cross
    /// nothing. For movement off the paths, such as crowd separation.
    /// </summary>
    public bool CrossesCliff(Vector3Int from, Vector3Int to)
    {
        return map != null && map.CrossesCliff(from, to);
    }

    /// <summary>
    /// <paramref name="point"/> moved off the cliff face the terrain renderer draws inside raised ground's bottom cells
    /// (<see cref="RaisedGround.OffTheFace(Tilemap, Vector2, float)"/>), so a goal set on the face, the cells it covers
    /// whole included, is reached on its top; unchanged without raised ground, before the grid is built, or where there
    /// is no face.
    /// </summary>
    public Vector2 MoveOffCliffFace(Vector2 point)
    {
        return map != null ? map.MoveOffCliffFace(point) : point;
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
        if (map == null)
        {
            nearest = origin;
            return false;
        }

        return map.TryGetNearestWalkableCell(origin, movementProfile, out nearest, maxRadius);
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
        return map != null ? map.MovementCost(from, to, movementProfile) : NavigationMap2D.BlockedCost;
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
        int baseTraversalCost = movementProfile != null ? movementProfile.GetMinimumTraversalCost() : NavigationMap2D.DefaultTraversalCost;
        return NavigationMap2D.OctileCost(Mathf.Abs(from.x - to.x), Mathf.Abs(from.y - to.y), baseTraversalCost, NavigationMap2D.ScaleDiagonalCost(baseTraversalCost));
    }

    /// <summary>
    /// Whether both cells are walkable for <paramref name="movementProfile"/> and in the same connected region, in
    /// constant time. False before the grid is built or for cells outside it.
    /// </summary>
    public bool AreCellsConnected(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        return IsBuilt && map.AreCellsConnected(from, to, movementProfile);
    }

    /// <summary>
    /// Whether the cell's region runs on beyond the grid's map (<see cref="NavigationMap2D.RunsBeyondLoadedPages"/>):
    /// never for a standalone area, whose one page is all there is. False before the grid is built.
    /// </summary>
    public bool RunsBeyondLoadedPages(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        return IsBuilt && map.RunsBeyondLoadedPages(cell, movementProfile);
    }

    /// <summary>
    /// Bakes the cost field for <paramref name="movementProfile"/> now so the first path query does not pay for it.
    /// Does nothing before the grid is built.
    /// </summary>
    public void PrewarmRegions(TerrainMovementProfile2D movementProfile)
    {
        if (IsBuilt)
        {
            map.PrewarmRegions(movementProfile);
        }
    }

    /// <summary>
    /// Whether the cost field for <paramref name="movementProfile"/> is baked and current, without baking it.
    /// </summary>
    internal bool AreRegionsLabeled(TerrainMovementProfile2D movementProfile)
    {
        return map != null && map.Pool.IsCostFieldCurrent(movementProfile);
    }

    /// <summary>
    /// Walks the Bresenham line from <paramref name="from"/> to <paramref name="to"/> and sums its step costs into
    /// <paramref name="cost"/>. False when either end or any step is blocked, including a diagonal step that would cut
    /// a blocked corner.
    /// </summary>
    public bool TryGetLineCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile, out int cost)
    {
        if (map == null)
        {
            cost = 0;
            return false;
        }

        return map.TryGetLineCost(from, to, movementProfile, out cost);
    }

    /// <summary>
    /// The cell at local index <paramref name="index"/> of its page (row by row from <see cref="WalkableBounds"/>'s
    /// minimum).
    /// </summary>
    internal Vector3Int GetCell(int index)
    {
        return page.CellAt(index);
    }

    /// <summary>
    /// Builds this grid's page over <paramref name="cells"/> and adds it to <paramref name="target"/>, a world's map,
    /// leaving any map it was in; logs an error and adds nothing when no source is usable. Called by
    /// <see cref="WorldNavigation.AddChunk"/>.
    /// </summary>
    internal bool PlaceIn(NavigationMap2D target, RectInt cells)
    {
        Unbuild();
        if (!ResolveConfiguration())
        {
            return false;
        }

        CollectObstacleCells(cells);
        page = CreatePage(cells);
        target.AddPage(page);
        map = target;
        ownsMap = false;
        return true;
    }

    /// <summary>
    /// Takes its page out of the map it is in, leaving the grid unbuilt. Called by
    /// <see cref="WorldNavigation.RemoveChunk"/>.
    /// </summary>
    internal void Unbuild()
    {
        if (page != null)
        {
            versionBase += page.Changes + 1;
            if (map != null)
            {
                map.RemovePage(page);
            }
        }
        else
        {
            versionBase++;
        }

        page = null;
        map = null;
        ownsMap = false;
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

        bool raised = tilemap == page.Raised;
        for (int i = 0; i < positions.Length; i++)
        {
            bool refreshed = raised ? map.RefreshRaisedCell(page, positions[i]) : map.RefreshCell(positions[i]);
            if (!refreshed)
            {
                BuildGrid();
                return;
            }
        }
    }

    /// <summary>
    /// Resolves what the next page reads: the usable terrain sources (at most <see cref="NavigationPage.MaxLayers"/>),
    /// the blocking tilemaps that are not dual-grid render tilemaps, the raised tilemap and the cliff face's height.
    /// Logs an error and returns false when no source is usable.
    /// </summary>
    private bool ResolveConfiguration()
    {
        resolvedSources.Clear();
        terrainLayers.Clear();
        usableBlockingTilemaps.Clear();
        watchedTilemaps.Clear();
        obstacleCells.Clear();
        referenceTilemap = null;
        cliffFaceHeight = 0f;

        ResolveTerrainSources();
        if (resolvedSources.Count == 0)
        {
            Debug.LogError("NavigationGrid2D could not find any NavigationTerrainSource2D components.", this);
            return false;
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

            if (terrainLayers.Count == NavigationPage.MaxLayers)
            {
                Debug.LogError($"NavigationGrid2D skipped terrain source '{source.name}': a grid reads at most {NavigationPage.MaxLayers} terrain layers.", source);
                continue;
            }

            if (referenceTilemap == null)
            {
                referenceTilemap = dataTilemap;
            }

            terrainLayers.Add(source);
            watchedTilemaps.Add(dataTilemap);
        }

        if (referenceTilemap == null)
        {
            Debug.LogError("NavigationGrid2D could not build because none of its terrain sources were fully configured.", this);
            return false;
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

            usableBlockingTilemaps.Add(blockingTilemap);
            watchedTilemaps.Add(blockingTilemap);
        }

        if (raisedTilemap != null)
        {
            watchedTilemaps.Add(raisedTilemap);
        }

        cliffFaceHeight = ResolveCliffFaceHeight();
        return true;
    }

    /// <summary>
    /// A page over <paramref name="cells"/> reading the resolved configuration.
    /// </summary>
    private NavigationPage CreatePage(RectInt cells)
    {
        return new NavigationPage(this, cells, 0, terrainLayers, usableBlockingTilemaps, raisedTilemap, obstacleCells, cliffFaceHeight, referenceTilemap);
    }

    /// <summary>
    /// Builds a page over <paramref name="cells"/> in a map and pool of its own, replacing any it had.
    /// </summary>
    private void BuildOwnMap(RectInt cells)
    {
        if (page != null && map != null)
        {
            versionBase += page.Changes;
            map.RemovePage(page);
        }

        NavigationPagePool pool = new(1, cells.width * cells.height);
        map = new NavigationMap2D(pool, cells.min, cells.size, Vector2Int.one, singleArea: !TryGetComponent(out WorldChunk _))
        {
            MaxSearchCells = maxSearchCells,
            NearestCellSearchRadius = nearestCellSearchRadius,
        };
        page = CreatePage(cells);
        map.AddPage(page);
        ownsMap = true;
    }

    /// <summary>
    /// The smallest rectangle holding every cell with a tile on a usable terrain source (row z = 0), or an empty one.
    /// </summary>
    private RectInt CollectSourceBounds()
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        for (int i = 0; i < terrainLayers.Count; i++)
        {
            Tilemap tilemap = terrainLayers[i].DataTilemap;
            BoundsInt bounds = tilemap.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.zMin > 0 || bounds.zMax <= 0)
            {
                continue;
            }

            BoundsInt row = new(bounds.xMin, bounds.yMin, 0, bounds.size.x, bounds.size.y, 1);
            TileBase[] tiles = tilemap.GetTilesBlock(row);
            for (int index = 0; index < tiles.Length; index++)
            {
                if (tiles[index] == null)
                {
                    continue;
                }

                int x = bounds.xMin + (index % bounds.size.x);
                int y = bounds.yMin + (index / bounds.size.x);
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }

        return minX > maxX ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// The smallest rectangle holding every walkable cell of the page, or an empty one.
    /// </summary>
    private RectInt WalkableRect()
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        for (int local = 0; local < page.CellCount; local++)
        {
            if (page.Terrain[local] == 0)
            {
                continue;
            }

            int x = local % page.Width;
            int y = local / page.Width;
            minX = Mathf.Min(minX, x);
            minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x);
            maxY = Mathf.Max(maxY, y);
        }

        return minX > maxX
            ? new RectInt(0, 0, 0, 0)
            : new RectInt(page.Rect.xMin + minX, page.Rect.yMin + minY, maxX - minX + 1, maxY - minY + 1);
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
    /// Records every cell that a collider on the obstacle layers overlapping <paramref name="area"/> covers: the cells its
    /// bounds touch, shrunk a little so a collider ending on a cell edge does not block the next cell. Colliders on
    /// tilemaps (the collision tilemap's own collider) and triggers are skipped, since blocking tilemaps already cover
    /// their cells.
    /// </summary>
    private void CollectObstacleCells(RectInt area)
    {
        if (obstacleLayers.value == 0 || area.width <= 0 || area.height <= 0)
        {
            return;
        }

        Vector2 areaMin = referenceTilemap.CellToWorld(new Vector3Int(area.xMin, area.yMin, 0));
        Vector2 areaMax = referenceTilemap.CellToWorld(new Vector3Int(area.xMax, area.yMax, 0));
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
                    obstacleCells.Add(new Vector3Int(x, y, 0));
                }
            }
        }

        obstacleHits.Clear();
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

        if (!drawWalkableCellGizmos || page == null)
        {
            return;
        }

        Gizmos.color = walkableCellColor;
        int drawn = 0;
        for (int i = 0; i < page.CellCount && drawn < maxWalkableCellsToDraw; i++)
        {
            if (page.Terrain[i] == 0)
            {
                continue;
            }

            Gizmos.DrawCube(CellToWorldCenter(page.CellAt(i)), Vector3.one * 0.12f);
            drawn++;
        }
    }

    /// <summary>
    /// Draws <see cref="WalkableBounds"/> as a wire box in world space.
    /// </summary>
    private void DrawGridBoundsGizmo()
    {
        Tilemap tilemap = GetReferenceTilemap();
        BoundsInt bounds = WalkableBounds;
        if (tilemap == null || bounds.size == Vector3Int.zero)
        {
            return;
        }

        Vector3 min = tilemap.CellToWorld(bounds.min);
        Vector3 max = tilemap.CellToWorld(bounds.max);
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
