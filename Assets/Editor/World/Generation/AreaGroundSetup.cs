using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The steps <see cref="AreaLayoutWriter"/> and <see cref="GroundLayerMigration"/> share to set up an area's ground
/// layers the way the blended terrain draws them (Docs/TerrainBlending.md): plain data tilemaps without dual-grid parts
/// or a Tilemap Renderer, each with a navigation source whose priority is its draw order, drawn by the Grid's
/// <see cref="TerrainBlendRenderer"/>, and filled by the area's <see cref="AreaTerrain"/> with their plain tiles. Every
/// change goes through undo, a step whose target is already set changes and records nothing, and each change adds a
/// line to the caller's list.
/// </summary>
internal static class AreaGroundSetup
{
    /// <summary>
    /// Whether ground layers can be set up from <paramref name="layers"/> and <paramref name="style"/>: a style, at
    /// most <see cref="TerrainControlMap.MaxLayers"/> layers with distinct tilemap names, each with a tile and a
    /// material. Logs an error naming <paramref name="owner"/> for the first problem, pinging
    /// <paramref name="context"/>.
    /// </summary>
    public static bool CanSetUp(string owner, TerrainStyle style, IReadOnlyList<AreaGenerator.TerrainLayer> layers, Object context)
    {
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
    /// Removes what a ground layer no longer has from <paramref name="tilemap"/>: its
    /// <see cref="DualGridTilemapModule"/> with the module's render tilemap (the first child that has a Tilemap), and a
    /// Tilemap Renderer on the data tilemap itself, which would draw the tiles' palette swatches.
    /// </summary>
    public static void RemoveDualGridParts(Tilemap tilemap, List<string> changes)
    {
        if (tilemap.TryGetComponent(out DualGridTilemapModule module))
        {
            Tilemap render = module.RenderTilemap;
            if (render != null && render != tilemap)
            {
                changes.Add($"{tilemap.name}: removed its render tilemap {render.name}");
                Undo.DestroyObjectImmediate(render.gameObject);
            }

            changes.Add($"{tilemap.name}: removed its {nameof(DualGridTilemapModule)}");
            Undo.DestroyObjectImmediate(module);
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
    /// <paramref name="tilemaps"/>, bottom first, with <paramref name="materials"/> and <paramref name="style"/>. Its
    /// other settings (chunk size, sorting, look) stay as they are. Warns about each tilemap it drew before and no
    /// longer draws.
    /// </summary>
    public static TerrainBlendRenderer SetRenderer(Grid grid, TerrainStyle style, IReadOnlyList<Tilemap> tilemaps, IReadOnlyList<TerrainMaterial> materials, List<string> changes)
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
        for (int i = 0; i < layers.arraySize; i++)
        {
            Object listed = layers.GetArrayElementAtIndex(i).FindPropertyRelative("tilemap").objectReferenceValue;
            if (listed != null && !Contains(tilemaps, listed))
            {
                Debug.LogWarning($"{grid.name}: the {nameof(TerrainBlendRenderer)} no longer draws {listed.name}, which is not one of the ground layers.", renderer);
                changes.Add($"{grid.name}: stopped drawing {listed.name}");
            }
        }

        if (layers.arraySize != tilemaps.Count)
        {
            layers.arraySize = tilemaps.Count;
        }

        for (int i = 0; i < tilemaps.Count; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            SerializedProperty tilemap = layer.FindPropertyRelative("tilemap");
            SerializedProperty material = layer.FindPropertyRelative("material");
            if (tilemap.objectReferenceValue != tilemaps[i] || material.objectReferenceValue != materials[i])
            {
                changes.Add($"{grid.name}: draws {tilemaps[i].name} as layer {i} with {Name(materials[i])}");
                tilemap.objectReferenceValue = tilemaps[i];
                material.objectReferenceValue = materials[i];
            }
        }

        data.ApplyModifiedProperties();
        return renderer;
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
    private static bool Contains(IReadOnlyList<Tilemap> tilemaps, Object candidate)
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
