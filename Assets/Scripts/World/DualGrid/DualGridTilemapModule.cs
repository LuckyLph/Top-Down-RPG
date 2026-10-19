using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// Which tilemap of a <see cref="DualGridTilemapModule"/> instantiates the tiles' GameObjects: the data tilemap,
/// the render tilemap, or neither.
/// </summary>
public enum GameObjectOrigin { None, DataTilemap, RenderTilemap }

/// <summary>
/// A module to be added that converts a <see cref="Tilemap"/> into a Dual Grid Tilemap.
/// </summary>
/// <remarks>
/// This module only guarantees the continuous update of the render tilemap, for data tilemap changes. Every change
/// refreshes each affected render tile once; batches go through <see cref="SetDataTiles(IReadOnlyList{Vector3Int})"/>
/// and <see cref="ClearDataTiles(IReadOnlyList{Vector3Int})"/>, which write the data and render tilemaps in bulk.
/// <para></para>
/// On a chunk of a seamless world (<see cref="SetChunk"/>) it draws the render tiles on its own cells, reading the
/// data cells of its loaded neighbours' modules across the border, and the render tiles along its upper and right
/// border only while the chunk owning them is not loaded; it follows its neighbours' data changes along the border.
/// <para></para>
/// Editor logic is kept in the associated editor script.
/// </remarks>
[RequireComponent(typeof(Tilemap))]
[DisallowMultipleComponent]
public class DualGridTilemapModule : MonoBehaviour
{
    [SerializeField, FormerlySerializedAs("_renderTile")] private DualGridRuleTile renderTile;
    public DualGridRuleTile RenderTile { get => renderTile; internal set => renderTile = value; }

    public DualGridDataTile DataTile => RenderTile.DataTile;

    [SerializeField, FormerlySerializedAs("_enableTilemapCollider")] private bool enableTilemapCollider;
    public bool EnableTilemapCollider { get => enableTilemapCollider; internal set => enableTilemapCollider = value; }

    [SerializeField, FormerlySerializedAs("_gameObjectOrigin")] private GameObjectOrigin gameObjectOrigin = GameObjectOrigin.None;
    public GameObjectOrigin GameObjectOrigin { get => gameObjectOrigin; internal set => gameObjectOrigin = value; }

    private Tilemap dataTilemap;
    public Tilemap DataTilemap
    {
        get
        {
            if (dataTilemap == null) dataTilemap = GetComponent<Tilemap>();
            return dataTilemap;
        }
    }

    private const int BulkRenderWriteThreshold = 16;

    private readonly HashSet<Vector3Int> pendingRenderTiles = new();
    private readonly List<Vector3Int> renderTilesToSet = new();
    private readonly List<Vector3Int> renderTilesToClear = new();
    [NonSerialized] private readonly List<DualGridTilemapModule> neighbours = new();
    private bool isWritingDataTiles;
    [NonSerialized] private bool hasChunk;
    [NonSerialized] private RectInt chunkCells;

    /// <summary>
    /// Whether it draws the walls of a chunk of a seamless world (<see cref="SetChunk"/>).
    /// </summary>
    public bool HasChunk => hasChunk;

    /// <summary>
    /// The data cells of its chunk, when <see cref="HasChunk"/>.
    /// </summary>
    public RectInt ChunkCells => chunkCells;
    internal IReadOnlyList<DualGridTilemapModule> Neighbours => neighbours;

    private Tilemap renderTilemap;
    public Tilemap RenderTilemap
    {
        get
        {
            if (renderTilemap == null) renderTilemap = transform.GetComponentInImmediateChildren<Tilemap>();
            return renderTilemap;
        }
    }

    /// <summary>
    /// Caches the data tilemap on this object and the render tilemap on its first child that has one.
    /// </summary>
    private void Awake()
    {
        if (dataTilemap == null) dataTilemap = GetComponent<Tilemap>();
        if (renderTilemap == null) renderTilemap = transform.GetComponentInImmediateChildren<Tilemap>();
    }

    /// <summary>
    /// Starts following tile changes made to the data tilemap from outside this module.
    /// </summary>
    private void OnEnable()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
    }

    /// <summary>
    /// Stops following tilemap changes.
    /// </summary>
    private void OnDisable()
    {
        Tilemap.tilemapPositionsChanged -= HandleTilemapPositionsChanged;
    }

    /// <summary>
    /// Updates the render tiles around every <see cref="DataTilemap"/> cell changed by something other than this
    /// module (a brush, or code writing the tilemap directly).
    /// </summary>
    internal void HandleTilemapPositionsChanged(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        if (tilemap != DataTilemap && ReadsNeighbourData(tilemap))
        {
            HandleNeighbourDataChanged(positions);
            return;
        }

        if (tilemap != DataTilemap || isWritingDataTiles)
        {
            return;
        }

        if (RenderTile == null)
        {
            Debug.LogError("Cannot update render tilemap, because tile is not set in dual grid module.", RenderTilemap);
            return;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            QueueRenderTiles(positions[i]);
        }

        ApplyPendingRenderTiles();
    }

    /// <summary>
    /// Fills the data cell at <paramref name="position"/> and refreshes the 4 render tiles around it.
    /// </summary>
    public void SetDataTile(Vector3Int position)
    {
        WriteDataTile(position, DataTile);
    }

    /// <summary>
    /// Empties the data cell at <paramref name="position"/> and refreshes the 4 render tiles around it.
    /// </summary>
    public void ClearDataTile(Vector3Int position)
    {
        WriteDataTile(position, null);
    }

    /// <summary>
    /// Fills every cell in <paramref name="positions"/> with one data tilemap write, then refreshes each affected render
    /// tile once.
    /// </summary>
    public void SetDataTiles(IReadOnlyList<Vector3Int> positions)
    {
        WriteDataTiles(positions, DataTile);
    }

    /// <summary>
    /// Empties every cell in <paramref name="positions"/> with one data tilemap write, then refreshes each affected
    /// render tile once.
    /// </summary>
    public void ClearDataTiles(IReadOnlyList<Vector3Int> positions)
    {
        WriteDataTiles(positions, null);
    }

    /// <summary>
    /// Fills every cell in <paramref name="bounds"/> in one batch, like
    /// <see cref="SetDataTiles(IReadOnlyList{Vector3Int})"/>.
    /// </summary>
    public void SetDataTiles(BoundsInt bounds)
    {
        WriteDataTiles(PositionsIn(bounds), DataTile);
    }

    /// <summary>
    /// Empties every cell in <paramref name="bounds"/> in one batch, like
    /// <see cref="ClearDataTiles(IReadOnlyList{Vector3Int})"/>.
    /// </summary>
    public void ClearDataTiles(BoundsInt bounds)
    {
        WriteDataTiles(PositionsIn(bounds), null);
    }

    /// <summary>
    /// Fully refreshes the <see cref="RenderTilemap"/> by forcing an update from all tiles in the <see cref="DataTilemap"/>.
    /// </summary>
    public void RefreshRenderTilemap()
    {
        if (RenderTile == null)
        {
            Debug.LogError("Cannot refresh render tilemap, because tile is not set in dual grid module.", RenderTilemap);
            return;
        }

        List<Vector3Int> filled = new();
        foreach (Vector3Int position in DataTilemap.cellBounds.allPositionsWithin)
        {
            if (DataTilemap.HasTile(position))
            {
                filled.Add(position);
            }
        }

        RenderTilemap.ClearAllTiles();
        WriteDataTiles(filled, DataTile);
    }

    /// <summary>
    /// Makes it draw the walls of the chunk owning the data cells <paramref name="cells"/>, beside
    /// <paramref name="neighbourModules"/>, the wall modules of its loaded neighbouring chunks (each with its own
    /// chunk set): it reads their data cells across the border, leaves them the render tiles on their cells, and draws
    /// those along its upper and right border only where no neighbour owns them. Refreshes the render tiles along its
    /// border. Not saved.
    /// </summary>
    internal void SetChunk(RectInt cells, IReadOnlyList<DualGridTilemapModule> neighbourModules)
    {
        hasChunk = true;
        chunkCells = cells;
        neighbours.Clear();
        for (int i = 0; neighbourModules != null && i < neighbourModules.Count; i++)
        {
            if (neighbourModules[i] != null && neighbourModules[i] != this)
            {
                neighbours.Add(neighbourModules[i]);
            }
        }

        if (RenderTile == null || RenderTilemap == null)
        {
            return;
        }

        for (int x = cells.xMin; x <= cells.xMax; x++)
        {
            pendingRenderTiles.Add(new Vector3Int(x, cells.yMin, 0));
            pendingRenderTiles.Add(new Vector3Int(x, cells.yMax, 0));
        }

        for (int y = cells.yMin; y <= cells.yMax; y++)
        {
            pendingRenderTiles.Add(new Vector3Int(cells.xMin, y, 0));
            pendingRenderTiles.Add(new Vector3Int(cells.xMax, y, 0));
        }

        ApplyPendingRenderTiles();
    }

    /// <summary>
    /// Whether it draws the render tile at <paramref name="renderTilePosition"/>: always without a chunk; on a chunk,
    /// a tile on its own cells, or one along its upper or right border that no neighbour's cells hold.
    /// </summary>
    public bool OwnsRenderTile(Vector3Int renderTilePosition)
    {
        if (!hasChunk || Contains(chunkCells, renderTilePosition))
        {
            return true;
        }

        if (renderTilePosition.x < chunkCells.xMin || renderTilePosition.x > chunkCells.xMax
            || renderTilePosition.y < chunkCells.yMin || renderTilePosition.y > chunkCells.yMax)
        {
            return false;
        }

        return NeighbourHolding(renderTilePosition) == null;
    }

    /// <summary>
    /// The tile its rules read at the data cell <paramref name="dataTilePosition"/>: its own data tilemap's (in the
    /// editor, its preview tile first), or on a chunk, past its cells, the data tilemap's of the neighbour holding the
    /// cell; null where no loaded chunk holds it.
    /// </summary>
    internal TileBase DataTileForRules(Vector3Int dataTilePosition)
    {
        Tilemap source = DataTilemapAt(dataTilePosition);
        if (source == null)
        {
            return null;
        }

#if UNITY_EDITOR
        TileBase preview = source.GetEditorPreviewTile(dataTilePosition);
        if (preview != null)
        {
            return preview;
        }
#endif
        return source.GetTile(dataTilePosition);
    }

    /// <summary>
    /// The data tilemap holding <paramref name="dataTilePosition"/>: its own, or on a chunk, past its cells, the
    /// neighbour's whose cells hold it, or null.
    /// </summary>
    private Tilemap DataTilemapAt(Vector3Int dataTilePosition)
    {
        if (!hasChunk || Contains(chunkCells, dataTilePosition))
        {
            return DataTilemap;
        }

        DualGridTilemapModule neighbour = NeighbourHolding(dataTilePosition);
        return neighbour != null ? neighbour.DataTilemap : null;
    }

    /// <summary>
    /// The neighbour module whose chunk's cells hold <paramref name="cell"/>, or null.
    /// </summary>
    private DualGridTilemapModule NeighbourHolding(Vector3Int cell)
    {
        for (int i = 0; i < neighbours.Count; i++)
        {
            DualGridTilemapModule neighbour = neighbours[i];
            if (neighbour != null && neighbour.hasChunk && Contains(neighbour.chunkCells, cell))
            {
                return neighbour;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="tilemap"/> is the data tilemap of one of its neighbours across a chunk border.
    /// </summary>
    internal bool ReadsNeighbourData(Tilemap tilemap)
    {
        for (int i = 0; hasChunk && i < neighbours.Count; i++)
        {
            if (neighbours[i] != null && neighbours[i].DataTilemap == tilemap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refreshes its render tiles around the changed data cells of a neighbour that lie along its border.
    /// </summary>
    private void HandleNeighbourDataChanged(NativeArray<Vector3Int> positions)
    {
        if (RenderTile == null)
        {
            return;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3Int position = positions[i];
            if (position.x >= chunkCells.xMin - 1 && position.x <= chunkCells.xMax
                && position.y >= chunkCells.yMin - 1 && position.y <= chunkCells.yMax)
            {
                QueueRenderTiles(position);
            }
        }

        if (pendingRenderTiles.Count > 0)
        {
            ApplyPendingRenderTiles();
        }
    }

    /// <summary>
    /// Whether <paramref name="cell"/> lies inside <paramref name="cells"/>, ignoring z.
    /// </summary>
    private static bool Contains(RectInt cells, Vector3Int cell)
    {
        return cell.x >= cells.xMin && cell.x < cells.xMax && cell.y >= cells.yMin && cell.y < cells.yMax;
    }

    /// <summary>
    /// Refreshes the 4 render tiles around <paramref name="dataTilePosition"/> from the current data tilemap.
    /// </summary>
    public void RefreshRenderTiles(Vector3Int dataTilePosition)
    {
        QueueRenderTiles(dataTilePosition);
        ApplyPendingRenderTiles();
    }

    /// <summary>
    /// Writes <paramref name="tile"/> (null to empty) to one data cell without reacting to its own change event,
    /// then refreshes the render tiles around it.
    /// </summary>
    private void WriteDataTile(Vector3Int position, TileBase tile)
    {
        isWritingDataTiles = true;
        try
        {
            DataTilemap.SetTile(position, tile);
        }
        finally
        {
            isWritingDataTiles = false;
        }

        RefreshRenderTiles(position);
    }

    /// <summary>
    /// Writes <paramref name="tile"/> (null to empty) to every data cell in <paramref name="positions"/> in one
    /// batch without reacting to its own change event, then refreshes each affected render tile once. An empty list
    /// does nothing.
    /// </summary>
    private void WriteDataTiles(IReadOnlyList<Vector3Int> positions, TileBase tile)
    {
        if (positions.Count == 0)
        {
            return;
        }

        Vector3Int[] cells = new Vector3Int[positions.Count];
        TileBase[] tiles = new TileBase[positions.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = positions[i];
            tiles[i] = tile;
            QueueRenderTiles(cells[i]);
        }

        isWritingDataTiles = true;
        try
        {
            DataTilemap.SetTiles(cells, tiles);
        }
        finally
        {
            isWritingDataTiles = false;
        }

        ApplyPendingRenderTiles();
    }

    /// <summary>
    /// Queues the 4 render tiles around <paramref name="dataTilePosition"/> for the next
    /// <see cref="ApplyPendingRenderTiles"/>; duplicates collapse.
    /// </summary>
    private void QueueRenderTiles(Vector3Int dataTilePosition)
    {
        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            pendingRenderTiles.Add(DualGridUtils.GetRenderTilePosition(dataTilePosition, corner));
        }
    }

    /// <summary>
    /// Resolves every queued render tile: adds it when a neighboring data tile is filled and it has none, removes it
    /// when no neighbor is filled, and otherwise refreshes an existing one so its rule is re-evaluated.
    /// </summary>
    private void ApplyPendingRenderTiles()
    {
        Tilemap render = RenderTilemap;
        renderTilesToSet.Clear();
        renderTilesToClear.Clear();
        foreach (Vector3Int renderTilePosition in pendingRenderTiles)
        {
            bool isInUse = OwnsRenderTile(renderTilePosition) && IsInUseByDataTilemap(renderTilePosition);
            bool hasTile = render.HasTile(renderTilePosition);
            if (isInUse && !hasTile)
            {
                renderTilesToSet.Add(renderTilePosition);
            }
            else if (!isInUse && hasTile)
            {
                renderTilesToClear.Add(renderTilePosition);
            }
            else if (hasTile)
            {
                render.RefreshTile(renderTilePosition);
            }
        }

        pendingRenderTiles.Clear();
        WriteRenderTiles(renderTilesToSet, RenderTile);
        WriteRenderTiles(renderTilesToClear, null);
    }

    /// <summary>
    /// Writes <paramref name="tile"/> to the render tilemap at <paramref name="positions"/>: one call per tile for
    /// small sets, one bulk write above <see cref="BulkRenderWriteThreshold"/>.
    /// </summary>
    private void WriteRenderTiles(List<Vector3Int> positions, TileBase tile)
    {
        if (positions.Count <= BulkRenderWriteThreshold)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                RenderTilemap.SetTile(positions[i], tile);
            }

            return;
        }

        TileBase[] tiles = new TileBase[positions.Count];
        Array.Fill(tiles, tile);
        RenderTilemap.SetTiles(positions.ToArray(), tiles);
    }

    /// <summary>
    /// Checks if the render tile at <paramref name="renderTilePosition"/> is in use by any data tile, its neighbours'
    /// across the border of a chunk included.
    /// </summary>
    private bool IsInUseByDataTilemap(Vector3Int renderTilePosition)
    {
        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            Vector3Int dataTilePosition = DualGridUtils.GetDataTilePosition(renderTilePosition, corner);
            Tilemap source = DataTilemapAt(dataTilePosition);
            if (source != null && source.HasTile(dataTilePosition))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every cell position inside <paramref name="bounds"/>, as a new list.
    /// </summary>
    private static List<Vector3Int> PositionsIn(BoundsInt bounds)
    {
        List<Vector3Int> positions = new(bounds.size.x * bounds.size.y * bounds.size.z);
        foreach (Vector3Int position in bounds.allPositionsWithin)
        {
            positions.Add(position);
        }

        return positions;
    }
}
