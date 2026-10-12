using System.Collections.Generic;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// A custom brush completely compatible with the standard tilemaps, with the added Dual Grid functionality.
/// </summary>
/// <remarks>
/// It's responsible for updating the RenderTilemap when any tiles are updated in the DataTilemap.
/// </remarks>
[CustomGridBrush(true, false, false, "Dual Grid Brush")]
public class DualGridBrush : GridBrush
{
    /// <summary>
    /// Paints the module's data tile over <paramref name="bounds"/> on a dual-grid target and refreshes the render
    /// tiles; falls back to the standard box fill on other tilemaps.
    /// </summary>
    public override void BoxFill(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds)
    {
        if (brushTarget.TryGetComponent(out DualGridTilemapModule dualGridTilemapModule))
        {
            SetDualGridTiles(dualGridTilemapModule, dualGridTilemapModule.DataTile, bounds);
        }
        else
        {
            base.BoxFill(gridLayout, brushTarget, bounds);
        }
    }

    /// <summary>
    /// Clears the data tiles in <paramref name="bounds"/> on a dual-grid target and refreshes the render tiles; falls
    /// back to the standard box erase on other tilemaps.
    /// </summary>
    public override void BoxErase(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds)
    {
        if (brushTarget.TryGetComponent(out DualGridTilemapModule dualGridTilemapModule))
        {
            SetDualGridTiles(dualGridTilemapModule, null, bounds);
        }
        else
        {
            base.BoxErase(gridLayout, brushTarget, bounds);
        }
    }

    /// <summary>
    /// Flood fills the data tilemap with the module's data tile from <paramref name="position"/> on a dual-grid target,
    /// leaving the render update to the tilemap change listeners; standard flood fill on other tilemaps.
    /// </summary>
    public override void FloodFill(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (brushTarget.TryGetComponent(out DualGridTilemapModule dualGridTilemapModule))
        {
            dualGridTilemapModule.DataTilemap.FloodFill(position, dualGridTilemapModule.DataTile);
        }
        else
        {
            base.FloodFill(gridLayout, brushTarget, position);
        }
    }

    /// <summary>
    /// Writes <paramref name="dualGridDataTile"/> (null erases) to every data cell in <paramref name="bounds"/> in one
    /// batch that respects lock flags, then refreshes the affected tiles.
    /// </summary>
    private void SetDualGridTiles(DualGridTilemapModule dualGridTilemapModule, DualGridDataTile dualGridDataTile, BoundsInt bounds)
    {
        var tileChangeData = new List<TileChangeData>();

        foreach (var position in bounds.allPositionsWithin)
        {
            tileChangeData.Add(new TileChangeData { position = position, tile = dualGridDataTile });
        }

        dualGridTilemapModule.DataTilemap.SetTiles(tileChangeData.ToArray(), ignoreLockFlags: false);
        RefreshDualGridTilemap(dualGridTilemapModule, bounds);
    }

    /// <summary>
    /// Refreshes the four render tiles around each cell in <paramref name="bounds"/> and the data tile itself;
    /// overridable by derived brushes.
    /// </summary>
    protected virtual void RefreshDualGridTilemap(DualGridTilemapModule dualGridTilemapModule, BoundsInt bounds)
    {
        foreach (var position in bounds.allPositionsWithin)
        {
            dualGridTilemapModule.RefreshRenderTiles(position);
            dualGridTilemapModule.DataTilemap.RefreshTile(position);
        }
    }
}
