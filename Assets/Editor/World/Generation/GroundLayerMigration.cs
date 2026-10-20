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
/// Ground. <see cref="AddLayer(Grid, TerrainMaterial)"/> adds one material's layer to an area the same way, keeping
/// every other layer (Docs/TerrainLayers32.md, Phase 4: the Terrain Brush offers it for a material the area lacks).
/// </summary>
public static class GroundLayerMigration
{
    public const string TerrainTypeFolder = "Assets/Settings/AI";

    /// <summary>
    /// The name of <paramref name="material"/>'s ground layer: its name and Tilemap, as the generators name theirs.
    /// </summary>
    public static string TilemapName(TerrainMaterial material)
    {
        return $"{material.name}Tilemap";
    }

    /// <summary>
    /// Where <paramref name="material"/>'s terrain type is: Terrain_ and the material's name in
    /// <see cref="TerrainTypeFolder"/>.
    /// </summary>
    public static string TerrainTypePath(TerrainMaterial material)
    {
        return $"{TerrainTypeFolder}/Terrain_{material.name}.asset";
    }

    /// <summary>
    /// Adds the ground layer of the project's <paramref name="material"/> to <paramref name="grid"/>
    /// (<see cref="AddLayer(Grid, AreaGenerator.TerrainLayer)"/>): its tilemap named after it (<see cref="TilemapName"/>),
    /// its plain tile (<see cref="TerrainTileWriter.TilePath"/>) and its terrain type (<see cref="TerrainTypePath"/>).
    /// Returns the layer's tilemap, or null after logging why when the material has no tile or terrain type or the layer
    /// cannot be added. With <paramref name="ownUndoStep"/> false the change joins the current undo group instead of
    /// making one of its own, so a brush stroke that adds layers to several chunks stays one undo step.
    /// </summary>
    public static Tilemap AddLayer(Grid grid, TerrainMaterial material, bool ownUndoStep = true)
    {
        Tile tile = material != null ? AssetDatabase.LoadAssetAtPath<Tile>(TerrainTileWriter.TilePath(material)) : null;
        TerrainType2D terrainType = material != null ? AssetDatabase.LoadAssetAtPath<TerrainType2D>(TerrainTypePath(material)) : null;
        if (tile == null || terrainType == null)
        {
            string name = material != null ? material.name : "no material";
            Debug.LogError($"{grid.name}: cannot add {name}'s ground layer: it needs a tile at {(material != null ? TerrainTileWriter.TilePath(material) : "?")} and a terrain type at {(material != null ? TerrainTypePath(material) : "?")}.", grid);
            return null;
        }

        return AddLayer(grid, new AreaGenerator.TerrainLayer(TilemapName(material), tile, terrainType, material), ownUndoStep);
    }

    /// <summary>
    /// Adds <paramref name="layer"/> to the ground layers <paramref name="grid"/>'s <see cref="TerrainBlendRenderer"/>
    /// draws, at its material's place in the renderer's style's layer order, as one undo step: its tilemap created empty
    /// as a plain data tilemap when missing, its navigation source with its terrain type and its draw order as
    /// priority, and the renderer's entry; every layer the renderer drew stays drawn, empty ones included, the layers
    /// above it moving up one (<see cref="AreaGroundSetup.InsertLayer"/>). Returns the layer's tilemap, or null after
    /// logging why when the grid has no renderer with a style, the style's order does not list the material, the
    /// renderer already draws it, it already draws <see cref="TerrainControlMap.MaxLayers"/> layers, or the layer has
    /// no tilemap name or tile, or during play. With <paramref name="ownUndoStep"/> false it joins the current undo
    /// group.
    /// </summary>
    public static Tilemap AddLayer(Grid grid, AreaGenerator.TerrainLayer layer, bool ownUndoStep = true)
    {
        string problem = AddProblem(grid, layer);
        if (problem != null)
        {
            Debug.LogError(problem, grid);
            return null;
        }

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        if (ownUndoStep)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"Add {layer.Material.name} layer");
        }

        int undoGroup = Undo.GetCurrentGroup();
        List<string> changes = new();
        Tilemap tilemap = AreaGroundSetup.InsertLayer(grid, renderer.Style, layer, changes);
        if (ownUndoStep)
        {
            Undo.CollapseUndoOperations(undoGroup);
        }

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        return tilemap;
    }

    /// <summary>
    /// Why <paramref name="layer"/> cannot be added to <paramref name="grid"/> (<see cref="AddLayer(Grid, AreaGenerator.TerrainLayer)"/>),
    /// or null when it can.
    /// </summary>
    private static string AddProblem(Grid grid, AreaGenerator.TerrainLayer layer)
    {
        string material = layer != null && layer.Material != null ? layer.Material.name : "no material";
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return $"{grid.name}: ground layers are added outside play mode; a change to the scene during play is lost when play stops.";
        }

        if (layer == null || layer.Material == null || layer.Tile == null || string.IsNullOrWhiteSpace(layer.TilemapName))
        {
            return $"{grid.name}: a ground layer needs a tilemap name, a tile and a material ({material}).";
        }

        if (!grid.TryGetComponent(out TerrainBlendRenderer renderer) || renderer.Style == null)
        {
            return $"{grid.name}: has no {nameof(TerrainBlendRenderer)} with a terrain style to add {material}'s layer to.";
        }

        if (renderer.Style.LayerOrderIndexOf(layer.Material) < 0)
        {
            return $"{grid.name}: {renderer.Style.name}'s layer order does not list {material}, so its layer has no place to go; add it there (Docs/TerrainTypes.md).";
        }

        foreach (TerrainBlendRenderer.Layer drawn in renderer.Layers)
        {
            if (drawn != null && drawn.Material == layer.Material)
            {
                return $"{grid.name}: already draws {material}{(drawn.Tilemap != null ? $" on {drawn.Tilemap.name}" : "")}.";
            }
        }

        if (renderer.Layers.Count >= TerrainControlMap.MaxLayers)
        {
            return $"{grid.name}: already draws {renderer.Layers.Count} ground layers, the most a {nameof(TerrainBlendRenderer)} draws, so {material}'s cannot be added.";
        }

        return null;
    }

    /// <summary>
    /// Converts the ground layers of <paramref name="grid"/> to <paramref name="generator"/>'s terrain layers and
    /// terrain style, and for a generator with raised ground sets up the raised tilemap, keeping its cells
    /// (<see cref="AreaGroundSetup.SetUpRaised"/>). Returns a line per change (empty when nothing changed), or null when
    /// the generator's layers cannot be set up, after logging why.
    /// </summary>
    public static List<string> Migrate(Grid grid, AreaGenerator generator)
    {
        List<string> changes = Migrate(grid, generator.TerrainStyle, generator.TerrainLayers, generator.name);
        if (changes != null && generator.RaisedTile != null)
        {
            int before = changes.Count;
            AreaGroundSetup.SetUpRaised(grid, changes);
            if (changes.Count > before)
            {
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            }
        }

        return changes;
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
