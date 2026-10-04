using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

public enum GameObjectOrigin { None, DataTilemap, RenderTilemap }

/// <summary>
/// A module to be added that converts a <see cref="Tilemap"/> into a Dual Grid Tilemap.
/// </summary>
/// <remarks>
/// This module only guarantees the continuous update of the render tilemap, for data tilemap changes. Every change
/// refreshes each affected render tile once; batches go through <see cref="SetDataTiles(IReadOnlyList{Vector3Int})"/>
/// and <see cref="ClearDataTiles(IReadOnlyList{Vector3Int})"/>, which write the data and render tilemaps in bulk.
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
    private bool isWritingDataTiles;

    private Tilemap renderTilemap;
    public Tilemap RenderTilemap
    {
        get
        {
            if (renderTilemap == null) renderTilemap = transform.GetComponentInImmediateChildren<Tilemap>();
            return renderTilemap;
        }
    }

    private void Awake()
    {
        if (dataTilemap == null) dataTilemap = GetComponent<Tilemap>();
        if (renderTilemap == null) renderTilemap = transform.GetComponentInImmediateChildren<Tilemap>();
    }

    private void OnEnable()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
    }

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

    public void SetDataTile(Vector3Int position)
    {
        WriteDataTile(position, DataTile);
    }

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

    public void SetDataTiles(BoundsInt bounds)
    {
        WriteDataTiles(PositionsIn(bounds), DataTile);
    }

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
    /// Refreshes the 4 render tiles around <paramref name="dataTilePosition"/> from the current data tilemap.
    /// </summary>
    public void RefreshRenderTiles(Vector3Int dataTilePosition)
    {
        QueueRenderTiles(dataTilePosition);
        ApplyPendingRenderTiles();
    }

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

    private void QueueRenderTiles(Vector3Int dataTilePosition)
    {
        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            pendingRenderTiles.Add(DualGridUtils.GetRenderTilePosition(dataTilePosition, corner));
        }
    }

    private void ApplyPendingRenderTiles()
    {
        Tilemap render = RenderTilemap;
        renderTilesToSet.Clear();
        renderTilesToClear.Clear();
        foreach (Vector3Int renderTilePosition in pendingRenderTiles)
        {
            bool isInUse = IsInUseByDataTilemap(renderTilePosition);
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
    /// Checks if the render tile at <paramref name="renderTilePosition"/> is in use by any data tile.
    /// </summary>
    private bool IsInUseByDataTilemap(Vector3Int renderTilePosition)
    {
        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            if (DataTilemap.HasTile(DualGridUtils.GetDataTilePosition(renderTilePosition, corner)))
            {
                return true;
            }
        }

        return false;
    }

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
