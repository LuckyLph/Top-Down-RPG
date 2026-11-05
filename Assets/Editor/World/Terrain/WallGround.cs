using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The ground under an area's walls (Docs/KnownGaps.md, Phase 6, chosen with the user over a wider fillet on the walls'
/// inner corners): where the ground stops at a wall, its rounded outer corner and the walls' fillet leave a few pixels
/// with no ground, so a wall cell beside ground gets ground under it, the ground then running on under the wall and its
/// edge hidden there. <see cref="FillUnder"/> paints it under walls just painted (the Terrain Brush, WorldAuthoring's
/// PaintWalls), with the ground most of a cell's eight neighbours show on top; <see cref="BareCells"/> finds the wall
/// cells beside ground with none under them, for Validate.
/// </summary>
public static class WallGround
{
    private static readonly Vector2Int[] Around =
    {
        new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1)
    };

    /// <summary>
    /// Paints, under each of <paramref name="cells"/> with no ground on the layers of the renderer
    /// <paramref name="rendererOf"/> gives for it, the ground of its neighbours (<see cref="GroundBeside"/>) with the tile
    /// a neighbour holds of it, read before anything is painted, on that renderer's layer of it, adding the layer when
    /// the renderer has none (<see cref="GroundLayerMigration.AddLayer(Grid, TerrainMaterial, bool)"/>, a chunk's links
    /// refreshed so its neighbours' skirts read it), each tilemap
    /// recorded for undo in the current group. A cell with no renderer, or with no ground beside it, is left as it is.
    /// Returns how many cells it painted.
    /// </summary>
    public static int FillUnder(IEnumerable<Vector3Int> cells, Func<Vector2Int, TerrainBlendRenderer> rendererOf)
    {
        List<(TerrainBlendRenderer Renderer, Vector3Int Cell, TerrainMaterial Ground, TileBase Tile)> planned = new();
        foreach (Vector3Int cell in cells)
        {
            TerrainBlendRenderer renderer = rendererOf((Vector2Int)cell);
            if (renderer == null || TopAt(renderer, (Vector2Int)cell) != null)
            {
                continue;
            }

            TerrainMaterial ground = GroundBeside((Vector2Int)cell, rendererOf, out TileBase tile);
            if (ground != null)
            {
                planned.Add((renderer, new Vector3Int(cell.x, cell.y, 0), ground, tile));
            }
        }

        HashSet<Tilemap> recorded = new();
        int painted = 0;
        foreach ((TerrainBlendRenderer renderer, Vector3Int cell, TerrainMaterial ground, TileBase tile) in planned)
        {
            Tilemap layer = LayerOf(renderer, ground);
            if (layer == null && tile != null)
            {
                layer = GroundLayerMigration.AddLayer(renderer.GetComponent<Grid>(), ground, false);
                if (layer != null && renderer.TryGetComponent(out WorldChunk chunk))
                {
                    chunk.RefreshLinks();
                }
            }

            if (layer == null || tile == null)
            {
                continue;
            }

            if (recorded.Add(layer))
            {
                Undo.RegisterCompleteObjectUndo(layer, "Paint the ground under walls");
            }

            layer.SetTile(cell, tile);
            painted++;
        }

        return painted;
    }

    /// <summary>
    /// The wall cells of <paramref name="walls"/> on <paramref name="renderer"/>'s grid, within
    /// <paramref name="area"/>, with no ground under them and ground on one of their eight neighbours (read through
    /// <paramref name="rendererOf"/>): where the ground's corner leaves a gap beside the walls.
    /// </summary>
    public static List<Vector2Int> BareCells(TerrainBlendRenderer renderer, Tilemap walls, RectInt area, Func<Vector2Int, TerrainBlendRenderer> rendererOf)
    {
        List<Vector2Int> bare = new();
        BoundsInt bounds = walls.cellBounds;
        foreach (Vector3Int cell in bounds.allPositionsWithin)
        {
            Vector2Int at = (Vector2Int)cell;
            if (cell.z == 0 && area.Contains(at) && walls.HasTile(cell) && TopAt(renderer, at) == null && GroundBeside(at, rendererOf, out _) != null)
            {
                bare.Add(at);
            }
        }

        return bare;
    }

    /// <summary>
    /// The ground most of <paramref name="cell"/>'s eight neighbours show on top (<see cref="TopAt"/> on the renderer
    /// <paramref name="rendererOf"/> gives for each), the first of them in the style's layer order on a tie, with in
    /// <paramref name="tile"/> the tile the first neighbour showing it holds; null when none has ground.
    /// </summary>
    public static TerrainMaterial GroundBeside(Vector2Int cell, Func<Vector2Int, TerrainBlendRenderer> rendererOf, out TileBase tile)
    {
        tile = null;
        Dictionary<TerrainMaterial, TileBase> tiles = new();
        Dictionary<TerrainMaterial, int> counts = new();
        TerrainMaterial best = null;
        int bestCount = 0;
        int bestRank = int.MaxValue;
        foreach (Vector2Int step in Around)
        {
            TerrainBlendRenderer renderer = rendererOf(cell + step);
            TerrainMaterial top = renderer != null ? TopAt(renderer, cell + step) : null;
            if (top == null)
            {
                continue;
            }

            counts[top] = counts.TryGetValue(top, out int count) ? count + 1 : 1;
            if (!tiles.ContainsKey(top))
            {
                Tilemap layer = LayerOf(renderer, top);
                tiles[top] = layer != null ? layer.GetTile(new Vector3Int(cell.x + step.x, cell.y + step.y, 0)) : null;
            }

            int rank = renderer.Style != null ? renderer.Style.LayerOrderIndexOf(top) : 0;
            if (counts[top] > bestCount || (counts[top] == bestCount && rank < bestRank))
            {
                best = top;
                bestCount = counts[top];
                bestRank = rank;
            }
        }

        tile = best != null ? tiles[best] : null;
        return best;
    }

    /// <summary>
    /// The tilemap of <paramref name="renderer"/>'s layer drawing <paramref name="material"/>, or null.
    /// </summary>
    private static Tilemap LayerOf(TerrainBlendRenderer renderer, TerrainMaterial material)
    {
        foreach (TerrainBlendRenderer.Layer layer in renderer.Layers)
        {
            if (layer != null && layer.Material == material && layer.Tilemap != null)
            {
                return layer.Tilemap;
            }
        }

        return null;
    }

    /// <summary>
    /// The material of <paramref name="renderer"/>'s highest layer holding a tile at <paramref name="cell"/>, or null.
    /// </summary>
    public static TerrainMaterial TopAt(TerrainBlendRenderer renderer, Vector2Int cell)
    {
        for (int i = renderer.Layers.Count - 1; i >= 0; i--)
        {
            TerrainBlendRenderer.Layer layer = renderer.Layers[i];
            if (layer != null && layer.Tilemap != null && layer.Tilemap.HasTile(new Vector3Int(cell.x, cell.y, 0)))
            {
                return layer.Material;
            }
        }

        return null;
    }
}
