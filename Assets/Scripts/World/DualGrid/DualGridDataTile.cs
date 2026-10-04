using System;
using UnityEngine;
using UnityEngine.Tilemaps;

[Serializable]
public class DualGridDataTile : Tile
{
    private DualGridTilemapModule dualGridTilemapModule;

    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        SetDataTilemap(tilemap);

        base.GetTileData(position, tilemap, ref tileData);

        if (dualGridTilemapModule != null && dualGridTilemapModule.GameObjectOrigin != GameObjectOrigin.DataTilemap)
        {
            tileData.gameObject = null;
        }
    }

    private void SetDataTilemap(ITilemap tilemap)
    {
        var originTilemap = tilemap.GetComponent<Tilemap>();
        dualGridTilemapModule = originTilemap.GetComponent<DualGridTilemapModule>();
    }
}
