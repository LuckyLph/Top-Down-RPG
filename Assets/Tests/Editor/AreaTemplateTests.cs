using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the area scene template (Assets/Settings/Scenes/AreaTemplate.unity, which AreaTemplate.scenetemplate
/// instantiates): its grid holds one ground layer, the grass, empty, first among its children and drawn by a terrain
/// renderer with the project's style and grass material, its navigation priority in step, then an empty collision
/// tilemap the navigation grid blocks; the area's terrain fills the grass with its tile, and the scene has empty Props
/// and MobSpawns roots and a start spawn point. Every other material's layer is added when an area needs it (the
/// Terrain Brush's button, the writer, Convert Ground; Docs/TerrainLayers32.md's Phase 4), so a new area lists only the
/// layers it uses. Each generator's layout can be written into it, the writer creating the generator's layers, after
/// which its renderer builds and its navigation grid blocks exactly the footprints, the water, except under a dock, the
/// ocean, lava and the cliff of raised ground. The template is opened beside the open scenes and closed without saving.
/// </summary>
public class AreaTemplateTests
{
    private const string TemplatePath = "Assets/Settings/Scenes/AreaTemplate.unity";
    private const string ProfilePath = "Assets/Settings/AI/TerrainMovement_Default.asset";
    private const string CollisionTilemapName = "CollisionTilemap";

    private static readonly string[] GeneratorPaths =
    {
        "Assets/Settings/Generation/Generator_Clearing.asset",
        "Assets/Settings/Generation/Generator_VillageForest.asset",
        "Assets/Settings/Generation/Generator_Lakeside.asset",
        "Assets/Settings/Generation/Generator_Snowfield.asset",
        "Assets/Settings/Generation/Generator_Coast.asset",
        "Assets/Settings/Generation/Generator_LavaFields.asset"
    };

    private Scene template;
    private int undoGroup;

    /// <summary>
    /// Opens the template beside the open scenes and starts the undo group its changes are reverted to.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        template = EditorSceneManager.OpenScene(TemplatePath, OpenSceneMode.Additive);
        Undo.IncrementCurrentGroup();
        undoGroup = Undo.GetCurrentGroup();
    }

    /// <summary>
    /// Reverts the test's undoable changes and closes the template without saving it.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Undo.RevertAllDownToGroup(undoGroup);
        if (template.IsValid() && template.isLoaded)
        {
            EditorSceneManager.CloseScene(template, true);
        }
    }

    /// <summary>
    /// The template's grid holds one ground layer, the grass, empty, first among its children, a data tilemap with the
    /// grass's terrain type on its navigation source, which the terrain renderer draws with the project's style and grass
    /// material, priorities in step; every other material's layer is added when an area needs it, so a new area lists
    /// only what it uses and stays narrow until it uses more than 16 materials. Every generator draws with that
    /// style. The navigation grid blocks the empty collision tilemap, the area's terrain fills the grass layer with its
    /// tile, and the scene has empty Props and MobSpawns roots and a start spawn point.
    /// </summary>
    [Test]
    public void TheTemplate_HoldsOnlyTheGrassLayer_Empty()
    {
        Grid grid = TemplateGrid();
        AreaGenerator lakeside = AssetDatabase.LoadAssetAtPath<AreaGenerator>(GeneratorPaths[2]);
        TerrainStyle style = lakeside.TerrainStyle;
        TerrainMaterial grass = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Grass.asset");
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.Style, Is.SameAs(style));
        Assert.That(renderer.Layers.Count, Is.EqualTo(1));
        Transform child = grid.transform.Find(GroundLayerMigration.TilemapName(grass));
        Assert.That(child, Is.Not.Null);
        Assert.That(child.GetSiblingIndex(), Is.Zero, "The grass layer comes first.");
        Tilemap tilemap = child.GetComponent<Tilemap>();
        Assert.That(tilemap.GetUsedTilesCount(), Is.Zero, "The grass layer is empty.");
        Assert.That(child.GetComponent<TilemapRenderer>(), Is.Null, "The grass layer is a data tilemap.");
        Assert.That(child.GetComponent<NavigationTerrainSource2D>().TerrainType, Is.SameAs(AssetDatabase.LoadAssetAtPath<TerrainType2D>(GroundLayerMigration.TerrainTypePath(grass))));
        Assert.That(renderer.Layers[0].Tilemap, Is.SameAs(tilemap));
        Assert.That(renderer.Layers[0].Material, Is.SameAs(grass));
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        foreach (string generatorPath in GeneratorPaths)
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath).TerrainStyle, Is.SameAs(style), generatorPath);
        }

        foreach (Transform layer in grid.transform)
        {
            Assert.That(layer == child || layer.name == CollisionTilemapName, Is.True, $"{layer.name} is no ground layer of the template");
        }

        Tilemap collision = grid.transform.Find(CollisionTilemapName).GetComponent<Tilemap>();
        Assert.That(collision.GetUsedTilesCount(), Is.Zero, "No walls");
        SerializedObject navigation = new(grid.GetComponent<NavigationGrid2D>());
        Assert.That(navigation.FindProperty("blockingTilemaps").GetArrayElementAtIndex(0).objectReferenceValue, Is.SameAs(collision));
        SerializedObject terrain = new(grid.GetComponent<AreaTerrain>());
        AreaGenerator clearing = AssetDatabase.LoadAssetAtPath<AreaGenerator>(GeneratorPaths[0]);
        Assert.That(terrain.FindProperty("layers").GetArrayElementAtIndex(0).FindPropertyRelative("fillTile").objectReferenceValue, Is.SameAs(clearing.TerrainLayers[0].Tile));
        Assert.That(Root("Props").transform.childCount, Is.Zero);
        Assert.That(Root(AreaLayoutWriter.MobSpawnsRootName).transform.childCount, Is.Zero);
        Assert.That(Root("SpawnPoint_start").GetComponent<SpawnPoint>(), Is.Not.Null);
    }

    /// <summary>
    /// Each project generator's layout can be written into the template: the writer creates the generator's layers,
    /// removes the empty grass layer when the generator does not list it, and the renderer draws exactly the generator's
    /// layers, in its order. The renderer then builds, and the navigation grid blocks exactly the footprint cells, the
    /// water, except under a dock, the ocean and lava, on the ground. A generator whose ground is not grass (the
    /// Snowfield's snow, the Lava Fields' dirt) first has its ground layer added
    /// (<see cref="GroundLayerMigration.AddLayer(Grid, AreaGenerator.TerrainLayer)"/>, as the Terrain Brush's button
    /// does) and the area's terrain pointed at it, as a new area of that ground has, or the template's empty grass layer,
    /// which the area's terrain lists, would be kept.
    /// </summary>
    [TestCase("Assets/Settings/Generation/Generator_Clearing.asset")]
    [TestCase("Assets/Settings/Generation/Generator_VillageForest.asset")]
    [TestCase("Assets/Settings/Generation/Generator_Lakeside.asset")]
    [TestCase("Assets/Settings/Generation/Generator_Snowfield.asset")]
    [TestCase("Assets/Settings/Generation/Generator_Coast.asset")]
    [TestCase("Assets/Settings/Generation/Generator_LavaFields.asset")]
    public void TheTemplate_TakesAGeneratedLayout_WithTheGeneratorsLayers(string generatorPath)
    {
        Grid grid = TemplateGrid();
        AreaGenerator generator = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        AreaLayout layout = generator.Generate(3);
        AreaGenerator.TerrainLayer ground = generator.TerrainLayers[0];
        Transform groundLayer = grid.transform.Find(ground.TilemapName);
        Tilemap groundTilemap = groundLayer != null ? groundLayer.GetComponent<Tilemap>() : GroundLayerMigration.AddLayer(grid, ground);
        Assert.That(groundTilemap, Is.Not.Null, ground.TilemapName);
        SerializedObject terrain = new(grid.GetComponent<AreaTerrain>());
        SerializedProperty groundEntry = terrain.FindProperty("layers").GetArrayElementAtIndex(0).FindPropertyRelative("tilemap");
        groundEntry.objectReferenceValue = groundTilemap;
        terrain.ApplyModifiedProperties();

        Assert.That(AreaLayoutWriter.Write(grid, generator, layout), Is.True);

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        IReadOnlyList<AreaGenerator.TerrainLayer> layers = generator.TerrainLayers;
        Assert.That(renderer.Layers.Count, Is.EqualTo(layers.Count));
        for (int i = 0; i < layers.Count; i++)
        {
            Assert.That(renderer.Layers[i].Tilemap.name, Is.EqualTo(layers[i].TilemapName), $"layer {i}");
            Assert.That(renderer.Layers[i].Material, Is.SameAs(layers[i].Material), $"layer {i}");
        }

        foreach (TerrainMaterial material in generator.TerrainStyle.LayerOrder)
        {
            if (layout.IndexOf(material) < 0)
            {
                Assert.That(grid.transform.Find($"{material.name}Tilemap"), Is.Null, $"{material.name}Tilemap is removed");
            }
        }

        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null, "The renderer builds.");
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.BuildGrid();
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        TerrainMaterial water = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Water.asset");
        TerrainMaterial planks = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Planks.asset");
        TerrainMaterial ocean = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Ocean.asset");
        TerrainMaterial lava = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/Lava.asset");
        HashSet<Vector3Int> blocked = layout.IndexOf(water) >= 0 ? new HashSet<Vector3Int>(layout.TerrainCells(layout.IndexOf(water)), CellComparer.Instance) : new HashSet<Vector3Int>(CellComparer.Instance);
        if (layout.IndexOf(ocean) >= 0)
        {
            blocked.UnionWith(layout.TerrainCells(layout.IndexOf(ocean)));
        }

        if (layout.IndexOf(lava) >= 0)
        {
            blocked.UnionWith(layout.TerrainCells(layout.IndexOf(lava)));
        }

        if (layout.IndexOf(planks) >= 0)
        {
            blocked.ExceptWith(layout.TerrainCells(layout.IndexOf(planks)));
        }
        foreach (AreaLayout.Prop prop in layout.Props)
        {
            if (PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
            {
                Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
                for (int dy = 0; dy < covered.y; dy++)
                {
                    for (int dx = 0; dx < covered.x; dx++)
                    {
                        blocked.Add(prop.Cell + new Vector3Int(dx, dy, 0));
                    }
                }
            }
        }

        foreach (Vector3Int cell in layout.Raised)
        {
            if (RaisedGround.IsCliff(layout.Raised, layout.Ramps, cell))
            {
                blocked.Add(cell);
            }
        }

        foreach (Vector3Int cell in layout.TerrainCells(0))
        {
            Assert.That(navigation.IsCellWalkable(cell, profile), Is.EqualTo(!blocked.Contains(cell)), $"cell {cell}");
        }
    }

    /// <summary>
    /// The template's grid.
    /// </summary>
    private Grid TemplateGrid()
    {
        Grid grid = Root("Grid").GetComponent<Grid>();
        Assert.That(grid.GetComponent<NavigationGrid2D>(), Is.Not.Null);
        return grid;
    }

    /// <summary>
    /// The template's root object named <paramref name="rootName"/>.
    /// </summary>
    private GameObject Root(string rootName)
    {
        foreach (GameObject root in template.GetRootGameObjects())
        {
            if (root.name == rootName)
            {
                return root;
            }
        }

        Assert.Fail($"The template has no {rootName}.");
        return null;
    }
}
