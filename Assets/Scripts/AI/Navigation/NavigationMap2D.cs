using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Navigation over a set of <see cref="NavigationPage"/>s laid on a grid of equal pages and placed in a
/// <see cref="NavigationPagePool"/>: walkability, costs, line costs, the nearest walkable cell, regions, the cliff of
/// raised ground and A* (<see cref="GridAStarPathfinder2D"/>), all reading across the borders of loaded neighbouring
/// pages. A cell where no page is loaded is blocked. An area's <see cref="NavigationGrid2D"/> is a map of one page over
/// its walkable bounds, which also reads its tilemaps outside them; a world's <see cref="WorldNavigation"/> is a map of a
/// page per loaded chunk. Loading, unloading or changing a page bumps <see cref="Version"/>.
/// </summary>
public class NavigationMap2D : INavigation2D
{
    public const int DefaultMaxSearchCells = WorldDefinition.DefaultMaxSearchCells;
    public const int DefaultNearestCellSearchRadius = WorldDefinition.DefaultNearestCellSearchRadius;

    internal const int DefaultTraversalCost = 10;
    internal const int BlockedCost = int.MaxValue / 4;

    private static readonly Vector2Int[] SideSteps = { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

    private readonly NavigationPagePool pool;
    private readonly int originX;
    private readonly int originY;
    private readonly int pageWidth;
    private readonly int pageHeight;
    private readonly int columns;
    private readonly int rows;
    private readonly bool singleArea;
    private readonly NavigationPage[] pages;
    private readonly List<NavigationPage> loaded = new();
    private readonly GridAStarPathfinder2D pathfinder;
    private int raisedPages;
    private int maxSearchCells = DefaultMaxSearchCells;

    /// <summary>
    /// A map whose pages are <paramref name="pageSize"/> cells each, <paramref name="pageCount"/> of them from the cell
    /// <paramref name="origin"/>, placed in <paramref name="pagePool"/>. A <paramref name="singleArea"/> map holds one
    /// page and reads its tilemaps outside it, as an area grid always has; other maps know nothing outside their pages,
    /// and their partial searches toward a goal where no page is loaded head for it anyway.
    /// </summary>
    internal NavigationMap2D(NavigationPagePool pagePool, Vector2Int origin, Vector2Int pageSize, Vector2Int pageCount, bool singleArea)
    {
        pool = pagePool;
        originX = origin.x;
        originY = origin.y;
        pageWidth = Mathf.Max(0, pageSize.x);
        pageHeight = Mathf.Max(0, pageSize.y);
        columns = Mathf.Max(1, pageCount.x);
        rows = Mathf.Max(1, pageCount.y);
        this.singleArea = singleArea;
        pages = new NavigationPage[columns * rows];
        pathfinder = new GridAStarPathfinder2D(this);
    }

    public bool IsBuilt => loaded.Count > 0;
    public int Version { get; private set; }
    public IPathfinder2D Pathfinder => pathfinder;
    public int PageCount => loaded.Count;

    /// <summary>
    /// Upper bound on cells a partial A* search toward an unreachable goal expands before returning the closest cell
    /// found so far.
    /// </summary>
    public int MaxSearchCells
    {
        get => maxSearchCells;
        set => maxSearchCells = Mathf.Max(1, value);
    }

    /// <summary>
    /// How far the nearest-walkable-cell search looks when not given a radius.
    /// </summary>
    public int NearestCellSearchRadius { get; set; } = DefaultNearestCellSearchRadius;

    internal NavigationPagePool Pool => pool;
    internal bool HasCliffSides => raisedPages > 0;
    internal bool SearchesTowardMissingPages => !singleArea;
    internal IReadOnlyList<NavigationPage> LoadedPages => loaded;

    /// <summary>
    /// The cell containing <paramref name="worldPosition"/> on a loaded page's tilemaps, or the zero cell with none.
    /// </summary>
    public Vector3Int WorldToCell(Vector3 worldPosition)
    {
        Tilemap layout = Layout();
        return layout != null ? layout.WorldToCell(worldPosition) : Vector3Int.zero;
    }

    /// <summary>
    /// The world-space centre of <paramref name="cell"/> on a loaded page's tilemaps, or the world origin with none.
    /// </summary>
    public Vector3 CellToWorldCenter(Vector3Int cell)
    {
        Tilemap layout = Layout();
        return layout != null ? layout.GetCellCenterWorld(cell) : Vector3.zero;
    }

    /// <summary>
    /// Whether <paramref name="cell"/> is on a loaded page and walkable for <paramref name="movementProfile"/> (null =
    /// every terrain walkable).
    /// </summary>
    public bool IsCellWalkable(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        return TryGetIndex(cell, out int index) && GetCostField(movementProfile).Costs[index] > 0;
    }

    /// <summary>
    /// Finds the cell walkable for <paramref name="movementProfile"/> nearest to <paramref name="origin"/>, searching
    /// outward in rings of growing Manhattan distance up to <paramref name="maxRadius"/> (the map's
    /// <see cref="NearestCellSearchRadius"/> when not positive). False, with <paramref name="nearest"/> left at the
    /// origin, when none is found.
    /// </summary>
    public bool TryGetNearestWalkableCell(Vector3Int origin, TerrainMovementProfile2D movementProfile, out Vector3Int nearest, int maxRadius = -1)
    {
        NavigationCostField field = GetCostField(movementProfile);
        if (IsWalkable(field, origin))
        {
            nearest = origin;
            return true;
        }

        int radiusLimit = maxRadius > 0 ? maxRadius : Mathf.Max(1, NearestCellSearchRadius);
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
    /// The cost of stepping from <paramref name="from"/> into the adjacent <paramref name="to"/> for
    /// <paramref name="movementProfile"/>: the target's diagonal cost for a diagonal step, its straight cost otherwise,
    /// or a prohibitive cost when it is blocked or the step crosses the cliff.
    /// </summary>
    public int MovementCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        return MovementCost(GetCostField(movementProfile), from, to);
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
    /// Whether both cells are walkable for <paramref name="movementProfile"/> and in joined regions, in constant time.
    /// False for cells where no page is loaded.
    /// </summary>
    public bool AreCellsConnected(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile)
    {
        if (!IsBuilt || !TryGetIndex(from, out int fromIndex) || !TryGetIndex(to, out int toIndex))
        {
            return false;
        }

        return GetCostField(movementProfile).AreConnected(fromIndex, toIndex);
    }

    /// <summary>
    /// Whether the region of <paramref name="cell"/> for <paramref name="movementProfile"/>, joined across the loaded
    /// pages, runs on beyond them: it holds a cell on a loaded page's border, with no cliff across it, whose neighbour
    /// across it lies on a page the map could load but has not (<see cref="CouldLoadPage"/>). False for a cell that is
    /// blocked or where no page is loaded. A region that does not run on lies wholly in the loaded pages, so what it
    /// connects to is known: a pocket (an islet, a walled yard) holds every cell it reaches.
    /// </summary>
    public bool RunsBeyondLoadedPages(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        if (!IsBuilt || !TryGetIndex(cell, out int index))
        {
            return false;
        }

        NavigationCostField field = GetCostField(movementProfile);
        int region = field.Regions[index];
        if (region < 0)
        {
            return false;
        }

        int root = field.Roots[region];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                NavigationPage page = pages[(row * columns) + column];
                if (page == null)
                {
                    continue;
                }

                if ((IsUnloadedPage(column - 1, row) && BorderRunsOut(field, page, root, true, 0, NavigationGrid2D.LeftSide))
                    || (IsUnloadedPage(column + 1, row) && BorderRunsOut(field, page, root, true, page.Width - 1, NavigationGrid2D.RightSide))
                    || (IsUnloadedPage(column, row - 1) && BorderRunsOut(field, page, root, false, 0, NavigationGrid2D.DownSide))
                    || (IsUnloadedPage(column, row + 1) && BorderRunsOut(field, page, root, false, page.Height - 1, NavigationGrid2D.UpSide)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the map could load a page at <paramref name="column"/>, <paramref name="row"/> of its page grid: every
    /// place on it here; a world's navigation only its world's chunks.
    /// </summary>
    protected virtual bool CouldLoadPage(int column, int row)
    {
        return true;
    }

    /// <summary>
    /// Whether <paramref name="column"/>, <paramref name="row"/> lies on the page grid, holds no loaded page and could
    /// hold one (<see cref="CouldLoadPage"/>).
    /// </summary>
    private bool IsUnloadedPage(int column, int row)
    {
        return column >= 0 && row >= 0 && column < columns && row < rows && pages[(row * columns) + column] == null
            && CouldLoadPage(column, row);
    }

    /// <summary>
    /// Whether a cell of <paramref name="page"/>'s border column (<paramref name="vertical"/>) or row at
    /// <paramref name="offset"/> belongs to the region rooted at <paramref name="root"/> with no cliff on its
    /// <paramref name="side"/>.
    /// </summary>
    private bool BorderRunsOut(NavigationCostField field, NavigationPage page, int root, bool vertical, int offset, byte side)
    {
        int count = vertical ? page.Height : page.Width;
        byte[] cliffSides = pool.CliffSides;
        for (int i = 0; i < count; i++)
        {
            int index = page.Base + (vertical ? (i * page.Width) + offset : (offset * page.Width) + i);
            int region = field.Regions[index];
            if (region >= 0 && field.Roots[region] == root && (cliffSides[index] & side) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bakes the cost field for <paramref name="movementProfile"/> now so the first path query does not pay for it.
    /// </summary>
    public void PrewarmRegions(TerrainMovementProfile2D movementProfile)
    {
        if (IsBuilt)
        {
            GetCostField(movementProfile);
        }
    }

    /// <summary>
    /// Walks the Bresenham line from <paramref name="from"/> to <paramref name="to"/> and sums its step costs into
    /// <paramref name="cost"/>. False when either end or any step is blocked, including a diagonal step that would cut
    /// a blocked corner or a step across the cliff.
    /// </summary>
    public bool TryGetLineCost(Vector3Int from, Vector3Int to, TerrainMovementProfile2D movementProfile, out int cost)
    {
        cost = 0;
        NavigationCostField field = GetCostField(movementProfile);
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
    /// Whether the Bresenham line from <paramref name="from"/> to <paramref name="to"/> crosses the cliff of raised
    /// ground, a diagonal step counting when either way round its corner does; steps with an end where no page is
    /// loaded cross nothing. For movement off the paths, such as crowd separation.
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
    /// <paramref name="point"/> moved off the cliff face drawn inside raised ground's bottom cells
    /// (<see cref="RaisedGround.OffTheFace{TKinds}"/>), reading raised ground across page borders; unchanged where the
    /// point's page has no raised ground or no face.
    /// </summary>
    public Vector2 MoveOffCliffFace(Vector2 point)
    {
        Tilemap layout = Layout();
        if (layout == null)
        {
            return point;
        }

        Vector3Int cell = layout.WorldToCell(point);
        NavigationPage page = ConfigPageOf(cell);
        return page != null && page.Raised != null && page.FaceHeight > 0f
            ? RaisedGround.OffTheFace(new Kinds(this), page.Raised, point, page.FaceHeight)
            : point;
    }

    /// <summary>
    /// What keeps <paramref name="movementProfile"/> off <paramref name="cell"/>, or
    /// <see cref="NavigationGrid2D.Blocker.None"/> when it can walk there. A cell without terrain, or where no page is
    /// loaded, is <see cref="NavigationGrid2D.Blocker.NoGround"/> unless a wall or an obstacle is on it or the cliff face
    /// covers it.
    /// </summary>
    internal NavigationGrid2D.Blocker GetBlocker(Vector3Int cell, TerrainMovementProfile2D movementProfile)
    {
        if (TryLocate(cell, out NavigationPage page, out int local) && page.Terrain[local] != 0)
        {
            return GetCostField(movementProfile).Costs[page.Base + local] > 0 ? NavigationGrid2D.Blocker.None : NavigationGrid2D.Blocker.Terrain;
        }

        NavigationPage configPage = ConfigPageOf(cell);
        if (configPage == null)
        {
            return NavigationGrid2D.Blocker.NoGround;
        }

        NavigationGrid2D.Blocker blocker = BlockerOf(configPage, cell);
        return blocker != NavigationGrid2D.Blocker.None ? blocker : NavigationGrid2D.Blocker.NoGround;
    }

    /// <summary>
    /// The sides of <paramref name="cell"/> the cliff runs along; none where no page is loaded.
    /// </summary>
    internal byte GetCliffSides(Vector3Int cell)
    {
        return TryGetIndex(cell, out int index) ? pool.CliffSides[index] : (byte)0;
    }

    /// <summary>
    /// The pool index of <paramref name="cell"/>, or false where no page is loaded.
    /// </summary>
    internal bool TryGetIndex(Vector3Int cell, out int index)
    {
        if (TryLocate(cell, out NavigationPage page, out int local))
        {
            index = page.Base + local;
            return true;
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// The cell at pool index <paramref name="index"/>; the inverse of <see cref="TryGetIndex"/>.
    /// </summary>
    internal Vector3Int GetCell(int index)
    {
        NavigationPage page = pool.Slots[index / pool.SlotSize];
        return page.CellAt(index - page.Base);
    }

    /// <summary>
    /// The loaded page holding <paramref name="cell"/> and its local index there; false where no page is.
    /// </summary>
    internal bool TryLocate(Vector3Int cell, out NavigationPage page, out int local)
    {
        return TryLocate(cell.x, cell.y, out page, out local) && cell.z == page.Z;
    }

    /// <summary>
    /// The cost field of <paramref name="movementProfile"/>, current for every page.
    /// </summary>
    internal NavigationCostField GetCostField(TerrainMovementProfile2D movementProfile)
    {
        return pool.GetCostField(movementProfile);
    }

    /// <summary>
    /// Whether a page is loaded at <paramref name="cell"/>'s place on the page grid.
    /// </summary>
    internal bool HasPageAt(Vector3Int cell)
    {
        return TryLocate(cell.x, cell.y, out _, out _);
    }

    /// <summary>
    /// Places <paramref name="page"/> in the pool and on the page grid, links it with the loaded pages around it, reads
    /// its cells, and reads again what its neighbours read across their shared borders: their cliff sides there, and the
    /// rows a cliff face covers above it.
    /// </summary>
    internal void AddPage(NavigationPage page)
    {
        int column = PageColumn(page.Rect.xMin);
        int row = PageRow(page.Rect.yMin);
        if (column < 0 || row < 0 || column >= columns || row >= rows || pages[(row * columns) + column] != null)
        {
            throw new System.ArgumentException($"No free place on the page grid for a page at {page.Rect}.", nameof(page));
        }

        pool.Acquire(page);
        page.Map = this;
        pages[(row * columns) + column] = page;
        loaded.Add(page);
        if (page.Raised != null)
        {
            raisedPages++;
        }

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                NavigationPage neighbour = PageAt(column + dx, row + dy);
                page.Around[((dy + 1) * 3) + dx + 1] = neighbour;
                if (neighbour != null)
                {
                    neighbour.Around[((1 - dy) * 3) + 1 - dx] = page;
                }
            }
        }

        ReadCells(page);
        ReadCliffSides(page, false);
        for (int i = 0; i < page.Around.Length; i++)
        {
            NavigationPage neighbour = page.Around[i];
            if (neighbour != null && neighbour != page)
            {
                RereadBorder(neighbour, i == 7);
            }
        }

        pool.Touch(page);
        Version++;
    }

    /// <summary>
    /// Takes <paramref name="page"/> off the page grid and out of the pool, and reads again what its neighbours read
    /// across their shared borders, where nothing is now.
    /// </summary>
    internal void RemovePage(NavigationPage page)
    {
        if (page == null || page.Map != this)
        {
            return;
        }

        int column = PageColumn(page.Rect.xMin);
        int row = PageRow(page.Rect.yMin);
        pages[(row * columns) + column] = null;
        loaded.Remove(page);
        if (page.Raised != null)
        {
            raisedPages--;
        }

        for (int i = 0; i < page.Around.Length; i++)
        {
            NavigationPage neighbour = page.Around[i];
            if (neighbour == null || neighbour == page)
            {
                continue;
            }

            neighbour.Around[8 - i] = null;
            page.Around[i] = null;
            RereadBorder(neighbour, i == 7);
        }

        pool.Release(page);
        page.Map = null;
        Version++;
    }

    /// <summary>
    /// Re-reads <paramref name="cell"/> into the page holding it. False when this is an area map, the cell lies outside
    /// its page and it now has terrain, which needs a full rebuild.
    /// </summary>
    internal bool RefreshCell(Vector3Int cell)
    {
        if (TryLocate(cell, out NavigationPage page, out int local))
        {
            page.Terrain[local] = ReadCell(page, cell);
            TouchAround(page, local);
            Version++;
            return true;
        }

        NavigationPage areaPage = singleArea && loaded.Count > 0 ? loaded[0] : null;
        return areaPage == null || ReadCell(areaPage, cell) == 0;
    }

    /// <summary>
    /// Re-reads <paramref name="cell"/> of <paramref name="page"/>'s raised tilemap after it changed: its kind, the cliff
    /// sides of it and its 4 neighbours, and the cells above it up to the rows the cliff face covers, whose face it
    /// decides, wherever they are. False when a cell needs a full rebuild (<see cref="RefreshCell"/>).
    /// </summary>
    internal bool RefreshRaisedCell(NavigationPage page, Vector3Int cell)
    {
        page.RefreshKind(cell);
        RefreshCliffSides(cell);
        if (page.FaceRows == 0)
        {
            return true;
        }

        for (int up = 0; up <= page.FaceRows; up++)
        {
            if (!RefreshCell(new Vector3Int(cell.x, cell.y + up, page.Z)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The kind of raised ground at <paramref name="x"/>, <paramref name="y"/>: from the page holding it, from an area's
    /// raised tilemap outside its page, else <see cref="RaisedGround.Level"/>.
    /// </summary>
    internal byte KindAt(int x, int y)
    {
        if (TryLocate(x, y, out NavigationPage page, out int local))
        {
            return page.Kinds != null ? page.Kinds[local] : RaisedGround.Level;
        }

        if (singleArea && loaded.Count > 0 && loaded[0].Raised != null)
        {
            return RaisedGround.KindAt(loaded[0].Raised, new Vector3Int(x, y, 0));
        }

        return RaisedGround.Level;
    }

    /// <summary>
    /// The octile cost of a move of <paramref name="dx"/> by <paramref name="dy"/> cells taking as many diagonal steps as
    /// possible, then straight ones.
    /// </summary>
    internal static int OctileCost(int dx, int dy, int straightCost, int diagonalCost)
    {
        int diagonal = Mathf.Min(dx, dy);
        int straight = Mathf.Max(dx, dy) - diagonal;
        return (diagonalCost * diagonal) + (straightCost * straight);
    }

    /// <summary>
    /// The cost of entering a cell diagonally: its straight cost times 1.4, rounded, and at least 1.
    /// </summary>
    internal static int ScaleDiagonalCost(int traversalCost)
    {
        return Mathf.Max(1, Mathf.RoundToInt(traversalCost * 1.4f));
    }

    /// <summary>
    /// The side of a cell facing the neighbour (<paramref name="dx"/>, <paramref name="dy"/>) away along one axis.
    /// </summary>
    internal static byte SideToward(int dx, int dy)
    {
        return dx > 0 ? NavigationGrid2D.RightSide : dx < 0 ? NavigationGrid2D.LeftSide : dy > 0 ? NavigationGrid2D.UpSide : NavigationGrid2D.DownSide;
    }

    /// <summary>
    /// The loaded page holding <paramref name="x"/>, <paramref name="y"/> (any z) and its local index there.
    /// </summary>
    private bool TryLocate(int x, int y, out NavigationPage page, out int local)
    {
        int rx = x - originX;
        int ry = y - originY;
        page = null;
        local = -1;
        if (rx < 0 || ry < 0)
        {
            return false;
        }

        int column = 0;
        int row = 0;
        if (columns > 1 || rows > 1)
        {
            column = rx / pageWidth;
            row = ry / pageHeight;
            if (column >= columns || row >= rows)
            {
                return false;
            }

            rx -= column * pageWidth;
            ry -= row * pageHeight;
        }
        else if (rx >= pageWidth || ry >= pageHeight)
        {
            return false;
        }

        page = pages[(row * columns) + column];
        if (page == null)
        {
            return false;
        }

        local = (ry * page.Width) + rx;
        return true;
    }

    /// <summary>
    /// The page whose tilemaps tell what is at <paramref name="cell"/>: the loaded page holding it, or an area's page for
    /// a cell outside it; null where no page is loaded.
    /// </summary>
    private NavigationPage ConfigPageOf(Vector3Int cell)
    {
        if (TryLocate(cell.x, cell.y, out NavigationPage page, out _))
        {
            return page;
        }

        return singleArea && loaded.Count > 0 ? loaded[0] : null;
    }

    private NavigationPage PageAt(int column, int row)
    {
        return column >= 0 && row >= 0 && column < columns && row < rows ? pages[(row * columns) + column] : null;
    }

    private int PageColumn(int cellX)
    {
        return pageWidth > 0 ? FloorDivide(cellX - originX, pageWidth) : 0;
    }

    private int PageRow(int cellY)
    {
        return pageHeight > 0 ? FloorDivide(cellY - originY, pageHeight) : 0;
    }

    private static int FloorDivide(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor != 0 && value < 0 ? quotient - 1 : quotient;
    }

    /// <summary>
    /// The tilemap world/cell conversions use: the first loaded page's reference tilemap.
    /// </summary>
    private Tilemap Layout()
    {
        return loaded.Count > 0 ? loaded[0].Reference : null;
    }

    /// <summary>
    /// Reads every cell of <paramref name="page"/>: its terrain in blocks, minus walls and obstacles, then the cells the
    /// cliff face covers whole, whose foot may lie on the page below.
    /// </summary>
    private void ReadCells(NavigationPage page)
    {
        page.ReadTerrainBlock();
        if (page.FaceRows == 0 || page.Kinds == null)
        {
            return;
        }

        Kinds kinds = new(this);
        for (int local = 0; local < page.CellCount; local++)
        {
            if (page.Terrain[local] != 0 && page.Kinds[local] != RaisedGround.Level
                && RaisedGround.IsOnFace(kinds, page.CellAt(local), page.FaceRows))
            {
                page.Terrain[local] = 0;
            }
        }
    }

    /// <summary>
    /// The terrain bits of <paramref name="cell"/> of <paramref name="page"/>, or 0 when something blocks it.
    /// </summary>
    private ulong ReadCell(NavigationPage page, Vector3Int cell)
    {
        return BlockerOf(page, cell) != NavigationGrid2D.Blocker.None ? 0 : page.ReadTerrain(cell);
    }

    /// <summary>
    /// What blocks <paramref name="cell"/> for every terrain layer, in this order: an obstacle collider, a tile on a
    /// blocking tilemap, the cliff face covering it whole; <see cref="NavigationGrid2D.Blocker.None"/> otherwise.
    /// </summary>
    private NavigationGrid2D.Blocker BlockerOf(NavigationPage page, Vector3Int cell)
    {
        NavigationGrid2D.Blocker blocker = page.TileBlockerOf(cell);
        if (blocker != NavigationGrid2D.Blocker.None)
        {
            return blocker;
        }

        return page.FaceRows > 0 && page.Raised != null && RaisedGround.IsOnFace(new Kinds(this), cell, page.FaceRows)
            ? NavigationGrid2D.Blocker.CliffFace
            : NavigationGrid2D.Blocker.None;
    }

    /// <summary>
    /// After a neighbouring page loaded or unloaded: reads again <paramref name="page"/>'s cliff sides along its borders
    /// and, when the change was the page below it (<paramref name="changedBelow"/>), its rows the cliff face may cover,
    /// then gives it a new stamp, since its edge costs read across the border too.
    /// </summary>
    private void RereadBorder(NavigationPage page, bool changedBelow)
    {
        ReadCliffSides(page, true);
        if (changedBelow && page.FaceRows > 0)
        {
            int rowsToRead = Mathf.Min(page.FaceRows, page.Height);
            for (int local = 0; local < rowsToRead * page.Width; local++)
            {
                page.Terrain[local] = ReadCell(page, page.CellAt(local));
            }
        }

        pool.Touch(page);
    }

    /// <summary>
    /// Reads the cliff sides of <paramref name="page"/>'s cells, only those along its borders when
    /// <paramref name="borderOnly"/>; all none while no page of the map has raised ground.
    /// </summary>
    private void ReadCliffSides(NavigationPage page, bool borderOnly)
    {
        byte[] cliffSides = pool.CliffSides;
        int width = page.Width;
        int height = page.Height;
        for (int y = 0; y < height; y++)
        {
            bool borderRow = y == 0 || y == height - 1;
            for (int x = 0; x < width; x++)
            {
                if (borderOnly && !borderRow && x != 0 && x != width - 1)
                {
                    continue;
                }

                cliffSides[page.Base + (y * width) + x] = HasCliffSides ? ReadCliffSides(page.Rect.xMin + x, page.Rect.yMin + y) : (byte)0;
            }
        }
    }

    /// <summary>
    /// Re-reads the cliff sides of <paramref name="changed"/> and its 4 neighbours, in whichever pages hold them.
    /// </summary>
    private void RefreshCliffSides(Vector3Int changed)
    {
        UpdateCliffSides(changed.x, changed.y);
        for (int side = 0; side < SideSteps.Length; side++)
        {
            UpdateCliffSides(changed.x + SideSteps[side].x, changed.y + SideSteps[side].y);
        }
    }

    private void UpdateCliffSides(int x, int y)
    {
        if (TryLocate(x, y, out NavigationPage page, out int local))
        {
            pool.CliffSides[page.Base + local] = HasCliffSides ? ReadCliffSides(x, y) : (byte)0;
            TouchAround(page, local);
        }
    }

    /// <summary>
    /// The sides of the cell at <paramref name="x"/>, <paramref name="y"/> a step across which
    /// <see cref="RaisedGround.CanCross"/> refuses, as side bits.
    /// </summary>
    private byte ReadCliffSides(int x, int y)
    {
        byte kind = KindAt(x, y);
        byte sides = 0;
        for (int side = 0; side < SideSteps.Length; side++)
        {
            Vector2Int step = SideSteps[side];
            if (!RaisedGround.CanCross(kind, KindAt(x + step.x, y + step.y), new Vector3Int(step.x, step.y, 0)))
            {
                sides |= (byte)(1 << side);
            }
        }

        return sides;
    }

    /// <summary>
    /// Gives <paramref name="page"/> a new stamp after its cell at <paramref name="local"/> changed, and its neighbours
    /// too when that cell is on its border, since their edge costs read it.
    /// </summary>
    private void TouchAround(NavigationPage page, int local)
    {
        pool.Touch(page);
        int x = local % page.Width;
        int y = local / page.Width;
        if (x > 0 && y > 0 && x < page.Width - 1 && y < page.Height - 1)
        {
            return;
        }

        for (int i = 0; i < page.Around.Length; i++)
        {
            if (page.Around[i] != null && page.Around[i] != page)
            {
                pool.Touch(page.Around[i]);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="cell"/> is on a loaded page and has a nonzero cost in <paramref name="field"/>.
    /// </summary>
    private bool IsWalkable(NavigationCostField field, Vector3Int cell)
    {
        return TryGetIndex(cell, out int index) && field.Costs[index] > 0;
    }

    /// <summary>
    /// Whether one step from <paramref name="from"/> to <paramref name="to"/> is allowed in <paramref name="field"/>:
    /// the cells are adjacent, the target is walkable, a diagonal step does not cut a blocked corner, and the step does
    /// not cross the cliff.
    /// </summary>
    private bool CanTraverse(NavigationCostField field, Vector3Int from, Vector3Int to)
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
    /// cliff: a straight step across a cliff side, a diagonal one when either way round its corner does. False for
    /// cells that are not neighbours or where no page is loaded.
    /// </summary>
    private bool StepCrossesCliff(Vector3Int from, Vector3Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        if ((dx == 0 && dy == 0) || Mathf.Abs(dx) > 1 || Mathf.Abs(dy) > 1 || !TryGetIndex(to, out _) || !TryGetIndex(from, out int index))
        {
            return false;
        }

        byte[] cliffSides = pool.CliffSides;
        if (dx == 0 || dy == 0)
        {
            return (cliffSides[index] & SideToward(dx, dy)) != 0;
        }

        byte horizontal = SideToward(dx, 0);
        byte vertical = SideToward(0, dy);
        return (cliffSides[index] & (horizontal | vertical)) != 0
            || (GetCliffSides(new Vector3Int(from.x + dx, from.y, from.z)) & vertical) != 0
            || (GetCliffSides(new Vector3Int(from.x, from.y + dy, from.z)) & horizontal) != 0;
    }

    /// <summary>
    /// The cost of entering <paramref name="to"/> from <paramref name="from"/> in <paramref name="field"/>: its diagonal
    /// cost for a diagonal step, its straight cost otherwise, or a prohibitive cost when it is blocked or the step
    /// crosses the cliff.
    /// </summary>
    private int MovementCost(NavigationCostField field, Vector3Int from, Vector3Int to)
    {
        if (!TryGetIndex(to, out int index) || field.Costs[index] == 0 || StepCrossesCliff(from, to))
        {
            return BlockedCost;
        }

        bool isDiagonal = Mathf.Abs(to.x - from.x) == 1 && Mathf.Abs(to.y - from.y) == 1;
        return isDiagonal ? field.DiagonalCosts[index] : field.Costs[index];
    }

    /// <summary>
    /// The kinds of raised ground across a map's pages, for the cliff face rules.
    /// </summary>
    private readonly struct Kinds : RaisedGround.IKinds
    {
        private readonly NavigationMap2D map;

        /// <summary>
        /// Reads the kinds of <paramref name="navigationMap"/>.
        /// </summary>
        public Kinds(NavigationMap2D navigationMap)
        {
            map = navigationMap;
        }

        /// <summary>
        /// The kind of <paramref name="cell"/> (<see cref="NavigationMap2D.KindAt"/>).
        /// </summary>
        public byte KindAt(Vector3Int cell)
        {
            return map.KindAt(cell.x, cell.y);
        }
    }
}
