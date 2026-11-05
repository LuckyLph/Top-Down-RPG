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
/// borders, each cell to the chunk that owns it (<see cref="WorldBrushStroke"/>). On a ground layer or raised ground
/// outside a world it flood fills by itself (<see cref="FloodCells"/>), so its editor's preview of a flood fill shows
/// exactly the cells it fills (Docs/KnownGaps.md, Phase 6). As the default brush it hides its own
/// extra instance, which the palette would otherwise list twice, and Unity's default brush; the Dual Grid Brush stays
/// in the list.
/// </summary>
[CustomGridBrush(true, true, true, "Terrain Brush")]
public class TerrainBrush : DualGridBrush
{
    [System.NonSerialized] private readonly List<TileChangeData> stroke = new();
    [System.NonSerialized] private readonly List<Vector3Int> floodCells = new();

    /// <summary>
    /// On a chunk, fills <paramref name="position"/> across chunk borders (<see cref="WorldBrushStroke.Apply"/>): the
    /// module's data tile in every cell on a dual-grid tilemap, else the brush's tiles where its cells hold one. The
    /// Dual Grid Brush's box fill elsewhere, then on an area's walls the ground under the walls painted beside ground
    /// (<see cref="WallGround.FillUnder"/>).
    /// </summary>
    public override void BoxFill(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
    {
        if (!WorldBrushStroke.TryResolve(brushTarget, out WorldBrushStroke.Target target))
        {
            base.BoxFill(gridLayout, brushTarget, position);
            if (brushTarget != null && brushTarget.TryGetComponent(out DualGridTilemapModule walls))
            {
                TerrainBlendRenderer renderer = walls.GetComponentInParent<TerrainBlendRenderer>();
                if (renderer != null)
                {
                    List<Vector3Int> filled = new();
                    foreach (Vector3Int cell in position.allPositionsWithin)
                    {
                        filled.Add(cell);
                    }

                    WallGround.FillUnder(filled, _ => renderer);
                }
            }

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
            if (!FloodsByItself(brushTarget, out Tilemap tilemap))
            {
                base.FloodFill(gridLayout, brushTarget, position);
                return;
            }

            TileBase tile = FloodTile;
            FloodCells(tilemap, position, tile, floodCells);
            if (floodCells.Count > 0)
            {
                TileBase[] tiles = new TileBase[floodCells.Count];
                System.Array.Fill(tiles, tile);
                tilemap.SetTiles(floodCells.ToArray(), tiles);
            }

            return;
        }

        TileBase fill = target.WritesEveryCell ? target.Module.DataTile : FloodTile;
        WorldBrushStroke.FloodFill(target, position, fill);
    }

    /// <summary>
    /// The tile a flood fill writes on a plain tilemap: the brush's first cell's.
    /// </summary>
    internal TileBase FloodTile => cellCount > 0 ? cells[0].tile : null;

    /// <summary>
    /// Whether the brush flood fills <paramref name="brushTarget"/>'s tilemap by itself: a plain tilemap a
    /// <see cref="TerrainBlendRenderer"/> draws as a ground layer or reads as its raised ground, outside a world.
    /// </summary>
    internal static bool FloodsByItself(GameObject brushTarget, out Tilemap tilemap)
    {
        tilemap = null;
        if (brushTarget == null || !brushTarget.TryGetComponent(out tilemap) || brushTarget.TryGetComponent(out DualGridTilemapModule _))
        {
            return false;
        }

        if (TerrainBlendRenderer.FindLayerDrawing(tilemap, out _) != null)
        {
            return true;
        }

        TerrainBlendRenderer renderer = tilemap.GetComponentInParent<TerrainBlendRenderer>();
        return renderer != null && renderer.RaisedTilemap == tilemap;
    }

    /// <summary>
    /// The cells a flood fill of <paramref name="fill"/> from <paramref name="start"/> on <paramref name="tilemap"/>
    /// writes, into <paramref name="found"/> (cleared first): the start cell and every cell joined to it, 4-connected, that
    /// holds the same tile, within the tilemap's cell bounds (the start cell taken in), as Unity's tilemap flood fill
    /// goes, so from an empty cell outside a shape it reaches those bounds; none for no fill or a start cell holding it.
    /// </summary>
    internal static void FloodCells(Tilemap tilemap, Vector3Int start, TileBase fill, List<Vector3Int> found)
    {
        found.Clear();
        TileBase from = tilemap.GetTile(start);
        if (fill == null || from == fill)
        {
            return;
        }

        BoundsInt bounds = tilemap.cellBounds;
        int xMin = Mathf.Min(bounds.xMin, start.x);
        int yMin = Mathf.Min(bounds.yMin, start.y);
        int xMax = Mathf.Max(bounds.xMax, start.x + 1);
        int yMax = Mathf.Max(bounds.yMax, start.y + 1);
        HashSet<Vector3Int> visited = new() { start };
        Queue<Vector3Int> frontier = new();
        frontier.Enqueue(start);
        Vector3Int[] steps = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
        while (frontier.Count > 0)
        {
            Vector3Int cell = frontier.Dequeue();
            found.Add(cell);
            foreach (Vector3Int step in steps)
            {
                Vector3Int next = cell + step;
                if (next.x >= xMin && next.x < xMax && next.y >= yMin && next.y < yMax && visited.Add(next) && tilemap.GetTile(next) == from)
                {
                    frontier.Enqueue(next);
                }
            }
        }
    }
}
