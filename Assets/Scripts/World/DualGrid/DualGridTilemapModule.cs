using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

public enum GameObjectOrigin { None, DataTilemap, RenderTilemap }

/// <summary>
/// A module to be added that converts a <see cref="Tilemap"/> into a Dual Grid Tilemap.
/// </summary>
/// <remarks>
/// This module only guarantees the continuous update of the render tilemap, for data tilemap changes.
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

#if UNITY_EDITOR || UNITY_2022_1_OR_NEWER
    private void OnEnable()
    {
        Tilemap.tilemapTileChanged += HandleTilemapChange;
    }

    private void OnDisable()
    {
        Tilemap.tilemapTileChanged -= HandleTilemapChange;
    }

    /// <summary>
    /// For each updated tile in the <see cref="DataTilemap"/>, update the <see cref="RenderTilemap"/>.
    /// </summary>
    internal void HandleTilemapChange(Tilemap tilemap, Tilemap.SyncTile[] tileChanges)
    {
        if (tilemap == DataTilemap)
        {
            if (RenderTile == null)
            {
                Debug.LogError($"Cannot update render tilemap, because tile is not set in dual grid module.", RenderTilemap);
                return;
            }

            foreach (Tilemap.SyncTile tileChange in tileChanges)
            {
                RefreshRenderTiles(tileChange.position);
            }
        }
    }

#endif
    public void SetDataTile(Vector3Int position)
    {
        DataTilemap.SetTile(position, DataTile);
        RefreshRenderTiles(position);
    }

    public void SetDataTiles(BoundsInt bounds)
    {
        foreach (var position in bounds.allPositionsWithin)
        {
            SetDataTile(position);
        }
    }

    public void ClearDataTile(Vector3Int position)
    {
        DataTilemap.SetTile(position, null);
        RefreshRenderTiles(position);
    }

    public void ClearDataTiles(BoundsInt bounds)
    {
        foreach (var position in bounds.allPositionsWithin)
        {
            ClearDataTile(position);
        }
    }

    /// <summary>
    /// Fully refreshes the <see cref="RenderTilemap"/> by forcing an update from all tiles in the <see cref="DataTilemap"/>.
    /// </summary>
    public virtual void RefreshRenderTilemap()
    {
        if (RenderTile == null)
        {
            Debug.LogError($"Cannot refresh render tilemap, because tile is not set in dual grid module.", RenderTilemap);
            return;
        }

        RenderTilemap.ClearAllTiles();
        foreach (var position in DataTilemap.cellBounds.allPositionsWithin)
        {
            if (DataTilemap.HasTile(position))
            {
                DataTilemap.SetTile(position, DataTile);
                RefreshRenderTiles(position);
            }
        }
    }

    public virtual void RefreshRenderTiles(Vector3Int dataTilePosition)
    {
        bool hasDataTile = DataTilemap.HasTile(dataTilePosition);

        foreach (Vector3Int renderTilePosition in DualGridUtils.GetRenderTilePositions(dataTilePosition))
        {
            if (hasDataTile)
            {
                SetRenderTile(renderTilePosition);
            }
            else
            {
                UnsetRenderTile(renderTilePosition);
            }
        }
    }

    private void SetRenderTile(Vector3Int renderTilePosition)
    {
        if (!RenderTilemap.HasTile(renderTilePosition))
        {
            RenderTilemap.SetTile(renderTilePosition, RenderTile);
        }
        else
        {
            RenderTilemap.RefreshTile(renderTilePosition);
        }
    }

    private void UnsetRenderTile(Vector3Int renderTilePosition)
    {
        if (RenderTilemap.HasTile(renderTilePosition) && !IsInUseByDataTilemap(renderTilePosition))
        {
            RenderTilemap.SetTile(renderTilePosition, null);
        }
        else
        {
            RenderTilemap.RefreshTile(renderTilePosition);
        }
    }

    /// <summary>
    /// Checks if the render tile at <paramref name="renderTilePosition"/> is in use by any data tile.
    /// </summary>
    private bool IsInUseByDataTilemap(Vector3Int renderTilePosition)
    {
        foreach (Vector3Int dataTilePosition in DualGridUtils.GetDataTilePositions(renderTilePosition))
        {
            if (DataTilemap.HasTile(dataTilePosition)) return true;
        }

        return false;
    }
}
