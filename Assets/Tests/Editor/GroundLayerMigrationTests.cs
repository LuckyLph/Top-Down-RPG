using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="GroundLayerMigration"/> on a grid built in the test like an area before Phase 4 of
/// Docs/TerrainBlending.md: dual-grid grass and gravel layers (data and render tilemaps of rule tiles made for the
/// test) and a plain stone layer, each with a navigation source whose priority is off, dual-grid walls on the collision
/// tilemap, and an <see cref="AreaTerrain"/> listing the grass and the walls. The conversion keeps every filled cell as
/// the layer's plain tile, leaves no module or render tilemap, adds the terrain renderer with the layers and materials
/// in order and the navigation priorities from that order, sets the area terrain's fill tiles, leaves the walls alone,
/// is undone in one step, and changes nothing when run again.
/// </summary>
public class GroundLayerMigrationTests
{
    private static readonly Vector3Int[] GrassCells = { new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(0, 1, 0), new(1, 1, 0), new(2, 1, 0), new(-3, 4, 0) };
    private static readonly Vector3Int[] GravelCells = { new(1, 0, 0), new(1, 1, 0) };
    private static readonly Vector3Int[] StoneCells = { new(2, 1, 0) };
    private static readonly Vector3Int[] WallCells = { new(5, 5, 0), new(5, 6, 0) };

    private readonly List<Object> created = new();
    private Grid grid;
    private DualGridTilemapModule grass;
    private DualGridTilemapModule gravel;
    private Tilemap stone;
    private DualGridTilemapModule walls;
    private AreaTerrain terrain;
    private TerrainStyle style;
    private AreaGenerator.TerrainLayer[] layers;
    private int undoGroup;

    /// <summary>
    /// Builds the area's grid, its layers and the layer definitions (tiles, terrain types, materials and a style made
    /// for the test), and starts the undo group the test's changes are reverted to.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        Undo.IncrementCurrentGroup();
        undoGroup = Undo.GetCurrentGroup();
        grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        TerrainMaterial[] materials = TestTerrainStyle.PlainMaterials(created);
        style = TestTerrainStyle.Create(created);
        layers = new[]
        {
            new AreaGenerator.TerrainLayer("GrassTilemap", CreateTile("Tile_Grass"), Track(TerrainType2D.Create("grass")), materials[0]),
            new AreaGenerator.TerrainLayer("GravelTilemap", CreateTile("Tile_Gravel"), Track(TerrainType2D.Create("gravel")), materials[1]),
            new AreaGenerator.TerrainLayer("StoneTilemap", CreateTile("Tile_Stone"), Track(TerrainType2D.Create("stone")), materials[2])
        };

        grass = AddDualGridLayer("GrassTilemap", GrassCells);
        grass.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(layers[0].TerrainType, 4);
        gravel = AddDualGridLayer("GravelTilemap", GravelCells);
        gravel.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(layers[1].TerrainType);
        stone = AddTilemap("StoneTilemap");
        stone.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(layers[2].TerrainType, 9);
        Tile oldStone = CreateTile("OldStone");
        foreach (Vector3Int cell in StoneCells)
        {
            stone.SetTile(cell, oldStone);
        }

        walls = AddDualGridLayer("CollisionTilemap", WallCells);
        terrain = grid.gameObject.AddComponent<AreaTerrain>();
        terrain.Configure(new AreaTerrain.Layer(grass.DataTilemap, null), new AreaTerrain.Layer(walls.DataTilemap, null));
    }

    /// <summary>
    /// Reverts the test's undoable changes, then destroys every object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Undo.RevertAllDownToGroup(undoGroup);
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Every cell filled before is filled afterwards, with the layer's plain tile, and no other cell is; the tilemap
    /// keeps no record of the tiles it held before (which a scene would save as references to them).
    /// </summary>
    [Test]
    public void Migrate_KeepsEveryFilledCell_AsTheLayersPlainTile()
    {
        Assert.That(GroundLayerMigration.Migrate(grid, style, layers), Is.Not.Empty);

        AssertHolds(Layer(0), GrassCells, layers[0].Tile);
        AssertHolds(Layer(1), GravelCells, layers[1].Tile);
        AssertHolds(Layer(2), StoneCells, layers[2].Tile);
        for (int i = 0; i < layers.Length; i++)
        {
            SerializedProperty tileAssets = new SerializedObject(Layer(i)).FindProperty("m_TileAssetArray");
            Assert.That(tileAssets.arraySize, Is.EqualTo(1), $"{layers[i].TilemapName} records one tile");
            Assert.That(tileAssets.GetArrayElementAtIndex(0).FindPropertyRelative("m_Data").objectReferenceValue, Is.SameAs(layers[i].Tile));
        }
    }

    /// <summary>
    /// The ground layers lose their modules and render tilemaps and keep only a tilemap and a navigation source, while
    /// the walls, which are not a ground layer, keep theirs with their cells.
    /// </summary>
    [Test]
    public void Migrate_LeavesNoModuleOrRenderTilemap_AndLeavesTheWallsAlone()
    {
        GroundLayerMigration.Migrate(grid, style, layers);

        for (int i = 0; i < layers.Length; i++)
        {
            Tilemap tilemap = Layer(i);
            Assert.That(tilemap.GetComponent<DualGridTilemapModule>(), Is.Null, tilemap.name);
            Assert.That(tilemap.GetComponent<TilemapRenderer>(), Is.Null, tilemap.name);
            Assert.That(tilemap.transform.childCount, Is.Zero, $"{tilemap.name} has no render tilemap");
        }

        Assert.That(walls != null && walls.RenderTilemap != null, Is.True, "The walls keep their module and render tilemap.");
        AssertHolds(walls.DataTilemap, WallCells, walls.DataTile);
        Assert.That(walls.RenderTilemap.HasTile(DualGridUtils.GetRenderTilePosition(WallCells[0], 0)), Is.True);
    }

    /// <summary>
    /// The grid gets a terrain renderer with the style, drawing the layers in the given order with their materials;
    /// each layer's navigation source gets its terrain type and its index there as priority, and the renderer builds
    /// with the converted cells.
    /// </summary>
    [Test]
    public void Migrate_AddsTheRenderer_WithTheLayersAndMaterialsInOrder_AndPrioritiesFromThatOrder()
    {
        GroundLayerMigration.Migrate(grid, style, layers);

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.Style, Is.SameAs(style));
        Assert.That(renderer.Layers.Count, Is.EqualTo(layers.Length));
        for (int i = 0; i < layers.Length; i++)
        {
            Assert.That(renderer.Layers[i].Tilemap, Is.SameAs(Layer(i)), $"layer {i}");
            Assert.That(renderer.Layers[i].Material, Is.SameAs(layers[i].Material), $"layer {i}");
            NavigationTerrainSource2D source = Layer(i).GetComponent<NavigationTerrainSource2D>();
            Assert.That(source.TerrainType, Is.SameAs(layers[i].TerrainType), $"layer {i}");
            Assert.That(source.Priority, Is.EqualTo(TerrainBlendRenderer.NavigationPriorityOf(i)), $"layer {i}");
        }

        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(1, 1, 0)), Is.EqualTo(0b011), "Grass and gravel fill that cell.");
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(2, 1, 0)), Is.EqualTo(0b101), "Grass and stone fill that cell.");
        Assert.That(renderer.ControlMap.GetCell(new Vector3Int(-3, 4, 0)), Is.EqualTo(0b001));
    }

    /// <summary>
    /// An existing renderer is updated in place (the same component), and a generator layer the area does not have is
    /// left out of it rather than created.
    /// </summary>
    [Test]
    public void Migrate_UpdatesAnExistingRenderer_AndLeavesOutLayersTheAreaDoesNotHave()
    {
        Object.DestroyImmediate(stone.gameObject);
        TerrainBlendRenderer existing = grid.gameObject.AddComponent<TerrainBlendRenderer>();
        existing.Configure(TestTerrainStyle.Create(created), new TerrainBlendRenderer.Layer(gravel.DataTilemap, layers[0].Material));

        GroundLayerMigration.Migrate(grid, style, layers);

        Assert.That(grid.GetComponent<TerrainBlendRenderer>(), Is.SameAs(existing));
        Assert.That(existing.Style, Is.SameAs(style));
        Assert.That(existing.Layers.Count, Is.EqualTo(2));
        Assert.That(existing.Layers[0].Tilemap, Is.SameAs(grass.DataTilemap));
        Assert.That(existing.Layers[1].Tilemap, Is.SameAs(Layer(1)));
        Assert.That(existing.Layers[1].Material, Is.SameAs(layers[1].Material));
        Assert.That(grid.transform.Find("StoneTilemap"), Is.Null, "A missing layer is not created.");
    }

    /// <summary>
    /// A tilemap the renderer drew besides the layers stays drawn above them, with its material, and its navigation
    /// source's priority follows its new place, so it does not go on counting for navigation without being drawn.
    /// </summary>
    [Test]
    public void Migrate_KeepsDrawingATilemapTheRendererDrewBesidesTheLayers_AboveThem()
    {
        Tilemap water = AddTilemap("WaterTilemap");
        water.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(Track(TerrainType2D.Create("water")));
        grass.GetComponent<NavigationTerrainSource2D>().Configure(layers[0].TerrainType, 1);
        TerrainBlendRenderer existing = grid.gameObject.AddComponent<TerrainBlendRenderer>();
        existing.Configure(style, new TerrainBlendRenderer.Layer(water, layers[2].Material), new TerrainBlendRenderer.Layer(grass.DataTilemap, layers[0].Material));

        List<string> changes = GroundLayerMigration.Migrate(grid, style, layers);

        Assert.That(existing.Layers.Count, Is.EqualTo(4));
        Assert.That(existing.Layers[3].Tilemap, Is.SameAs(water));
        Assert.That(existing.Layers[3].Material, Is.SameAs(layers[2].Material));
        Assert.That(water.GetComponent<NavigationTerrainSource2D>().Priority, Is.EqualTo(TerrainBlendRenderer.NavigationPriorityOf(3)));
        Assert.That(existing.HasNavigationPrioritiesInStep(), Is.True);
        Assert.That(changes, Does.Contain("Grid: keeps drawing WaterTilemap, now as layer 3"));
        Assert.That(GroundLayerMigration.Migrate(grid, style, layers), Is.Empty);
    }

    /// <summary>
    /// A renderer entry without a tilemap is dropped, and that counts as a change, so the conversion reports it and
    /// records it as its undo step instead of saying the ground already matches. The entry is added and dropped in one
    /// undo group, so the revert in <see cref="TearDown"/> never rebuilds the renderer with it.
    /// </summary>
    [Test]
    public void Migrate_ReportsDroppingARendererLayerWithoutATilemap()
    {
        GroundLayerMigration.Migrate(grid, style, layers);
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        SerializedObject rendererData = new(renderer);
        SerializedProperty listed = rendererData.FindProperty("layers");
        listed.arraySize = 4;
        listed.GetArrayElementAtIndex(3).FindPropertyRelative("tilemap").objectReferenceValue = null;
        rendererData.ApplyModifiedProperties();

        List<string> changes = GroundLayerMigration.Migrate(grid, style, layers);
        Undo.CollapseUndoOperations(group);

        Assert.That(changes, Is.EqualTo(new[] { "Grid: draws 3 layers instead of 4" }));
        Assert.That(renderer.Layers.Count, Is.EqualTo(3));
    }

    /// <summary>
    /// A render tilemap left under a ground layer after its module was removed would go on drawing the old tileset, so
    /// the conversion removes it too.
    /// </summary>
    [Test]
    public void Migrate_RemovesARenderTilemapLeftWithoutItsModule()
    {
        Object.DestroyImmediate(grass);

        Assert.That(GroundLayerMigration.Migrate(grid, style, layers), Does.Contain("GrassTilemap: removed its render tilemap RenderTilemap"));

        Assert.That(Layer(0).transform.childCount, Is.Zero);
    }

    /// <summary>
    /// A layer whose cells all hold its tile already but which keeps the record of a tile it held before (written over
    /// cell by cell, not cleared: the old record stays with a count of 0 once the new tile has a record of its own) is
    /// rewritten, so the scene stops referencing the old tile; then a second run changes nothing.
    /// </summary>
    [Test]
    public void Migrate_DropsRecordsOfTilesALayerNoLongerHolds()
    {
        Vector3Int[] cells = { StoneCells[0], new(3, 1, 0) };
        stone.SetTile(cells[1], stone.GetTile(cells[0]));
        foreach (Vector3Int cell in cells)
        {
            stone.SetTile(cell, layers[2].Tile);
        }

        Assert.That(new SerializedObject(stone).FindProperty("m_TileAssetArray").arraySize, Is.EqualTo(2), "The old tile's record stays.");

        Assert.That(GroundLayerMigration.Migrate(grid, style, layers), Does.Contain("StoneTilemap: dropped its records of tiles it no longer holds"));

        SerializedProperty records = new SerializedObject(stone).FindProperty("m_TileAssetArray");
        Assert.That(records.arraySize, Is.EqualTo(1));
        Assert.That(records.GetArrayElementAtIndex(0).FindPropertyRelative("m_Data").objectReferenceValue, Is.SameAs(layers[2].Tile));
        AssertHolds(stone, cells, layers[2].Tile);
        Assert.That(GroundLayerMigration.Migrate(grid, style, layers), Is.Empty);
    }

    /// <summary>
    /// Layers none of which is a tilemap under the grid (renamed, say) are refused with an error, leaving an existing
    /// renderer's layers as they were instead of emptying them.
    /// </summary>
    [Test]
    public void Migrate_WithNoneOfItsLayersUnderTheGrid_LogsAnErrorAndChangesNothing()
    {
        TerrainBlendRenderer existing = grid.gameObject.AddComponent<TerrainBlendRenderer>();
        grass.GetComponent<NavigationTerrainSource2D>().Configure(layers[0].TerrainType);
        existing.Configure(style, new TerrainBlendRenderer.Layer(grass.DataTilemap, layers[0].Material));
        AreaGenerator.TerrainLayer[] renamed = { new("GroundTilemap", layers[0].Tile, layers[0].TerrainType, layers[0].Material) };
        LogAssert.Expect(LogType.Error, "GroundLayerMigration: none of its terrain layers is a tilemap under Grid, so there is nothing to convert.");

        Assert.That(GroundLayerMigration.Migrate(grid, style, renamed), Is.Null);

        Assert.That(existing.Layers.Count, Is.EqualTo(1));
        Assert.That(existing.Layers[0].Tilemap, Is.SameAs(grass.DataTilemap));
        Assert.That(grass.GetComponent<DualGridTilemapModule>(), Is.Not.Null);
    }

    /// <summary>
    /// The area terrain fills each converted layer it lists with the layer's tile; the walls keep no fill tile, since
    /// their module paints them.
    /// </summary>
    [Test]
    public void Migrate_SetsTheAreaTerrainsFillTiles()
    {
        GroundLayerMigration.Migrate(grid, style, layers);

        SerializedProperty listed = new SerializedObject(terrain).FindProperty("layers");
        Assert.That(listed.GetArrayElementAtIndex(0).FindPropertyRelative("fillTile").objectReferenceValue, Is.SameAs(layers[0].Tile));
        Assert.That(listed.GetArrayElementAtIndex(1).FindPropertyRelative("fillTile").objectReferenceValue, Is.Null);
    }

    /// <summary>
    /// One undo brings back the modules and render tilemaps with their tiles, the data tiles, the navigation priorities
    /// and the area terrain's fill tiles, and removes the renderer the conversion added.
    /// </summary>
    [Test]
    public void Migrate_IsUndoneInOneStep()
    {
        Tile oldStone = (Tile)stone.GetTile(StoneCells[0]);
        DualGridDataTile grassDataTile = grass.DataTile;
        GroundLayerMigration.Migrate(grid, style, layers);

        Undo.PerformUndo();

        DualGridTilemapModule grassModule = Layer(0).GetComponent<DualGridTilemapModule>();
        Assert.That(grassModule, Is.Not.Null, "The grass module is back.");
        Assert.That(Layer(1).GetComponent<DualGridTilemapModule>(), Is.Not.Null, "The gravel module is back.");
        Assert.That(grassModule.RenderTilemap, Is.Not.Null.And.Not.SameAs(Layer(0)), "The grass render tilemap is back.");
        Assert.That(grassModule.RenderTilemap.HasTile(DualGridUtils.GetRenderTilePosition(GrassCells[0], 0)), Is.True, "with its tiles");
        AssertHolds(Layer(0), GrassCells, grassDataTile);
        AssertHolds(Layer(2), StoneCells, oldStone);
        Assert.That(Layer(0).GetComponent<NavigationTerrainSource2D>().Priority, Is.EqualTo(4));
        Assert.That(Layer(2).GetComponent<NavigationTerrainSource2D>().Priority, Is.EqualTo(9));
        Assert.That(grid.GetComponent<TerrainBlendRenderer>(), Is.Null, "The renderer the conversion added is gone.");
        SerializedProperty listed = new SerializedObject(terrain).FindProperty("layers");
        Assert.That(listed.GetArrayElementAtIndex(0).FindPropertyRelative("fillTile").objectReferenceValue, Is.Null);
    }

    /// <summary>
    /// Running the conversion again reports no change and records no undo step: the next undo still undoes the first
    /// conversion.
    /// </summary>
    [Test]
    public void Migrate_Twice_ChangesNothing()
    {
        GroundLayerMigration.Migrate(grid, style, layers);
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();

        Assert.That(GroundLayerMigration.Migrate(grid, style, layers), Is.Empty);

        Assert.That(grid.GetComponent<TerrainBlendRenderer>(), Is.SameAs(renderer));
        AssertHolds(Layer(0), GrassCells, layers[0].Tile);
        Undo.PerformUndo();
        Assert.That(Layer(0).GetComponent<DualGridTilemapModule>(), Is.Not.Null, "The undo after the second run undoes the first one.");
    }

    /// <summary>
    /// Without a style the conversion logs an error, returns null and changes nothing.
    /// </summary>
    [Test]
    public void Migrate_WithoutAStyle_LogsAnErrorAndChangesNothing()
    {
        LogAssert.Expect(LogType.Error, "GroundLayerMigration: no terrain style for the area's TerrainBlendRenderer.");

        Assert.That(GroundLayerMigration.Migrate(grid, null, layers), Is.Null);

        Assert.That(grass.GetComponent<DualGridTilemapModule>(), Is.Not.Null);
        Assert.That(grid.GetComponent<TerrainBlendRenderer>(), Is.Null);
    }

    /// <summary>
    /// The tilemap of layer <paramref name="index"/>, found by its name under the grid.
    /// </summary>
    private Tilemap Layer(int index)
    {
        return grid.transform.Find(layers[index].TilemapName).GetComponent<Tilemap>();
    }

    /// <summary>
    /// Asserts that <paramref name="tilemap"/> holds <paramref name="tile"/> in exactly <paramref name="cells"/>.
    /// </summary>
    private static void AssertHolds(Tilemap tilemap, Vector3Int[] cells, TileBase tile)
    {
        HashSet<Vector3Int> filled = new();
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.HasTile(cell))
            {
                filled.Add(cell);
            }
        }

        Assert.That(filled, Is.EquivalentTo(cells), $"{tilemap.name} cells");
        foreach (Vector3Int cell in cells)
        {
            Assert.That(tilemap.GetTile(cell), Is.SameAs(tile), $"{tilemap.name} {cell}");
        }
    }

    /// <summary>
    /// Adds a dual-grid layer named <paramref name="name"/> under the grid: a data tilemap with a module of a rule tile
    /// made for the test, its render tilemap (its Tilemap Renderer switched off, as in an area the renderer draws) and
    /// <paramref name="cells"/> painted through the module.
    /// </summary>
    private DualGridTilemapModule AddDualGridLayer(string name, Vector3Int[] cells)
    {
        Tilemap data = AddTilemap(name);
        GameObject render = new("RenderTilemap", typeof(Tilemap), typeof(TilemapRenderer));
        render.transform.SetParent(data.transform, false);
        render.transform.localPosition = new Vector3(-0.5f, -0.5f, 0f);
        render.GetComponent<TilemapRenderer>().enabled = false;
        DualGridRuleTile ruleTile = Track(ScriptableObject.CreateInstance<DualGridRuleTile>());
        Track(ruleTile.RefreshDataTile());
        DualGridTilemapModule module = data.gameObject.AddComponent<DualGridTilemapModule>();
        module.RenderTile = ruleTile;
        module.SetDataTiles(cells);
        return module;
    }

    /// <summary>
    /// Adds a plain tilemap named <paramref name="name"/> under the grid.
    /// </summary>
    private Tilemap AddTilemap(string name)
    {
        GameObject data = new(name, typeof(Tilemap));
        data.transform.SetParent(grid.transform, false);
        return data.GetComponent<Tilemap>();
    }

    /// <summary>
    /// A plain tile named <paramref name="name"/>, destroyed in <see cref="TearDown"/>.
    /// </summary>
    private Tile CreateTile(string name)
    {
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        tile.name = name;
        return tile;
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
