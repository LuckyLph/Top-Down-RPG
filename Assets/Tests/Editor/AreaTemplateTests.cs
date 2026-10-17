using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the area scene template (Assets/Settings/Scenes/AreaTemplate.unity, which AreaTemplate.scenetemplate
/// instantiates): its grid holds a ground layer for every material of the project style's layer order but those it
/// leaves out (the ocean and lava, since a renderer draws at most 16 layers), empty, first among its children and drawn in that order
/// by a terrain renderer with the project's style and materials, their
/// navigation priorities in step, an empty collision tilemap the navigation grid blocks and the area's terrain lists,
/// empty Props and MobSpawns roots and a start spawn point; and each generator's layout can be written into it, which
/// trims it to the generator's layers, after which its renderer builds and its navigation grid blocks exactly the
/// footprints, the water, except under a dock, the ocean, lava and the cliff of raised ground; a generator that lists
/// the ocean or lava, which the template leaves out, gets its layer from the writer, and one with raised ground its raised tilemap. The template is opened
/// beside the open scenes and closed without saving.
/// </summary>
public class AreaTemplateTests
{
    private const string TemplatePath = "Assets/Settings/Scenes/AreaTemplate.unity";
    private const string ProfilePath = "Assets/Settings/AI/TerrainMovement_Default.asset";
    private const string CollisionTilemapName = "CollisionTilemap";

    private static readonly string[] LeftOut = { "Ocean", "Lava" };

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
    /// The template's grid holds a ground layer for every material of the project style's layer order but the ocean and
    /// lava, which it leaves out since a renderer draws at most 16 layers (a generator that lists either adds its layer
    /// when it writes),
    /// empty, first among its children in that order, each named after its material with that material's terrain type
    /// on its navigation source, so a new area can be painted with almost anything; its terrain renderer draws them in
    /// that order with the style, priorities in step, and every generator's layers but the left-out ones are among them. The navigation grid blocks the
    /// empty collision tilemap, the area's terrain fills the grass layer with its tile, and the scene has empty Props
    /// and MobSpawns roots and a start spawn point.
    /// </summary>
    [Test]
    public void TheTemplate_HoldsALayerForEveryMaterialOfTheSharedOrder_Empty()
    {
        Grid grid = TemplateGrid();
        AreaGenerator lakeside = AssetDatabase.LoadAssetAtPath<AreaGenerator>(GeneratorPaths[2]);
        TerrainStyle style = lakeside.TerrainStyle;
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.Style, Is.SameAs(style));
        List<TerrainMaterial> held = new();
        foreach (TerrainMaterial material in style.LayerOrder)
        {
            if (System.Array.IndexOf(LeftOut, material.name) < 0)
            {
                held.Add(material);
            }
            else
            {
                Assert.That(grid.transform.Find($"{material.name}Tilemap"), Is.Null, $"{material.name} is left out");
            }
        }

        Assert.That(held.Count, Is.EqualTo(style.LayerOrder.Count - LeftOut.Length), "every left-out material is in the order");
        Assert.That(held.Count, Is.LessThanOrEqualTo(TerrainControlMap.MaxLayers));
        Assert.That(renderer.Layers.Count, Is.EqualTo(held.Count));
        for (int i = 0; i < held.Count; i++)
        {
            TerrainMaterial material = held[i];
            Transform child = grid.transform.Find($"{material.name}Tilemap");
            Assert.That(child, Is.Not.Null, material.name);
            Assert.That(child.GetSiblingIndex(), Is.EqualTo(i), $"{child.name} at its place");
            Tilemap tilemap = child.GetComponent<Tilemap>();
            Assert.That(tilemap.GetUsedTilesCount(), Is.Zero, $"{child.name} is empty");
            Assert.That(child.GetComponent<TilemapRenderer>(), Is.Null, $"{child.name} is a data tilemap");
            Assert.That(child.GetComponent<NavigationTerrainSource2D>().TerrainType, Is.SameAs(AssetDatabase.LoadAssetAtPath<TerrainType2D>($"Assets/Settings/AI/Terrain_{material.name}.asset")), child.name);
            Assert.That(renderer.Layers[i].Tilemap, Is.SameAs(tilemap), $"layer {i}");
            Assert.That(renderer.Layers[i].Material, Is.SameAs(material), $"layer {i}");
        }

        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        foreach (string generatorPath in GeneratorPaths)
        {
            AreaGenerator generator = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
            Assert.That(generator.TerrainStyle, Is.SameAs(style), generatorPath);
            foreach (AreaGenerator.TerrainLayer layer in generator.TerrainLayers)
            {
                if (System.Array.IndexOf(LeftOut, layer.Material.name) >= 0)
                {
                    continue;
                }

                Assert.That(renderer.LayerIndexOf(grid.transform.Find(layer.TilemapName).GetComponent<Tilemap>()), Is.GreaterThanOrEqualTo(0), $"{generatorPath}: {layer.TilemapName}");
            }
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
    /// Each project generator's layout can be written into the template, which the generator trims to its own layers:
    /// the layers it does not list, all empty, are removed, and the renderer draws exactly the generator's, in its
    /// order. The renderer then builds, and the navigation grid blocks exactly the footprint cells, the water, except
    /// under a dock, the ocean and lava, on the ground (the Coast's ocean and the Lava Fields' lava layers, which the
    /// template leaves out, created by the writer). A generator whose ground is not grass (the Snowfield's snow, the Lava Fields' dirt) has the
    /// area's terrain pointed at its ground layer first, as a new area of that ground is, or the template's empty grass
    /// layer, which the area's terrain lists, would be kept.
    /// </summary>
    [TestCase("Assets/Settings/Generation/Generator_Clearing.asset")]
    [TestCase("Assets/Settings/Generation/Generator_VillageForest.asset")]
    [TestCase("Assets/Settings/Generation/Generator_Lakeside.asset")]
    [TestCase("Assets/Settings/Generation/Generator_Snowfield.asset")]
    [TestCase("Assets/Settings/Generation/Generator_Coast.asset")]
    [TestCase("Assets/Settings/Generation/Generator_LavaFields.asset")]
    public void TheTemplate_TakesAGeneratedLayout_TrimmedToTheGeneratorsLayers(string generatorPath)
    {
        Grid grid = TemplateGrid();
        AreaGenerator generator = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        AreaLayout layout = generator.Generate(3);
        SerializedObject terrain = new(grid.GetComponent<AreaTerrain>());
        SerializedProperty groundEntry = terrain.FindProperty("layers").GetArrayElementAtIndex(0).FindPropertyRelative("tilemap");
        groundEntry.objectReferenceValue = grid.transform.Find(generator.TerrainLayers[0].TilemapName).GetComponent<Tilemap>();
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
