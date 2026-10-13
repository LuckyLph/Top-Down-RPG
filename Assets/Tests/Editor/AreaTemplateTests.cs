using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the area scene template (Assets/Settings/Scenes/AreaTemplate.unity, which AreaTemplate.scenetemplate
/// instantiates): its grid is set up as every generator expects, with the generators' six ground layers empty, first
/// among its children and drawn in their order by a terrain renderer with the project's style and materials, their
/// navigation priorities in step, an empty collision tilemap the navigation grid blocks and the area's terrain lists,
/// empty Props and MobSpawns roots and a start spawn point; and a generated layout can be written into it, after which
/// its renderer builds and its navigation grid blocks exactly the footprints and the water. The template is opened
/// beside the open scenes and closed without saving.
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
        "Assets/Settings/Generation/Generator_Lakeside.asset"
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
    /// The template's grid lists every generator's ground layers, empty and first among its children in the
    /// generators' order, each with its terrain type's navigation source; its terrain renderer draws them in that order
    /// with each layer's material and the generators' style, priorities in step; the navigation grid blocks the empty
    /// collision tilemap, the area's terrain fills the grass layer with its tile; and the scene has empty Props and
    /// MobSpawns roots and a start spawn point.
    /// </summary>
    [Test]
    public void TheTemplate_SetsUpTheGeneratorsGroundLayers_Empty()
    {
        Grid grid = TemplateGrid();
        foreach (string generatorPath in GeneratorPaths)
        {
            AreaGenerator generator = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
            IReadOnlyList<AreaGenerator.TerrainLayer> layers = generator.TerrainLayers;
            TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.Style, Is.SameAs(generator.TerrainStyle), generatorPath);
            Assert.That(renderer.Layers.Count, Is.EqualTo(layers.Count), generatorPath);
            for (int i = 0; i < layers.Count; i++)
            {
                Transform child = grid.transform.Find(layers[i].TilemapName);
                Assert.That(child, Is.Not.Null, layers[i].TilemapName);
                Assert.That(child.GetSiblingIndex(), Is.EqualTo(i), $"{layers[i].TilemapName} at its place");
                Tilemap tilemap = child.GetComponent<Tilemap>();
                Assert.That(tilemap.GetUsedTilesCount(), Is.Zero, $"{layers[i].TilemapName} is empty");
                Assert.That(child.GetComponent<TilemapRenderer>(), Is.Null, $"{layers[i].TilemapName} is a data tilemap");
                Assert.That(child.GetComponent<NavigationTerrainSource2D>().TerrainType, Is.SameAs(layers[i].TerrainType), layers[i].TilemapName);
                Assert.That(renderer.Layers[i].Tilemap, Is.SameAs(tilemap), $"{generatorPath} layer {i}");
                Assert.That(renderer.Layers[i].Material, Is.SameAs(layers[i].Material), $"{generatorPath} layer {i}");
            }

            Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
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
    /// Each project generator's layout can be written into the template: its renderer then builds, and its navigation
    /// grid blocks exactly the footprint cells and the water on the grass.
    /// </summary>
    [TestCase("Assets/Settings/Generation/Generator_Clearing.asset")]
    [TestCase("Assets/Settings/Generation/Generator_Lakeside.asset")]
    public void TheTemplate_TakesAGeneratedLayout(string generatorPath)
    {
        Grid grid = TemplateGrid();
        AreaGenerator generator = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        AreaLayout layout = generator.Generate(3);

        Assert.That(AreaLayoutWriter.Write(grid, generator, layout), Is.True);

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null, "The renderer builds.");
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.BuildGrid();
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        HashSet<Vector3Int> blocked = new(layout.TerrainCells(AreaLayoutBuilder.WaterLayer));
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

        foreach (Vector3Int cell in layout.TerrainCells(AreaLayoutBuilder.GrassLayer))
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
