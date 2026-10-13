using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Brings an area's ground layers in line with a generator's terrain layers without repainting them, as one undo step
/// (Docs/TerrainBlending.md, Phase 4, which used it to move every area off the dual-grid ground, and Phase 5, which
/// used it to give the areas their empty sand, mud and water layers). For each terrain layer: its tilemap under the
/// area's <see cref="Grid"/> is created empty when missing, every filled cell keeps its place and holds the layer's
/// plain tile, the layer loses its dual-grid module, render tilemap and any Tilemap Renderer of its own, the layers come
/// first among the grid's children in the generator's order, and each one's navigation source gets the layer's terrain
/// type and its draw order as priority. The grid's <see cref="TerrainBlendRenderer"/> is added or updated to draw the
/// layers in the generator's order with their materials and the generator's style, and the area's
/// <see cref="AreaTerrain"/> fills them with their tiles. A layer the renderer drew that the generator does not list is
/// kept, with a warning, at its material's place in the style's layer order when it is painted or the area's terrain
/// may edit it, and removed when empty (Docs/TerrainTypes.md, Phase 2), so an area keeps what was painted by hand and
/// sheds the layers it does not use. An area with none of the layers is refused, and every other tilemap (the walls),
/// prop and object stays as it is. Running it again changes nothing. The Scene Generator window runs it as Convert
/// Ground.
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
    /// <paramref name="style"/>, creating the layers the grid lacks. Returns a line per change (empty when nothing
    /// changed), or null when the layers cannot be set up or none of them is a tilemap under the grid, after logging an
    /// error naming <paramref name="owner"/>.
    /// </summary>
    public static List<string> Migrate(Grid grid, TerrainStyle style, IReadOnlyList<AreaGenerator.TerrainLayer> layers, string owner = nameof(GroundLayerMigration))
    {
        if (!AreaGroundSetup.CanSetUp(owner, style, layers, grid))
        {
            return null;
        }

        if (!HasAnyLayer(grid, layers))
        {
            Debug.LogError($"{owner}: none of its terrain layers is a tilemap under {grid.name}, so there is nothing to convert.", grid);
            return null;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName($"Convert {grid.gameObject.scene.name} ground");
        int undoGroup = Undo.GetCurrentGroup();
        List<string> changes = new();
        Tilemap[] tilemaps = AreaGroundSetup.SetUpLayers(grid, style, layers, owner, changes);
        TileBase[] tiles = new TileBase[layers.Count];
        for (int i = 0; i < layers.Count; i++)
        {
            tiles[i] = layers[i].Tile;
            SwapTiles(tilemaps[i], layers[i].Tile, changes);
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
    /// Whether any of <paramref name="layers"/> is a tilemap under <paramref name="grid"/>.
    /// </summary>
    private static bool HasAnyLayer(Grid grid, IReadOnlyList<AreaGenerator.TerrainLayer> layers)
    {
        foreach (AreaGenerator.TerrainLayer layer in layers)
        {
            Transform child = grid.transform.Find(layer.TilemapName);
            if (child != null && child.TryGetComponent(out Tilemap _))
            {
                return true;
            }
        }

        return false;
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
