using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Writes an <see cref="AreaLayout"/> into the scene of a standalone <see cref="Grid"/> as one undo step. Each terrain
/// layer's data tilemap under the grid is repainted with the layer's plain tile (and created, with its navigation
/// source, when the scene has none) and is left a plain tilemap: a dual-grid module, render tilemap or Tilemap Renderer
/// still on it is removed, since every cell is repainted anyway. The grid's <see cref="TerrainBlendRenderer"/> (added
/// when missing) draws the layers in the generator's order with their materials and the generator's terrain style, and
/// any layer it drew that the generator does not list is kept at its place in the style's layer order when painted,
/// and removed when empty (<see cref="AreaGroundSetup.SetUpLayers"/>); each layer's navigation priority is its place in
/// the renderer, and the grid's <see cref="AreaTerrain"/> fills the layers it lists with their tiles. Every prop under
/// the scene's <c>Props</c> root and every <see cref="MobSpawnPoint"/> is replaced. The layout's spawn cell is written
/// nowhere: it only shapes the layout (the generator's preview shows it). The collision tilemap and everything else in
/// the scene stay as they are.
/// </summary>
public static class AreaLayoutWriter
{
    public const string MobSpawnsRootName = "MobSpawns";

    /// <summary>
    /// Replaces the terrain, props and mob spawns of <paramref name="grid"/>'s scene with <paramref name="layout"/>,
    /// laid out by <paramref name="generator"/>. Logs an error and changes nothing when the grid's cells are not one
    /// unit, the layout was laid out with other layers than the generator's, or the generator's layers cannot be set up
    /// (<see cref="AreaGroundSetup.Problem"/>: no terrain style, too many layers, a layer lacking a tilemap name, a tile
    /// or a material, or layers out of the style's layer order).
    /// </summary>
    public static bool Write(Grid grid, AreaGenerator generator, AreaLayout layout)
    {
        IReadOnlyList<AreaGenerator.TerrainLayer> layers = generator.TerrainLayers;
        if (!CanWrite(grid, generator, layers, layout))
        {
            return false;
        }

        Scene scene = grid.gameObject.scene;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName($"Generate {scene.name}");
        int undoGroup = Undo.GetCurrentGroup();

        List<string> changes = new();
        Tilemap[] tilemaps = AreaGroundSetup.SetUpLayers(grid, generator.TerrainStyle, layers, generator.name, changes);
        TileBase[] tiles = new TileBase[layers.Count];
        for (int i = 0; i < layers.Count; i++)
        {
            tiles[i] = layers[i].Tile;
            Paint(tilemaps[i], layers[i].Tile, layout.TerrainCells(i));
        }

        AreaGroundSetup.SetFillTiles(grid, tilemaps, tiles, changes);
        WriteRaised(grid, generator, layout, changes);
        WriteProps(grid, scene, layout.Props);
        WriteMobSpawns(grid, scene, layout.Mobs);

        if (grid.TryGetComponent(out NavigationGrid2D navigation))
        {
            navigation.MarkGizmoGridStale();
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }

    /// <summary>
    /// The world position of the centre of <paramref name="cell"/> on <paramref name="grid"/>, at z = 0.
    /// </summary>
    public static Vector3 CellCentre(Grid grid, Vector3Int cell)
    {
        Vector3 centre = grid.GetCellCenterWorld(cell);
        return new Vector3(centre.x, centre.y, 0f);
    }

    /// <summary>
    /// Whether the layout can be written: a grid of one-unit cells, a layout of the generator's layers (as many, with
    /// the same materials), and layers that can be set up (<see cref="AreaGroundSetup.CanSetUp"/>). Logs an error for
    /// the first problem.
    /// </summary>
    private static bool CanWrite(Grid grid, AreaGenerator generator, IReadOnlyList<AreaGenerator.TerrainLayer> layers, AreaLayout layout)
    {
        if ((Vector2)grid.cellSize != Vector2.one)
        {
            Debug.LogError($"{grid.name} has {grid.cellSize} cells; generated layouts need one-unit cells.", grid);
            return false;
        }

        if (layout.TerrainLayerCount != layers.Count)
        {
            Debug.LogError($"The layout has {layout.TerrainLayerCount} terrain layers but {generator.name} has {layers.Count}.", generator);
            return false;
        }

        for (int i = 0; i < layers.Count; i++)
        {
            if (layout.TerrainLayers[i] == null || layers[i] == null || layout.TerrainLayers[i].Material != layers[i].Material)
            {
                Debug.LogError($"The layout's terrain layer {i} is not {generator.name}'s; generate the layout again.", generator);
                return false;
            }
        }

        return AreaGroundSetup.CanSetUp(generator.name, generator.TerrainStyle, layers, generator);
    }

    /// <summary>
    /// Clears <paramref name="tilemap"/> and fills <paramref name="cells"/> with <paramref name="tile"/> in one batch,
    /// recording the tilemap for undo first.
    /// </summary>
    private static void Paint(Tilemap tilemap, TileBase tile, HashSet<Vector3Int> cells)
    {
        Undo.RegisterCompleteObjectUndo(tilemap, $"Paint {tilemap.name}");
        List<Vector3Int> ordered = new(cells);
        ordered.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        TileBase[] tiles = new TileBase[ordered.Count];
        Array.Fill(tiles, tile);
        tilemap.ClearAllTiles();
        tilemap.SetTiles(ordered.ToArray(), tiles);
    }

    /// <summary>
    /// Writes the layout's raised ground onto the grid's raised tilemap, set up by
    /// <see cref="AreaGroundSetup.SetUpRaised"/>, the generator's ramp tile in its ramps and its raised tile elsewhere. Does nothing for a generator without a raised tile.
    /// </summary>
    private static void WriteRaised(Grid grid, AreaGenerator generator, AreaLayout layout, List<string> changes)
    {
        if (generator.RaisedTile == null)
        {
            return;
        }

        Tilemap raised = AreaGroundSetup.SetUpRaised(grid, changes);
        Undo.RegisterCompleteObjectUndo(raised, $"Paint {raised.name}");
        List<Vector3Int> ordered = new(layout.Raised);
        ordered.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        TileBase[] tiles = new TileBase[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            tiles[i] = layout.Ramps.Contains(ordered[i]) && generator.RampTile != null ? generator.RampTile : generator.RaisedTile;
        }

        raised.ClearAllTiles();
        raised.SetTiles(ordered.ToArray(), tiles);
    }

    /// <summary>
    /// Destroys every child of the scene's <c>Props</c> root and instantiates the layout's props under it: a
    /// blocking prop with its footprint on its cells, a walk-through prop at its point snapped to the pixel grid.
    /// </summary>
    private static void WriteProps(Grid grid, Scene scene, List<AreaLayout.Prop> props)
    {
        Transform root = PropPlacementWindow.FindOrCreateRoot(scene, PropPlacementWindow.PropsRootName);
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        }

        foreach (AreaLayout.Prop prop in props)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prop.Prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, $"Place {prop.Prefab.name}");
            instance.transform.SetParent(root, false);
            instance.transform.position = PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint)
                ? PropGridSnapping.RootOnCells(grid, prop.Cell, footprint)
                : PropGridSnapping.SnapToPixels(grid.CellToWorld(prop.Cell) + (Vector3)Vector2.Scale(prop.Offset, grid.cellSize));
        }
    }

    /// <summary>
    /// Destroys every mob spawn point in the scene and creates one per layout mob under the <c>MobSpawns</c> root
    /// (created when missing), at its cell's centre.
    /// </summary>
    private static void WriteMobSpawns(Grid grid, Scene scene, List<AreaLayout.Mob> mobs)
    {
        foreach (MobSpawnPoint existing in SceneQuery.FindAll<MobSpawnPoint>(scene))
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        Transform root = PropPlacementWindow.FindOrCreateRoot(scene, MobSpawnsRootName);
        for (int i = 0; i < mobs.Count; i++)
        {
            AreaLayout.Mob mob = mobs[i];
            GameObject marker = new($"MobSpawn_{mob.Prefab.name}_{i + 1:00}");
            Undo.RegisterCreatedObjectUndo(marker, "Create mob spawn");
            marker.transform.SetParent(root, false);
            marker.transform.position = CellCentre(grid, mob.Cell);
            SerializedObject spawn = new(marker.AddComponent<MobSpawnPoint>());
            spawn.FindProperty("mobPrefab").objectReferenceValue = mob.Prefab;
            spawn.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
