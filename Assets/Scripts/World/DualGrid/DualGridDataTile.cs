using System;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The tile painted into a dual-grid data tilemap, generated from a <see cref="DualGridRuleTile"/>. It marks a cell as
/// filled and carries the rule tile's default collider and GameObject; the render tilemap draws the visuals.
/// </summary>
[Serializable]
public class DualGridDataTile : Tile
{
    private DualGridTilemapModule dualGridTilemapModule;

    private Tilemap lastOriginTilemap;

    /// <summary>
    /// Returns the base tile data, dropping the GameObject unless the module places GameObjects on the data
    /// tilemap.
    /// </summary>
    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        SetDataTilemap(tilemap);

        base.GetTileData(position, tilemap, ref tileData);

        if (dualGridTilemapModule != null && dualGridTilemapModule.GameObjectOrigin != GameObjectOrigin.DataTilemap)
        {
            tileData.gameObject = null;
        }
    }

    /// <summary>
    /// Finds the <see cref="DualGridTilemapModule"/> on the tilemap being updated, reusing the cached one while the
    /// tilemap is unchanged.
    /// </summary>
    private void SetDataTilemap(ITilemap tilemap)
    {
        Tilemap originTilemap = tilemap.GetComponent<Tilemap>();
        if (originTilemap != lastOriginTilemap || dualGridTilemapModule == null)
        {
            lastOriginTilemap = originTilemap;
            dualGridTilemapModule = originTilemap != null ? originTilemap.GetComponent<DualGridTilemapModule>() : null;
        }
    }
}
