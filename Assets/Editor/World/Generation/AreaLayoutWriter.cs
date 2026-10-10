using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Writes an <see cref="AreaLayout"/> into the scene of an area's <see cref="Grid"/> as one undo step. Each terrain
/// layer's dual-grid data tilemap under the grid is repainted (and created, with its render tilemap and navigation
/// source, when the scene has none); every prop under the scene's <c>Props</c> root and every
/// <see cref="MobSpawnPoint"/> is replaced; the party's start <see cref="SpawnPoint"/> moves to the layout's spawn
/// cell. The collision tilemap and everything else in the scene stay as they are.
/// </summary>
public static class AreaLayoutWriter
{
    public const string MobSpawnsRootName = "MobSpawns";
    public const string StartSpawnId = "start";
    private const string TerrainSortingLayer = "Terrain";

    /// <summary>
    /// Replaces the terrain, props and mob spawns of <paramref name="grid"/>'s scene with <paramref name="layout"/>,
    /// laid out by <paramref name="generator"/>, and moves the start spawn point. Logs an error and changes nothing
    /// when the grid's cells are not one unit or a terrain layer has no rule tile.
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

        for (int i = 0; i < layers.Count; i++)
        {
            DualGridTilemapModule module = PrepareTerrainLayer(grid, layers[i], i);
            Paint(module, layout.TerrainCells(i));
        }

        WriteProps(grid, scene, layout.Props);
        WriteMobSpawns(grid, scene, layout.Mobs);
        MoveStartSpawn(grid, scene, layout.SpawnCell);

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
    /// Whether the layout can be written: a grid of one-unit cells, a rule tile and a tilemap name on every layer, and
    /// as many layers in the layout as the generator has. Logs an error for the first problem.
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
            if (layers[i] == null || layers[i].RuleTile == null || string.IsNullOrWhiteSpace(layers[i].TilemapName))
            {
                Debug.LogError($"{generator.name}: terrain layer {i} needs a tilemap name and a rule tile.", generator);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The dual-grid module of the grid's child tilemap named after <paramref name="layer"/>, created when missing and
    /// set up to match it: its rule tile, a navigation source with its terrain type and priority, its render tilemap
    /// on the Terrain sorting layer at its order, and its place among the grid's children.
    /// </summary>
    private static DualGridTilemapModule PrepareTerrainLayer(Grid grid, AreaGenerator.TerrainLayer layer, int siblingIndex)
    {
        Transform child = grid.transform.Find(layer.TilemapName);
        DualGridTilemapModule module = child != null ? child.GetComponent<DualGridTilemapModule>() : null;
        if (module == null)
        {
            GameObject data = new(layer.TilemapName);
            Undo.RegisterCreatedObjectUndo(data, $"Create {layer.TilemapName}");
            data.transform.SetParent(grid.transform, false);
            data.AddComponent<Tilemap>();
            module = data.AddComponent<DualGridTilemapModule>();
            DualGridTilemapModuleEditor.CreateRenderTilemapObject(module);
        }

        if (module.RenderTile != layer.RuleTile)
        {
            Undo.RecordObject(module, $"Set {layer.TilemapName} rule tile");
            module.RenderTile = layer.RuleTile;
        }

        if (!module.TryGetComponent(out NavigationTerrainSource2D source))
        {
            source = Undo.AddComponent<NavigationTerrainSource2D>(module.gameObject);
        }

        SerializedObject sourceData = new(source);
        sourceData.FindProperty("terrainType").objectReferenceValue = layer.TerrainType;
        sourceData.FindProperty("priority").intValue = layer.NavigationPriority;
        sourceData.ApplyModifiedProperties();

        TilemapRenderer renderer = module.RenderTilemap.GetComponent<TilemapRenderer>();
        if (renderer.sortingLayerName != TerrainSortingLayer || renderer.sortingOrder != layer.SortingOrder)
        {
            Undo.RecordObject(renderer, $"Sort {layer.TilemapName}");
            renderer.sortingLayerName = TerrainSortingLayer;
            renderer.sortingOrder = layer.SortingOrder;
        }

        if (module.transform.GetSiblingIndex() != siblingIndex)
        {
            Undo.SetSiblingIndex(module.transform, siblingIndex, $"Order {layer.TilemapName}");
        }

        return module;
    }

    /// <summary>
    /// Clears both tilemaps of <paramref name="module"/> and paints <paramref name="cells"/> as data tiles in one
    /// batch, recording both tilemaps for undo first.
    /// </summary>
    private static void Paint(DualGridTilemapModule module, HashSet<Vector3Int> cells)
    {
        Undo.RegisterCompleteObjectUndo(new Object[] { module.DataTilemap, module.RenderTilemap }, $"Paint {module.name}");
        List<Vector3Int> ordered = new(cells);
        ordered.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        module.DataTilemap.ClearAllTiles();
        module.RenderTilemap.ClearAllTiles();
        module.SetDataTiles(ordered);
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

    /// <summary>
    /// Moves the scene's start spawn point (the first spawn point when none has the start id) to the centre of
    /// <paramref name="cell"/>, creating one when the scene has none.
    /// </summary>
    private static void MoveStartSpawn(Grid grid, Scene scene, Vector3Int cell)
    {
        SpawnPoint[] spawnPoints = SceneQuery.FindAll<SpawnPoint>(scene);
        SpawnPoint start = spawnPoints.Length > 0 ? spawnPoints[0] : null;
        foreach (SpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint.SpawnId == StartSpawnId)
            {
                start = spawnPoint;
                break;
            }
        }

        if (start == null)
        {
            GameObject created = new($"SpawnPoint_{StartSpawnId}");
            SceneManager.MoveGameObjectToScene(created, scene);
            Undo.RegisterCreatedObjectUndo(created, "Create spawn point");
            start = created.AddComponent<SpawnPoint>();
        }

        Undo.RecordObject(start.transform, "Move spawn point");
        start.transform.position = CellCentre(grid, cell);
    }
}
