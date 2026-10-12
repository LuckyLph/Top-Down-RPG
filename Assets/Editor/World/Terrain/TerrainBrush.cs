using UnityEngine;

/// <summary>
/// The Tile Palette's default brush, for an area's ground layers (Docs/TerrainBlending.md). It writes what the Dual
/// Grid Brush writes: on a tilemap that has a <see cref="DualGridTilemapModule"/> the module's data tile, in every cell
/// of the stroke, so a dual-grid layer only ever holds one tile type; on a plain tilemap the palette's tile, as Unity's
/// default brush does. What it adds is <see cref="TerrainBrushEditor"/>'s hover preview on a ground layer, a tilemap a
/// <see cref="TerrainBlendRenderer"/> draws, through the renderer instead of a render tilemap. On every other tilemap
/// (walls, and the areas still drawn by dual-grid render tilemaps) it is the Dual Grid Brush, preview included. As the
/// default brush it hides its own extra instance, which the palette would otherwise list twice, and Unity's default
/// brush; the Dual Grid Brush stays in the list.
/// </summary>
[CustomGridBrush(true, true, true, "Terrain Brush")]
public class TerrainBrush : DualGridBrush
{
}
