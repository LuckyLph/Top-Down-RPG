using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Brings an area's ground layers in line with a generator's terrain layers without repainting them, as one undo step
/// (Docs/TerrainBlending.md, Phase 4, which used it to move every area off the dual-grid ground). For each terrain layer
/// whose tilemap the area's <see cref="Grid"/> has: every filled cell keeps its place and holds the layer's plain tile,
/// the layer loses its dual-grid module, render tilemap and any Tilemap Renderer of its own, and its navigation source
/// gets the layer's terrain type and its draw order as priority. The grid's <see cref="TerrainBlendRenderer"/> is added
/// or updated to draw those layers in the generator's order with their materials and the generator's style, and the
/// area's <see cref="AreaTerrain"/> fills them with their tiles. Terrain layers the area does not have are left out (an
/// area with none of them is refused), and every other tilemap (the walls), prop and object stays as it is, except that
/// a tilemap the renderer drew besides the terrain layers stays drawn above them. Running it again changes nothing.
/// The Scene Generator window runs it as Convert Ground.
/// </summary>
public static class GroundLayerMigration
{
    /// <summary>
    /// Converts the ground layers of <paramref name="grid"/> to <paramref name="generator"/>'s terrain layers and
    /// terrain style. Returns a line per change (empty when nothing changed), or null when the generator's layers
    /// cannot be set up, after logging why.
    /// </summary>
    public static List<string> Migrate(Grid grid, AreaGenerator generator)
    {
        return Migrate(grid, generator.TerrainStyle, generator.TerrainLayers, generator.name);
    }

    /// <summary>
    /// Converts the ground layers of <paramref name="grid"/> to <paramref name="layers"/>, bottom first, drawn with
    /// <paramref name="style"/>. Returns a line per change (empty when nothing changed), or null when the layers cannot
    /// be set up or none of them is a tilemap under the grid, after logging an error naming <paramref name="owner"/>.
    /// </summary>
    public static List<string> Migrate(Grid grid, TerrainStyle style, IReadOnlyList<AreaGenerator.TerrainLayer> layers, string owner = nameof(GroundLayerMigration))
    {
        if (!AreaGroundSetup.CanSetUp(owner, style, layers, grid))
        {
            return null;
        }

        List<Tilemap> tilemaps = new();
        List<TerrainMaterial> materials = new();
        List<TileBase> tiles = new();
        List<AreaGenerator.TerrainLayer> present = new();
        foreach (AreaGenerator.TerrainLayer layer in layers)
        {
            Transform child = grid.transform.Find(layer.TilemapName);
            if (child != null && child.TryGetComponent(out Tilemap tilemap))
            {
                tilemaps.Add(tilemap);
                materials.Add(layer.Material);
                tiles.Add(layer.Tile);
                present.Add(layer);
            }
        }

        if (present.Count == 0)
        {
            Debug.LogError($"{owner}: none of its terrain layers is a tilemap under {grid.name}, so there is nothing to convert.", grid);
            return null;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName($"Convert {grid.gameObject.scene.name} ground");
        int undoGroup = Undo.GetCurrentGroup();
        List<string> changes = new();
        for (int i = 0; i < tilemaps.Count; i++)
        {
            AreaGroundSetup.RemoveDualGridParts(tilemaps[i], changes);
            SwapTiles(tilemaps[i], present[i].Tile, changes);
        }

        AreaGroundSetup.SetRenderer(grid, style, tilemaps, materials, changes);
        for (int i = 0; i < tilemaps.Count; i++)
        {
            AreaGroundSetup.SetNavigationSource(tilemaps[i], present[i].TerrainType, TerrainBlendRenderer.NavigationPriorityOf(i), changes);
        }

        AreaGroundSetup.SetFillTiles(grid, tilemaps, tiles, changes);
        if (changes.Count > 0)
        {
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        }

        return changes;
    }

    /// <summary>
    /// When a filled cell of <paramref name="tilemap"/> holds another tile than <paramref name="tile"/>, or the tilemap
    /// still keeps records of tiles or sprites it no longer holds (<see cref="HasStaleRecords"/>), clears the tilemap
    /// and fills every filled cell with <paramref name="tile"/> in one batch recorded for undo. Clearing first drops
    /// those records, which would otherwise stay in the scene unused but still pointing at the old tiles.
    /// </summary>
    private static void SwapTiles(Tilemap tilemap, TileBase tile, List<string> changes)
    {
        List<Vector3Int> filled = new();
        int swapped = 0;
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            TileBase held = tilemap.GetTile(cell);
            if (held != null)
            {
                filled.Add(cell);
                swapped += held != tile ? 1 : 0;
            }
        }

        if (swapped == 0 && !HasStaleRecords(tilemap, tile))
        {
            return;
        }

        TileBase[] tiles = new TileBase[filled.Count];
        System.Array.Fill(tiles, tile);
        Undo.RegisterCompleteObjectUndo(tilemap, $"Convert {tilemap.name}");
        tilemap.ClearAllTiles();
        tilemap.SetTiles(filled.ToArray(), tiles);
        changes.Add(swapped > 0 ? $"{tilemap.name}: {swapped} cells now hold {tile.name}" : $"{tilemap.name}: dropped its records of tiles it no longer holds");
    }

    /// <summary>
    /// Whether <paramref name="tilemap"/>'s serialized records of the tiles and sprites its cells hold keep an entry no
    /// cell uses any more, or a tile other than <paramref name="tile"/>. Writing a tile over a cell keeps the old tile's
    /// record, with a count of 0, until the tilemap is cleared.
    /// </summary>
    private static bool HasStaleRecords(Tilemap tilemap, TileBase tile)
    {
        SerializedObject data = new(tilemap);
        SerializedProperty tileRecords = data.FindProperty("m_TileAssetArray");
        for (int i = 0; i < tileRecords.arraySize; i++)
        {
            SerializedProperty record = tileRecords.GetArrayElementAtIndex(i);
            if (record.FindPropertyRelative("m_RefCount").intValue == 0 || record.FindPropertyRelative("m_Data").objectReferenceValue != tile)
            {
                return true;
            }
        }

        SerializedProperty spriteRecords = data.FindProperty("m_TileSpriteArray");
        for (int i = 0; i < spriteRecords.arraySize; i++)
        {
            if (spriteRecords.GetArrayElementAtIndex(i).FindPropertyRelative("m_RefCount").intValue == 0)
            {
                return true;
            }
        }

        return false;
    }
}
