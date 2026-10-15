using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The steps <see cref="AreaLayoutWriter"/> and <see cref="GroundLayerMigration"/> share to set up an area's ground
/// layers the way the blended terrain draws them (Docs/TerrainBlending.md): plain data tilemaps without dual-grid parts
/// or a Tilemap Renderer, first among the Grid's children in draw order (created when missing), each with a navigation
/// source whose priority is its draw order, drawn by the Grid's <see cref="TerrainBlendRenderer"/> in the order of the
/// style's <see cref="TerrainStyle.LayerOrder"/>, and filled by the area's <see cref="AreaTerrain"/> with their plain
/// tiles. A layer the area draws that the generator does not list is kept, at its material's place in that order, when
/// it is painted or <see cref="AreaTerrain"/> may edit it, and removed otherwise (Docs/TerrainTypes.md, Phase 2). Every
/// change goes through undo, a step whose target is already set changes and records nothing, and each change adds a
/// line to the caller's list. Only outside play mode, where the changes can be saved.
/// </summary>
internal static class AreaGroundSetup
{
    /// <summary>
    /// One layer of the area as it will be drawn: a generator's layer, or a layer the area drew that the generator does
    /// not list, with its tilemap, its material and its material's place in the style's layer order.
    /// </summary>
    private sealed class PlannedLayer
    {
        public AreaGenerator.TerrainLayer Layer;
        public Tilemap Tilemap;
        public TerrainMaterial Material;
        public int Rank;
    }

    /// <summary>
    /// Whether ground layers can be set up from <paramref name="layers"/> and <paramref name="style"/>: outside play
    /// mode (a change to the scene's play-mode copy would be lost when play stops) and with no <see cref="Problem"/>.
    /// Logs an error for the first problem, pinging <paramref name="context"/>.
    /// </summary>
    public static bool CanSetUp(string owner, TerrainStyle style, IReadOnlyList<AreaGenerator.TerrainLayer> layers, Object context)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{owner}: ground layers are set up outside play mode; a change to the scene during play is lost when play stops.", context);
            return false;
        }

        string problem = Problem(owner, style, layers);
        if (problem != null)
        {
            Debug.LogError(problem, context);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The first reason ground layers cannot be set up from <paramref name="layers"/> and <paramref name="style"/>, as a
    /// message naming <paramref name="owner"/>, or null when they can: a style, at most
    /// <see cref="TerrainControlMap.MaxLayers"/> layers with distinct tilemap names, each with a tile and a material,
    /// and their materials a subsequence of the style's layer order (each listed there, in that order, none twice).
    /// </summary>
    public static string Problem(string owner, TerrainStyle style, IReadOnlyList<AreaGenerator.TerrainLayer> layers)
    {
        if (style == null)
        {
            return $"{owner}: no terrain style for the area's {nameof(TerrainBlendRenderer)}.";
        }

        if (layers.Count > TerrainControlMap.MaxLayers)
        {
            return $"{owner}: {layers.Count} terrain layers, but the blended terrain draws at most {TerrainControlMap.MaxLayers}.";
        }

        int previousRank = -1;
        for (int i = 0; i < layers.Count; i++)
        {
            AreaGenerator.TerrainLayer layer = layers[i];
            if (layer == null || string.IsNullOrWhiteSpace(layer.TilemapName) || layer.Tile == null || layer.Material == null)
            {
                return $"{owner}: terrain layer {i} needs a tilemap name, a tile and a material.";
            }

            for (int j = 0; j < i; j++)
            {
                if (layers[j].TilemapName == layer.TilemapName)
                {
                    return $"{owner}: terrain layers {j} and {i} both name {layer.TilemapName}; each tilemap is one layer.";
                }

                if (layers[j].Material == layer.Material)
                {
                    return $"{owner}: terrain layers {j} and {i} are both drawn with {layer.Material.name}; an area draws each material once.";
                }
            }

            int rank = style.LayerOrderIndexOf(layer.Material);
            if (rank < 0)
            {
                return $"{owner}: terrain layer {i} is drawn with {layer.Material.name}, which {style.name}'s layer order does not list; add it there at its place in the draw order (Docs/TerrainTypes.md).";
            }

            if (rank < previousRank)
            {
                return $"{owner}: terrain layer {i} ({layer.Material.name}) comes before {layers[i - 1].Material.name} in {style.name}'s layer order, so it cannot be listed over it; list the layers in that order.";
            }

            previousRank = rank;
        }

        return null;
    }

    /// <summary>
    /// Sets up <paramref name="layers"/> as the ground layers of <paramref name="grid"/>, drawn with
    /// <paramref name="style"/>, and returns their tilemaps in the same order, for the caller to fill. Each layer is
    /// prepared (<see cref="PrepareLayer"/>, created empty when missing). Every other tilemap the grid's renderer drew
    /// stays drawn when it is painted or the area's <see cref="AreaTerrain"/> lists it, with its material, placed among
    /// the layers by its material's place in the style's layer order (above them all when the order does not list its
    /// material), and a warning names it, since <paramref name="owner"/> does not list it; an empty one stops being
    /// drawn and its tilemap, when a child of the grid, is removed. Past <see cref="TerrainControlMap.MaxLayers"/>
    /// layers the kept ones from the top stop being drawn, with a warning. Every layer drawn comes first among the
    /// grid's children in draw order, with its index as navigation priority, and the renderer draws exactly them, in
    /// that order. The removals are recorded first, then the moves among the grid's children, then the navigation
    /// priorities, then the renderer: an undo reverts them in reverse, and restoring a removed or moved child rebuilds
    /// the renderer, which then finds its old layers and their old priorities.
    /// </summary>
    public static Tilemap[] SetUpLayers(Grid grid, TerrainStyle style, IReadOnlyList<AreaGenerator.TerrainLayer> layers, string owner, List<string> changes)
    {
        List<PlannedLayer> plan = new();
        foreach (AreaGenerator.TerrainLayer layer in layers)
        {
            plan.Add(new PlannedLayer { Layer = layer, Material = layer.Material, Rank = style.LayerOrderIndexOf(layer.Material) });
        }

        List<Tilemap> removed = new();
        foreach (PlannedLayer other in OtherDrawnLayers(grid, layers))
        {
            if (!IsPainted(other.Tilemap) && !IsEditedAtRuntime(grid, other.Tilemap))
            {
                removed.Add(other.Tilemap);
                continue;
            }

            int place = plan.Count;
            other.Rank = style.LayerOrderIndexOf(other.Material);
            if (other.Rank < 0)
            {
                other.Rank = int.MaxValue;
                Debug.LogWarning($"{grid.gameObject.scene.name}: {owner} does not list {other.Tilemap.name}, which is painted or edited at runtime, so it is kept; {style.name}'s layer order does not list its material {Name(other.Material)}, so it is drawn above the other layers. Add the material to the layer order.", grid);
            }
            else
            {
                while (place > 0 && plan[place - 1].Rank > other.Rank)
                {
                    place--;
                }

                Debug.LogWarning($"{grid.gameObject.scene.name}: {owner} does not list {Name(other.Material)}, but {other.Tilemap.name} is painted or edited at runtime, so it is kept, drawn at its place in {style.name}'s layer order.", grid);
            }

            plan.Insert(place, other);
        }

        for (int i = plan.Count - 1; plan.Count > TerrainControlMap.MaxLayers && i >= 0; i--)
        {
            if (plan[i].Layer == null)
            {
                Debug.LogWarning($"{grid.name}: the {nameof(TerrainBlendRenderer)} no longer draws {plan[i].Tilemap.name}: it draws at most {TerrainControlMap.MaxLayers} layers. {plan[i].Tilemap.name}'s navigation source still counts.", grid);
                changes.Add($"{grid.name}: stopped drawing {plan[i].Tilemap.name}");
                plan.RemoveAt(i);
            }
        }

        foreach (Tilemap tilemap in removed)
        {
            if (tilemap.transform.parent == grid.transform)
            {
                changes.Add($"{grid.name}: removed {tilemap.name}: it is empty and {owner} does not list it");
                Undo.DestroyObjectImmediate(tilemap.gameObject);
            }
        }

        Tilemap[] tilemaps = new Tilemap[layers.Count];
        int sibling = 0;
        foreach (PlannedLayer planned in plan)
        {
            if (planned.Layer != null)
            {
                planned.Tilemap = PrepareLayer(grid, planned.Layer, sibling, changes);
                tilemaps[IndexOf(layers, planned.Layer)] = planned.Tilemap;
                sibling++;
            }
            else if (planned.Tilemap.transform.parent == grid.transform)
            {
                SetSiblingIndex(planned.Tilemap.transform, sibling, changes);
                sibling++;
            }
        }

        for (int i = 0; i < plan.Count; i++)
        {
            if (plan[i].Layer != null)
            {
                SetNavigationSource(plan[i].Tilemap, plan[i].Layer.TerrainType, TerrainBlendRenderer.NavigationPriorityOf(i), changes);
            }
            else
            {
                SetNavigationPriority(plan[i].Tilemap, TerrainBlendRenderer.NavigationPriorityOf(i), changes);
            }
        }

        SetRenderer(grid, style, plan, changes);
        return tilemaps;
    }

    /// <summary>
    /// The layers the grid's renderer draws whose tilemap is none of <paramref name="layers"/>' (the grid's child named
    /// after one), each once, in the renderer's order, with the material it draws them with. Entries without a tilemap
    /// are left out.
    /// </summary>
    private static List<PlannedLayer> OtherDrawnLayers(Grid grid, IReadOnlyList<AreaGenerator.TerrainLayer> layers)
    {
        List<PlannedLayer> others = new();
        if (!grid.TryGetComponent(out TerrainBlendRenderer renderer))
        {
            return others;
        }

        foreach (TerrainBlendRenderer.Layer drawn in renderer.Layers)
        {
            Tilemap tilemap = drawn != null ? drawn.Tilemap : null;
            if (tilemap == null || IsOneOf(grid, layers, tilemap) || others.Exists(other => other.Tilemap == tilemap))
            {
                continue;
            }

            others.Add(new PlannedLayer { Tilemap = tilemap, Material = drawn.Material });
        }

        return others;
    }

    /// <summary>
    /// Whether <paramref name="tilemap"/> is the grid's child named after one of <paramref name="layers"/>.
    /// </summary>
    private static bool IsOneOf(Grid grid, IReadOnlyList<AreaGenerator.TerrainLayer> layers, Tilemap tilemap)
    {
        foreach (AreaGenerator.TerrainLayer layer in layers)
        {
            Transform child = grid.transform.Find(layer.TilemapName);
            if (child != null && child == tilemap.transform)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any cell of <paramref name="tilemap"/> holds a tile.
    /// </summary>
    private static bool IsPainted(Tilemap tilemap)
    {
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.HasTile(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an <see cref="AreaTerrain"/> in the grid's scene lists <paramref name="tilemap"/>, so gameplay may paint
    /// it however empty it is saved.
    /// </summary>
    private static bool IsEditedAtRuntime(Grid grid, Tilemap tilemap)
    {
        foreach (AreaTerrain terrain in SceneQuery.FindAll<AreaTerrain>(grid.gameObject.scene))
        {
            SerializedProperty listed = new SerializedObject(terrain).FindProperty("layers");
            for (int i = 0; i < listed.arraySize; i++)
            {
                if (listed.GetArrayElementAtIndex(i).FindPropertyRelative("tilemap").objectReferenceValue == tilemap)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The index of <paramref name="layer"/> in <paramref name="layers"/>.
    /// </summary>
    private static int IndexOf(IReadOnlyList<AreaGenerator.TerrainLayer> layers, AreaGenerator.TerrainLayer layer)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] == layer)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The grid's child tilemap named after <paramref name="layer"/>, created empty when missing, as a plain data
    /// tilemap at place <paramref name="sibling"/> among the grid's children, any dual-grid parts or Tilemap Renderer
    /// removed. <see cref="SetUpLayers"/> sets its navigation source afterwards.
    /// </summary>
    private static Tilemap PrepareLayer(Grid grid, AreaGenerator.TerrainLayer layer, int sibling, List<string> changes)
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
        SetSiblingIndex(tilemap.transform, sibling, changes);

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
    /// Adds the <see cref="TerrainBlendRenderer"/> of <paramref name="grid"/> when it has none and has it draw exactly
    /// the tilemaps of <paramref name="plan"/>, bottom first, with their materials and <paramref name="style"/>. Its
    /// other settings (chunk size, sorting, look) stay as they are.
    /// </summary>
    private static void SetRenderer(Grid grid, TerrainStyle style, List<PlannedLayer> plan, List<string> changes)
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
        if (layers.arraySize != plan.Count)
        {
            changes.Add($"{grid.name}: draws {plan.Count} layers instead of {layers.arraySize}");
            layers.arraySize = plan.Count;
        }

        for (int i = 0; i < plan.Count; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            SerializedProperty tilemap = layer.FindPropertyRelative("tilemap");
            SerializedProperty material = layer.FindPropertyRelative("material");
            if (tilemap.objectReferenceValue != plan[i].Tilemap || material.objectReferenceValue != plan[i].Material)
            {
                changes.Add(plan[i].Layer != null ? $"{grid.name}: draws {plan[i].Tilemap.name} as layer {i} with {Name(plan[i].Material)}" : $"{grid.name}: keeps drawing {plan[i].Tilemap.name}, now as layer {i}");
                tilemap.objectReferenceValue = plan[i].Tilemap;
                material.objectReferenceValue = plan[i].Material;
            }
        }

        data.ApplyModifiedProperties();
    }

    /// <summary>
    /// Puts <paramref name="child"/> at place <paramref name="index"/> among its parent's children.
    /// </summary>
    private static void SetSiblingIndex(Transform child, int index, List<string> changes)
    {
        if (child.GetSiblingIndex() != index)
        {
            Undo.SetSiblingIndex(child, index, $"Order {child.name}");
            changes.Add($"{child.name}: moved to place {index} under {child.parent.name}");
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
    /// Sets up <paramref name="grid"/>'s raised tilemap, the child named <see cref="AreaGenerator.RaisedTilemapName"/>,
    /// for a generator with raised ground: created empty when missing, a plain tilemap after the ground layers, and set
    /// on the grid's terrain renderer, which draws it, and on its navigation grid, which blocks its cliff
    /// (<see cref="RaisedGround.IsCliff"/>); each step recorded for undo and listed in <paramref name="changes"/>, and
    /// nothing recorded when already so. Returns the tilemap.
    /// </summary>
    public static Tilemap SetUpRaised(Grid grid, List<string> changes)
    {
        Transform child = grid.transform.Find(AreaGenerator.RaisedTilemapName);
        Tilemap raised = child != null ? child.GetComponent<Tilemap>() : null;
        if (raised == null)
        {
            GameObject created = new(AreaGenerator.RaisedTilemapName, typeof(Tilemap));
            Undo.RegisterCreatedObjectUndo(created, "Create raised tilemap");
            Undo.SetTransformParent(created.transform, grid.transform, "Create raised tilemap");
            raised = created.GetComponent<Tilemap>();
            changes.Add($"Created {AreaGenerator.RaisedTilemapName}.");
        }

        if (grid.TryGetComponent(out TerrainBlendRenderer renderer) && renderer.RaisedTilemap != raised)
        {
            SerializedObject serialized = new(renderer);
            serialized.FindProperty("raisedTilemap").objectReferenceValue = raised;
            serialized.ApplyModifiedProperties();
            changes.Add($"The terrain renderer draws {raised.name}.");
        }

        if (grid.TryGetComponent(out NavigationGrid2D navigation) && navigation.RaisedTilemap != raised)
        {
            SerializedObject serialized = new(navigation);
            serialized.FindProperty("raisedTilemap").objectReferenceValue = raised;
            serialized.ApplyModifiedProperties();
            changes.Add($"The navigation grid blocks the cliff of {raised.name}.");
        }

        return raised;
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
    /// The name of <paramref name="asset"/>, or "none".
    /// </summary>
    private static string Name(Object asset)
    {
        return asset != null ? asset.name : "none";
    }
}
