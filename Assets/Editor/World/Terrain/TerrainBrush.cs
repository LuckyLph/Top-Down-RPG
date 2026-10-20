using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The Tile Palette's default brush, for an area's ground layers (Docs/TerrainBlending.md). It writes what the Dual
/// Grid Brush writes: on a tilemap that has a <see cref="DualGridTilemapModule"/> the module's data tile, in every cell
/// of the stroke, so a dual-grid layer only ever holds one tile type; on a plain tilemap the palette's tile, as Unity's
/// default brush does. What it adds is <see cref="TerrainBrushEditor"/>'s hover preview on a ground layer, a tilemap a
/// <see cref="TerrainBlendRenderer"/> draws, through the renderer instead of a render tilemap. On every other tilemap
/// (walls, and the areas still drawn by dual-grid render tilemaps) it is the Dual Grid Brush, preview included. On a
/// chunk of a seamless world its box fills, box erases (and so paints and erases) and flood fills go across chunk
/// borders, each cell to the chunk that owns it (<see cref="WorldBrushStroke"/>). As the default brush it hides its own
/// extra instance, which the palette would otherwise list twice, and Unity's default brush; the Dual Grid Brush stays
/// in the list.
/// </summary>
[CustomGridBrush(true, true, true, "Terrain Brush")]
public class TerrainBrush : DualGridBrush
{
    [System.NonSerialized] private readonly List<TileChangeData> stroke = new();

    /// <summary>
    /// On a chunk, fills <paramref name="position"/> across chunk borders (<see cref="WorldBrushStroke.Apply"/>): the
    /// module's data tile in every cell on a dual-grid tilemap, else the brush's tiles where its cells hold one. The
    /// Dual Grid Brush's box fill elsewhere.
    /// </summary>
    public override void BoxFill(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
    {
        if (!WorldBrushStroke.TryResolve(brushTarget, out WorldBrushStroke.Target target))
        {
            base.BoxFill(gridLayout, brushTarget, position);
            return;
        }

        stroke.Clear();
        foreach (Vector3Int location in position.allPositionsWithin)
        {
            if (target.WritesEveryCell)
            {
                stroke.Add(new TileChangeData { position = location, tile = target.Module.DataTile, color = Color.white, transform = Matrix4x4.identity });
                continue;
            }

            Vector3Int local = location - position.min;
            if (cellCount == 0)
            {
                continue;
            }

            BrushCell cell = cells[GetCellIndexWrapAround(local.x, local.y, local.z)];
            if (cell.tile != null)
            {
                stroke.Add(new TileChangeData { position = location, tile = cell.tile, color = cell.color, transform = cell.matrix });
            }
        }

        WorldBrushStroke.Apply(target, stroke);
    }

    /// <summary>
    /// On a chunk, empties <paramref name="position"/> across chunk borders; the Dual Grid Brush's box erase elsewhere.
    /// </summary>
    public override void BoxErase(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
    {
        if (!WorldBrushStroke.TryResolve(brushTarget, out WorldBrushStroke.Target target))
        {
            base.BoxErase(gridLayout, brushTarget, position);
            return;
        }

        stroke.Clear();
        foreach (Vector3Int location in position.allPositionsWithin)
        {
            stroke.Add(new TileChangeData { position = location, tile = null, color = Color.white, transform = Matrix4x4.identity });
        }

        WorldBrushStroke.Apply(target, stroke);
    }

    /// <summary>
    /// On a chunk, flood fills from <paramref name="position"/> across the open chunks
    /// (<see cref="WorldBrushStroke.FloodFill"/>) with the module's data tile on a dual-grid tilemap, else the brush's
    /// first tile; the Dual Grid Brush's flood fill elsewhere.
    /// </summary>
    public override void FloodFill(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (!WorldBrushStroke.TryResolve(brushTarget, out WorldBrushStroke.Target target))
        {
            base.FloodFill(gridLayout, brushTarget, position);
            return;
        }

        TileBase fill = target.WritesEveryCell ? target.Module.DataTile : cellCount > 0 ? cells[0].tile : null;
        WorldBrushStroke.FloodFill(target, position, fill);
    }
}
