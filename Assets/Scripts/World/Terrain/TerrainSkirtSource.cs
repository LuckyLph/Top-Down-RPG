using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// One tilemap of a neighbouring chunk that a <see cref="TerrainBlendRenderer"/> also reads, only in
/// <see cref="Cells"/>, the strip of the neighbour's cells along its border (Docs/WorldStreaming.md: Terrain): a ground
/// layer drawing <see cref="Material"/>, matched to the renderer's layer of the same material, or, without a material,
/// the neighbour's raised ground. Only drawing reads it; the renderer's own tilemaps, which navigation reads, never
/// hold these cells.
/// </summary>
public readonly struct TerrainSkirtSource
{
    /// <summary>
    /// A source of <paramref name="tilemap"/>'s cells in <paramref name="cells"/>: a ground layer drawing
    /// <paramref name="material"/>, or raised ground when <paramref name="material"/> is null.
    /// </summary>
    public TerrainSkirtSource(Tilemap tilemap, TerrainMaterial material, RectInt cells)
    {
        Tilemap = tilemap;
        Material = material;
        Cells = cells;
    }

    public Tilemap Tilemap { get; }
    public TerrainMaterial Material { get; }
    public RectInt Cells { get; }
    public bool IsRaised => Material == null;

    /// <summary>
    /// A source of the raised ground of <paramref name="tilemap"/> in <paramref name="cells"/>.
    /// </summary>
    public static TerrainSkirtSource Raised(Tilemap tilemap, RectInt cells)
    {
        return new TerrainSkirtSource(tilemap, null, cells);
    }

    /// <summary>
    /// Whether <paramref name="cell"/> lies inside <see cref="Cells"/>, ignoring z.
    /// </summary>
    public bool Contains(Vector3Int cell)
    {
        return cell.x >= Cells.xMin && cell.x < Cells.xMax && cell.y >= Cells.yMin && cell.y < Cells.yMax;
    }

    /// <summary>
    /// Whether any cell of <see cref="Cells"/> holds a tile.
    /// </summary>
    public bool HasAnyTile()
    {
        if (Tilemap == null || Cells.width <= 0 || Cells.height <= 0)
        {
            return false;
        }

        foreach (TileBase tile in Tilemap.GetTilesBlock(new BoundsInt(Cells.xMin, Cells.yMin, 0, Cells.width, Cells.height, 1)))
        {
            if (tile != null)
            {
                return true;
            }
        }

        return false;
    }
}
