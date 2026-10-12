using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor window that generates the active area scene's content from an <see cref="AreaGenerator"/> asset. It shows
/// the generator's settings in place and a map of the layout they give, redrawn on every change; Random Seed picks a
/// new seed and Generate replaces the scene's terrain, props and mob spawns with that layout as one undo step, through
/// <see cref="AreaLayoutWriter"/>. The scene is left unsaved for review.
/// </summary>
public sealed class SceneGeneratorWindow : EditorWindow
{
    private const string MenuPath = ProjectScenes.MenuRoot + "World/Scene Generator";
    private const float PreviewHeight = 240f;
    private const int PreviewMargin = 2;

    private static readonly Color32 Background = new(24, 26, 30, 255);
    private static readonly Color32[] LayerColours =
    {
        new(78, 138, 56, 255),
        new(198, 170, 120, 255),
        new(150, 148, 142, 255),
        new(120, 110, 170, 255)
    };
    private static readonly Color32 BlockingColour = new(24, 66, 30, 255);
    private static readonly Color32 WalkThroughColour = new(150, 206, 110, 255);
    private static readonly Color32 MobColour = new(232, 64, 52, 255);
    private static readonly Color32 SpawnColour = new(70, 220, 255, 255);

    [SerializeField] private AreaGenerator generator;
    private Editor generatorEditor;
    private AreaLayout preview;
    private Texture2D previewTexture;
    private bool previewStale = true;
    private Grid cachedGrid;
    private Vector2 scroll;

    /// <summary>
    /// Opens the scene generator window.
    /// </summary>
    [MenuItem(MenuPath, priority = 70)]
    private static void Open()
    {
        GetWindow<SceneGeneratorWindow>("Scene Generator");
    }

    /// <summary>
    /// Picks the first generator asset when none is set and starts following undo and scene changes.
    /// </summary>
    private void OnEnable()
    {
        if (generator == null)
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(AreaGenerator)}");
            generator = guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<AreaGenerator>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        previewStale = true;
        Undo.undoRedoPerformed += MarkPreviewStale;
        EditorApplication.hierarchyChanged += ForgetGrid;
        EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
    }

    /// <summary>
    /// Stops following undo and scene changes and destroys the settings editor and the preview texture.
    /// </summary>
    private void OnDisable()
    {
        Undo.undoRedoPerformed -= MarkPreviewStale;
        EditorApplication.hierarchyChanged -= ForgetGrid;
        EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
        if (generatorEditor != null)
        {
            DestroyImmediate(generatorEditor);
        }

        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
        }
    }

    /// <summary>
    /// Draws the target scene, the generator picker, the buttons, the layout preview with its counts and warnings, and
    /// the generator's settings.
    /// </summary>
    private void OnGUI()
    {
        Grid grid = TargetGrid();
        if (grid == null)
        {
            EditorGUILayout.HelpBox("Make an area scene active: the generator writes into the active scene's Grid.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("Area", grid.gameObject.scene.name);
        }

        EditorGUI.BeginChangeCheck();
        generator = (AreaGenerator)EditorGUILayout.ObjectField("Generator", generator, typeof(AreaGenerator), false);
        if (EditorGUI.EndChangeCheck())
        {
            previewStale = true;
        }

        if (generator == null)
        {
            EditorGUILayout.HelpBox("Pick a generator asset, or create one under Assets > Create > TopDownRPG > World.", MessageType.Info);
            return;
        }

        DrawButtons(grid);
        DrawPreview();

        Editor.CreateCachedEditor(generator, null, ref generatorEditor);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUI.BeginChangeCheck();
        generatorEditor.OnInspectorGUI();
        if (EditorGUI.EndChangeCheck())
        {
            previewStale = true;
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Draws Random Seed, which gives the generator a new seed with undo, and Generate, which is disabled without a
    /// target and generates into <paramref name="grid"/>'s scene once this GUI pass is over.
    /// </summary>
    private void DrawButtons(Grid grid)
    {
        using EditorGUILayout.HorizontalScope row = new();
        if (GUILayout.Button("Random Seed"))
        {
            SerializedObject settings = new(generator);
            settings.FindProperty("seed").intValue = Random.Range(1, int.MaxValue);
            settings.ApplyModifiedProperties();
            MarkPreviewStale();
        }

        using (new EditorGUI.DisabledScope(grid == null))
        {
            if (GUILayout.Button("Generate"))
            {
                AreaGenerator target = generator;
                EditorApplication.delayCall += () => GenerateInto(grid, target);
            }

            if (GUILayout.Button(new GUIContent("Convert Ground", "Sets the scene's ground layers up as the generator's layers, drawn by the blended terrain, keeping every painted cell; nothing is generated.")))
            {
                AreaGenerator target = generator;
                EditorApplication.delayCall += () => ConvertGround(grid, target);
            }
        }
    }

    /// <summary>
    /// Asks whether to convert the ground layers of <paramref name="grid"/>'s scene to <paramref name="target"/>'s
    /// terrain layers without repainting them, then runs <see cref="GroundLayerMigration"/> and logs what changed.
    /// </summary>
    private static void ConvertGround(Grid grid, AreaGenerator target)
    {
        if (grid == null || target == null)
        {
            return;
        }

        string sceneName = grid.gameObject.scene.name;
        if (!EditorUtility.DisplayDialog(
            "Convert ground",
            $"Set the ground layers of {sceneName} up as {target.name}'s terrain layers, drawn by the blended terrain? Every painted cell stays, dual-grid parts are removed, and props, mobs and walls are left alone. Undo reverts it.",
            "Convert",
            "Cancel"))
        {
            return;
        }

        List<string> changes = GroundLayerMigration.Migrate(grid, target);
        if (changes != null)
        {
            Debug.Log(changes.Count == 0 ? $"{sceneName}'s ground already matches {target.name}." : $"Converted {sceneName}'s ground to {target.name}:\n{string.Join("\n", changes)}", grid);
        }
    }

    /// <summary>
    /// Asks whether to replace the content of <paramref name="grid"/>'s scene, then writes
    /// <paramref name="target"/>'s layout for its seed into it and logs what was placed.
    /// </summary>
    private void GenerateInto(Grid grid, AreaGenerator target)
    {
        if (grid == null || target == null)
        {
            return;
        }

        string sceneName = grid.gameObject.scene.name;
        if (!EditorUtility.DisplayDialog(
            "Generate area",
            $"Replace the terrain, props and mob spawns of {sceneName} with {target.name}'s layout for seed {target.Seed}? Undo reverts it.",
            "Generate",
            "Cancel"))
        {
            return;
        }

        AreaLayout layout = target.Generate(target.Seed);
        if (AreaLayoutWriter.Write(grid, target, layout))
        {
            Debug.Log($"Generated {sceneName} from {target.name} (seed {target.Seed}): {Summary(target, layout)}.", grid);
        }
    }

    /// <summary>
    /// Draws the map of the current layout, rebuilt during the layout pass when the settings changed, with its counts
    /// and the generator's warnings.
    /// </summary>
    private void DrawPreview()
    {
        if (preview == null || (previewStale && Event.current.type == EventType.Layout))
        {
            RebuildPreview();
        }

        Rect area = GUILayoutUtility.GetRect(position.width - 8f, PreviewHeight);
        EditorGUI.DrawRect(area, Background);
        GUI.DrawTexture(area, previewTexture, ScaleMode.ScaleToFit);
        EditorGUILayout.LabelField(Summary(generator, preview), EditorStyles.miniLabel);
        foreach (string warning in preview.Warnings)
        {
            EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }
    }

    /// <summary>
    /// Generates the layout for the current settings and seed and paints it one pixel per cell: terrain layers in
    /// order, blocking footprints, walk-through props, mob spawns and the party spawn.
    /// </summary>
    private void RebuildPreview()
    {
        previewStale = false;
        preview = generator.Generate(generator.Seed);
        BoundsInt bounds = TerrainBounds(preview);
        int width = bounds.size.x + 2 * PreviewMargin;
        int height = bounds.size.y + 2 * PreviewMargin;
        if (previewTexture == null || previewTexture.width != width || previewTexture.height != height)
        {
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
            }

            previewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        Color32[] pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Background;
        }

        Vector3Int origin = bounds.min - new Vector3Int(PreviewMargin, PreviewMargin, 0);
        for (int layer = 0; layer < preview.TerrainLayerCount; layer++)
        {
            Color32 colour = LayerColours[Mathf.Min(layer, LayerColours.Length - 1)];
            foreach (Vector3Int cell in preview.TerrainCells(layer))
            {
                Paint(pixels, width, height, cell - origin, colour);
            }
        }

        foreach (AreaLayout.Prop prop in preview.Props)
        {
            if (PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
            {
                Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
                for (int dy = 0; dy < covered.y; dy++)
                {
                    for (int dx = 0; dx < covered.x; dx++)
                    {
                        Paint(pixels, width, height, prop.Cell + new Vector3Int(dx, dy, 0) - origin, BlockingColour);
                    }
                }
            }
            else
            {
                Paint(pixels, width, height, prop.Cell - origin, WalkThroughColour);
            }
        }

        foreach (AreaLayout.Mob mob in preview.Mobs)
        {
            Paint(pixels, width, height, mob.Cell - origin, MobColour);
        }

        Paint(pixels, width, height, preview.SpawnCell - origin, SpawnColour);
        previewTexture.SetPixels32(pixels);
        previewTexture.Apply();
    }

    /// <summary>
    /// Sets the pixel for <paramref name="cell"/>, ignoring cells outside the texture.
    /// </summary>
    private static void Paint(Color32[] pixels, int width, int height, Vector3Int cell, Color32 colour)
    {
        if (cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height)
        {
            pixels[cell.y * width + cell.x] = colour;
        }
    }

    /// <summary>
    /// The bounds of every terrain cell in <paramref name="layout"/>, or a one-cell bounds at the origin when it has
    /// none.
    /// </summary>
    private static BoundsInt TerrainBounds(AreaLayout layout)
    {
        bool any = false;
        Vector3Int min = Vector3Int.zero;
        Vector3Int max = Vector3Int.zero;
        for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
        {
            foreach (Vector3Int cell in layout.TerrainCells(layer))
            {
                min = any ? Vector3Int.Min(min, cell) : cell;
                max = any ? Vector3Int.Max(max, cell) : cell;
                any = true;
            }
        }

        return new BoundsInt(min, max - min + Vector3Int.one);
    }

    /// <summary>
    /// The cell counts of each of <paramref name="source"/>'s terrain layers and the prop and mob counts of
    /// <paramref name="layout"/>.
    /// </summary>
    private static string Summary(AreaGenerator source, AreaLayout layout)
    {
        IReadOnlyList<AreaGenerator.TerrainLayer> layers = source.TerrainLayers;
        List<string> parts = new();
        for (int i = 0; i < layout.TerrainLayerCount; i++)
        {
            string label = i < layers.Count && layers[i] != null ? layers[i].TilemapName : $"layer {i}";
            parts.Add($"{label} {layout.TerrainCells(i).Count}");
        }

        int blocking = 0;
        foreach (AreaLayout.Prop prop in layout.Props)
        {
            blocking += PropGridSnapping.TryGetFootprint(prop.Prefab, out _) ? 1 : 0;
        }

        return $"{string.Join(", ", parts)} cells, {layout.Props.Count} props ({blocking} blocking), {layout.Mobs.Count} mobs";
    }

    /// <summary>
    /// The active scene's area grid: the one carrying its <see cref="NavigationGrid2D"/>, else its first
    /// <see cref="Grid"/>. Cached until the hierarchy or the active scene changes.
    /// </summary>
    private Grid TargetGrid()
    {
        if (cachedGrid != null)
        {
            return cachedGrid;
        }

        Scene scene = SceneManager.GetActiveScene();
        NavigationGrid2D navigation = PropPlacementWindow.FindGrid(scene);
        cachedGrid = navigation != null ? navigation.GetComponentInParent<Grid>() : null;
        if (cachedGrid == null && scene.IsValid() && scene.isLoaded)
        {
            cachedGrid = SceneQuery.FindFirst<Grid>(scene);
        }

        return cachedGrid;
    }

    /// <summary>
    /// Forgets the cached grid so the next lookup finds it again, and redraws.
    /// </summary>
    private void ForgetGrid()
    {
        cachedGrid = null;
        Repaint();
    }

    /// <summary>
    /// Forgets the cached grid when another scene becomes active.
    /// </summary>
    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        ForgetGrid();
    }

    /// <summary>
    /// Rebuilds the preview after an undo or redo, which may have changed the generator's settings.
    /// </summary>
    private void MarkPreviewStale()
    {
        previewStale = true;
        Repaint();
    }
}
