using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The steps <see cref="AreaLayoutWriter"/> and <see cref="GroundLayerMigration"/> share to set up an area's ground
/// layers the way the blended terrain draws them (Docs/TerrainBlending.md): plain data tilemaps without dual-grid parts
/// or a Tilemap Renderer, first among the Grid's children in draw order (created when missing), each with a navigation
/// source whose priority is its draw order, drawn by the Grid's
/// <see cref="TerrainBlendRenderer"/>, and filled by the area's <see cref="AreaTerrain"/> with their plain tiles. Every
/// change goes through undo, a step whose target is already set changes and records nothing, and each change adds a
/// line to the caller's list. Only outside play mode, where the changes can be saved.
/// </summary>
internal static class AreaGroundSetup
{
    /// <summary>
    /// Whether ground layers can be set up from <paramref name="layers"/> and <paramref name="style"/>: outside play
    /// mode (a change to the scene's play-mode copy would be lost when play stops), with a style, and at most
    /// <see cref="TerrainControlMap.MaxLayers"/> layers with distinct tilemap names, each with a tile and a material.
    /// Logs an error naming <paramref name="owner"/> for the first problem, pinging <paramref name="context"/>.
    /// </summary>
    public static bool CanSetUp(string owner, TerrainStyle style, IReadOnlyList<AreaGenerator.TerrainLayer> layers, Object context)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{owner}: ground layers are set up outside play mode; a change to the scene during play is lost when play stops.", context);
            return false;
        }

        if (style == null)
        {
            Debug.LogError($"{owner}: no terrain style for the area's {nameof(TerrainBlendRenderer)}.", context);
            return false;
        }

        if (layers.Count > TerrainControlMap.MaxLayers)
        {
            Debug.LogError($"{owner}: {layers.Count} terrain layers, but the blended terrain draws at most {TerrainControlMap.MaxLayers}.", context);
            return false;
        }

        for (int i = 0; i < layers.Count; i++)
        {
            AreaGenerator.TerrainLayer layer = layers[i];
            if (layer == null || string.IsNullOrWhiteSpace(layer.TilemapName) || layer.Tile == null || layer.Material == null)
            {
                Debug.LogError($"{owner}: terrain layer {i} needs a tilemap name, a tile and a material.", context);
                return false;
            }

            for (int j = 0; j < i; j++)
            {
                if (layers[j].TilemapName == layer.TilemapName)
                {
                    Debug.LogError($"{owner}: terrain layers {j} and {i} both name {layer.TilemapName}; each tilemap is one layer.", context);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The grid's child tilemap named after <paramref name="layer"/>, created empty when missing, as a plain data
    /// tilemap at place <paramref name="index"/> among the grid's children: any dual-grid parts or Tilemap Renderer
    /// removed, and a navigation source with the layer's terrain type and its index as priority, its draw order in the
    /// renderer <see cref="SetRenderer"/> sets up.
    /// </summary>
    public static Tilemap PrepareLayer(Grid grid, AreaGenerator.TerrainLayer layer, int index, List<string> changes)
    {
        Transform child = grid.transform.Find(layer.TilemapName);
        Tilemap tilemap = child != null ? child.GetComponent<Tilemap>() : null;
        if (child == null)
        {
            GameObject data = new(layer.TilemapName);
            Undo.RegisterCreatedObjectUndo(data, $"Create {layer.TilemapName}");
            data.transform.SetParent(grid.transform, false);
            tilemap = data.AddComponent<Tilemap>();
            changes.Add($"{grid.name}: created {layer.TilemapName}");
        }
        else if (tilemap == null)
        {
            tilemap = Undo.AddComponent<Tilemap>(child.gameObject);
            changes.Add($"{layer.TilemapName}: added a {nameof(Tilemap)}");
        }

        RemoveDualGridParts(tilemap, changes);
        SetNavigationSource(tilemap, layer.TerrainType, TerrainBlendRenderer.NavigationPriorityOf(index), changes);
        if (tilemap.transform.GetSiblingIndex() != index)
        {
            Undo.SetSiblingIndex(tilemap.transform, index, $"Order {layer.TilemapName}");
            changes.Add($"{layer.TilemapName}: moved to place {index} under {grid.name}");
        }

        return tilemap;
    }

    /// <summary>
    /// Removes what a ground layer no longer has from <paramref name="tilemap"/>: its
    /// <see cref="DualGridTilemapModule"/>, every tilemap under it (the module's render tilemap, or one left behind
    /// after its module was removed, which would go on drawing the old tileset), and a Tilemap Renderer on the data
    /// tilemap itself, which would draw the tiles' palette swatches.
    /// </summary>
    public static void RemoveDualGridParts(Tilemap tilemap, List<string> changes)
    {
        if (tilemap.TryGetComponent(out DualGridTilemapModule module))
        {
            changes.Add($"{tilemap.name}: removed its {nameof(DualGridTilemapModule)}");
            Undo.DestroyObjectImmediate(module);
        }

        for (int i = tilemap.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = tilemap.transform.GetChild(i);
            if (child.TryGetComponent(out Tilemap _))
            {
                changes.Add($"{tilemap.name}: removed its render tilemap {child.name}");
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        if (tilemap.TryGetComponent(out TilemapRenderer tilemapRenderer))
        {
            changes.Add($"{tilemap.name}: removed its {nameof(TilemapRenderer)}");
            Undo.DestroyObjectImmediate(tilemapRenderer);
        }
    }

    /// <summary>
    /// Gives <paramref name="tilemap"/> a <see cref="NavigationTerrainSource2D"/> (added when missing) with
    /// <paramref name="terrainType"/> and <paramref name="priority"/>.
    /// </summary>
    public static void SetNavigationSource(Tilemap tilemap, TerrainType2D terrainType, int priority, List<string> changes)
    {
        if (!tilemap.TryGetComponent(out NavigationTerrainSource2D source))
        {
            source = Undo.AddComponent<NavigationTerrainSource2D>(tilemap.gameObject);
            changes.Add($"{tilemap.name}: added a {nameof(NavigationTerrainSource2D)}");
        }

        SerializedObject data = new(source);
        SerializedProperty type = data.FindProperty("terrainType");
        SerializedProperty order = data.FindProperty("priority");
        if (type.objectReferenceValue != terrainType)
        {
            changes.Add($"{tilemap.name}: terrain type {Name(terrainType)}");
            type.objectReferenceValue = terrainType;
        }

        if (order.intValue != priority)
        {
            changes.Add($"{tilemap.name}: navigation priority {order.intValue} -> {priority}");
            order.intValue = priority;
        }

        data.ApplyModifiedProperties();
    }

    /// <summary>
    /// Adds the <see cref="TerrainBlendRenderer"/> of <paramref name="grid"/> when it has none and has it draw
    /// <paramref name="tilemaps"/> first, bottom first, with <paramref name="materials"/> and <paramref name="style"/>.
    /// Any other tilemap it drew stays drawn above them, in its old order and with its material, and its navigation
    /// source's priority follows its new place, so no layer stops being drawn while it still counts for navigation. Past
    /// <see cref="TerrainControlMap.MaxLayers"/> layers the rest are dropped with a warning, and entries without a
    /// tilemap are dropped. Its other settings (chunk size, sorting, look) stay as they are.
    /// </summary>
    public static void SetRenderer(Grid grid, TerrainStyle style, IReadOnlyList<Tilemap> tilemaps, IReadOnlyList<TerrainMaterial> materials, List<string> changes)
    {
        if (!grid.TryGetComponent(out TerrainBlendRenderer renderer))
        {
            renderer = Undo.AddComponent<TerrainBlendRenderer>(grid.gameObject);
            changes.Add($"{grid.name}: added a {nameof(TerrainBlendRenderer)}");
        }

        SerializedObject data = new(renderer);
        SerializedProperty styleProperty = data.FindProperty("style");
        if (styleProperty.objectReferenceValue != style)
        {
            changes.Add($"{grid.name}: terrain style {Name(style)}");
            styleProperty.objectReferenceValue = style;
        }

        SerializedProperty layers = data.FindProperty("layers");
        List<Tilemap> drawn = new(tilemaps);
        List<TerrainMaterial> drawnMaterials = new(materials);
        for (int i = 0; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            Tilemap listed = layer.FindPropertyRelative("tilemap").objectReferenceValue as Tilemap;
            if (listed == null || Contains(drawn, listed))
            {
                continue;
            }

            if (drawn.Count >= TerrainControlMap.MaxLayers)
            {
                Debug.LogWarning($"{grid.name}: the {nameof(TerrainBlendRenderer)} no longer draws {listed.name}: it draws at most {TerrainControlMap.MaxLayers} layers. {listed.name}'s navigation source still counts.", renderer);
                changes.Add($"{grid.name}: stopped drawing {listed.name}");
                continue;
            }

            drawn.Add(listed);
            drawnMaterials.Add(layer.FindPropertyRelative("material").objectReferenceValue as TerrainMaterial);
        }

        if (layers.arraySize != drawn.Count)
        {
            changes.Add($"{grid.name}: draws {drawn.Count} layers instead of {layers.arraySize}");
            layers.arraySize = drawn.Count;
        }

        for (int i = 0; i < drawn.Count; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            SerializedProperty tilemap = layer.FindPropertyRelative("tilemap");
            SerializedProperty material = layer.FindPropertyRelative("material");
            if (tilemap.objectReferenceValue != drawn[i] || material.objectReferenceValue != drawnMaterials[i])
            {
                changes.Add(i < tilemaps.Count ? $"{grid.name}: draws {drawn[i].name} as layer {i} with {Name(drawnMaterials[i])}" : $"{grid.name}: keeps drawing {drawn[i].name}, now as layer {i}");
                tilemap.objectReferenceValue = drawn[i];
                material.objectReferenceValue = drawnMaterials[i];
            }
        }

        data.ApplyModifiedProperties();
        for (int i = tilemaps.Count; i < drawn.Count; i++)
        {
            SetNavigationPriority(drawn[i], TerrainBlendRenderer.NavigationPriorityOf(i), changes);
        }
    }

    /// <summary>
    /// Gives the navigation source of <paramref name="tilemap"/>, when it has one, <paramref name="priority"/>.
    /// </summary>
    private static void SetNavigationPriority(Tilemap tilemap, int priority, List<string> changes)
    {
        if (!tilemap.TryGetComponent(out NavigationTerrainSource2D source))
        {
            return;
        }

        SerializedObject data = new(source);
        SerializedProperty order = data.FindProperty("priority");
        if (order.intValue != priority)
        {
            changes.Add($"{tilemap.name}: navigation priority {order.intValue} -> {priority}");
            order.intValue = priority;
            data.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Sets the fill tile of every layer of the scene's <see cref="AreaTerrain"/>s whose tilemap is one of
    /// <paramref name="tilemaps"/> to the matching tile of <paramref name="tiles"/>.
    /// </summary>
    public static void SetFillTiles(Grid grid, IReadOnlyList<Tilemap> tilemaps, IReadOnlyList<TileBase> tiles, List<string> changes)
    {
        foreach (AreaTerrain terrain in SceneQuery.FindAll<AreaTerrain>(grid.gameObject.scene))
        {
            SerializedObject data = new(terrain);
            SerializedProperty layers = data.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                Object listed = layer.FindPropertyRelative("tilemap").objectReferenceValue;
                SerializedProperty fill = layer.FindPropertyRelative("fillTile");
                for (int j = 0; listed != null && j < tilemaps.Count; j++)
                {
                    if (listed == tilemaps[j] && fill.objectReferenceValue != tiles[j])
                    {
                        changes.Add($"{terrain.name}: fills {listed.name} with {Name(tiles[j])}");
                        fill.objectReferenceValue = tiles[j];
                    }
                }
            }

            data.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Whether <paramref name="tilemaps"/> holds <paramref name="candidate"/>.
    /// </summary>
    private static bool Contains(IReadOnlyList<Tilemap> tilemaps, Tilemap candidate)
    {
        for (int i = 0; i < tilemaps.Count; i++)
        {
            if (tilemaps[i] == candidate)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The name of <paramref name="asset"/>, or "none".
    /// </summary>
    private static string Name(Object asset)
    {
        return asset != null ? asset.name : "none";
    }
}
