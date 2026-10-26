using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for writing a composition into chunk scenes (Docs/WorldGeneration.md: Testing): in memory, chunks of the
/// Overworld's Phase 0 composition written by <see cref="WorldGenerationWriter.WriteChunk"/> into chunk grids built as
/// <see cref="WorldAuthoring.CreateChunk"/> builds them (<see cref="TestWorldGenerators"/>); and once on disk, a Clearing
/// on a 2 x 2 world written by <see cref="WorldGenerationWriter.Write"/> into chunk scenes it creates in a temporary
/// folder under <c>Assets/Dev</c>, deleted after. Each chunk lists its own and its neighbours' strips' materials in the
/// style's order, each layer holding exactly its composed cells and tile, with its raised ground, its mobs under its
/// generated root with derived ids the id watcher keeps and its props under its generated props' root where the
/// composition puts them; its <see cref="AreaTerrain"/> is only appended to; the written hash follows paint on any layer,
/// the generated spawn points and the generated props, and nothing else; objects outside the generated roots (props
/// placed by hand among them) and entries made by hand stay, and a rewrite reuses the prop instances; a locked chunk is
/// never opened, an unchanged one is skipped without being opened, one
/// changed by hand is refused unless forced; a second write changes no file; and the written world validates, bakes
/// its coasts and keeps its derived ids through Repair ids; an entry moves only once the chunk under its new place is
/// written; a mob whose id a chunk keeping its content still holds is left out of the chunk it moved to, also when that
/// chunk is refused after it in the same write. Creating a chunk keeps the open scenes' unsaved changes and refuses
/// beside an untitled scene with some.
/// </summary>
public class WorldGenerationWriterTests
{
    private const string DiskFolder = "Assets/Dev/WorldGenerationWriterTests";
    private const string DiskWorldName = "WriterTest";

    private TestWorldGenerators generators;
    private WorldGenerator overworldGenerator;
    private WorldComposition overworld;
    private TestWorldGenerators perTest;

    /// <summary>
    /// Composes the Overworld's Phase 0 settings once for the fixture; the tests only read it.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        generators = new TestWorldGenerators();
        overworldGenerator = generators.Overworld();
        overworld = overworldGenerator.Compose();
        Assert.That(overworld.Warnings, Is.Empty, "the Overworld composes with no warnings");
    }

    /// <summary>
    /// Destroys the fixture's generator and world.
    /// </summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        generators.Destroy();
    }

    /// <summary>
    /// Starts a test's own set of objects to destroy.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        perTest = new TestWorldGenerators();
    }

    /// <summary>
    /// Destroys what the test made, and the temporary world on disk with its folder when the test made one.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        perTest.Destroy();
        if (AssetDatabase.IsValidFolder(DiskFolder))
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(DiskFolder);
        }
    }

    /// <summary>
    /// The chunk holding the Coast's plateau (raised ground and ramps), a chunk of the Snowfield's snow, and a corner
    /// chunk of the shore, each written into a new chunk grid: its layers are its list's materials and the grass layer
    /// its <see cref="AreaTerrain"/> lists, in the style's order, each a child of the grid at its place with its
    /// navigation priority and terrain type, holding exactly its material's composed cells of the chunk with its tile;
    /// its raised tilemap holds the composed raised cells and ramps; its mobs stand under its generated root at their
    /// cells' centres with their prefabs and derived ids, which the id watcher keeps; its props stand under its generated
    /// props' root, of their prefabs and where the composition puts them; the grid is active again and the chunk read
    /// back hashes as it was composed.
    /// </summary>
    [Test]
    public void WriteChunk_LaysTheChunksList_EachLayerHoldingExactlyItsComposedCells_WithItsRaisedGroundAndMobs()
    {
        Vector2Int plateau = ChunkCoords.ChunkOf(FirstRaisedCell(RaisedGround.Ramp));
        Vector2Int snow = ChunkWithOwn(TestWorldGenerators.GroundMaterial("Snow"));
        int mobs = 0;
        int props = 0;
        Dictionary<Vector2Int, Grid> grids = new();
        foreach (Vector2Int coords in new[] { plateau, snow, new Vector2Int(0, 0) })
        {
            grids[coords] = perTest.NewChunk(overworldGenerator.World, coords);
            string written = WorldGenerationWriter.WriteChunk(grids[coords], overworld, coords);
            Assert.That(written, Is.EqualTo(overworld.ChunkAt(coords).ContentHash), $"chunk {coords}'s written hash");
            ExpectWritten(grids[coords], coords);
            mobs += MobsOf(coords).Count;
            props += PropsOf(coords).Count;
        }

        Assert.That(mobs, Is.GreaterThan(0), "the chunks written hold mobs");
        Assert.That(props, Is.GreaterThan(0), "the chunks written hold props");
        Tilemap raised = grids[plateau].GetComponent<TerrainBlendRenderer>().RaisedTilemap;
        Assert.That(raised != null, Is.True, "the plateau's chunk has a raised tilemap");
        Assert.That(raised.GetTile(Cell3(FirstRaisedCell(RaisedGround.Ramp))) == TestWorldGenerators.Area("Coast").RampTile, Is.True, "its ramp holds the Coast's ramp tile");
    }

    /// <summary>
    /// A chunk of the Snowfield whose <see cref="AreaTerrain"/> lists the grass layer, the walls and an empty mud layer
    /// keeps those three entries at their indices, gains its snow layer after them, keeps drawing the mud layer though
    /// it is empty, and fills each listed layer with its tile; writing it again appends nothing more.
    /// </summary>
    [Test]
    public void AreaTerrain_IsOnlyAppendedTo_ABaseGroundLayerIsAppended_AndAListedLayerStaysWhenEmpty()
    {
        TerrainMaterial mud = TestWorldGenerators.GroundMaterial("Mud");
        TerrainMaterial snow = TestWorldGenerators.GroundMaterial("Snow");
        Vector2Int coords = ChunkWithOwn(snow);
        Assert.That(overworld.ChunkAt(coords).Materials, Has.No.Member(mud), "the chunk lays no mud");
        Grid grid = perTest.NewChunk(overworldGenerator.World, coords);
        WorldAuthoring.AddWalls(grid);
        Tilemap mudLayer = ListLayer(grid, mud);
        List<Tilemap> before = Listed(grid);
        Assert.That(before.ConvertAll(tilemap => tilemap.name), Is.EqualTo(new[] { "GrassTilemap", WorldAuthoring.WallsName, mudLayer.name }), "the entries before");

        WorldGenerationWriter.WriteChunk(grid, overworld, coords);
        List<Tilemap> after = Listed(grid);
        Assert.That(after.GetRange(0, before.Count), Is.EqualTo(before), "every entry keeps its index");
        List<TerrainMaterial> appended = after.GetRange(before.Count, after.Count - before.Count).ConvertAll(tilemap => MaterialOf(grid, tilemap));
        Assert.That(appended, Has.Member(snow), "the snow layer is appended");
        foreach (TerrainMaterial material in appended)
        {
            Assert.That(overworld.Grounds, Has.Member(material), $"{material.name} is a base ground");
        }

        Assert.That(LayerOf(grid, mud) == mudLayer, Is.True, "the listed mud layer is still drawn");
        Assert.That(Filled(mudLayer), Is.Empty, "the listed mud layer is empty");
        SerializedProperty entries = new SerializedObject(grid.GetComponent<AreaTerrain>()).FindProperty("layers");
        for (int i = 0; i < entries.arraySize; i++)
        {
            Tilemap tilemap = (Tilemap)entries.GetArrayElementAtIndex(i).FindPropertyRelative("tilemap").objectReferenceValue;
            TerrainMaterial material = MaterialOf(grid, tilemap);
            Object fill = entries.GetArrayElementAtIndex(i).FindPropertyRelative("fillTile").objectReferenceValue;
            Assert.That(fill, Is.EqualTo(material != null ? TileOf(material) : null), $"{tilemap.name}'s fill tile");
        }

        WorldGenerationWriter.WriteChunk(grid, overworld, coords);
        Assert.That(Listed(grid), Is.EqualTo(after), "a second write appends nothing");
    }

    /// <summary>
    /// The hash of a written chunk read back from its grid changes with a cell painted on an empty layer its
    /// <see cref="AreaTerrain"/> keeps, a cell painted on any ground layer, a raised cell, a generated spawn point moved
    /// and a generated prop moved, and comes back when each is undone; walls, a decor tilemap, a spawn point outside the
    /// generated root and a prop placed by hand under the scene's Props root change nothing.
    /// </summary>
    [Test]
    public void TheWrittenHash_FollowsPaintOnAnyLayerRaisedGroundAndGeneratedSpawnPoints_AndNothingElse()
    {
        Vector2Int coords = ChunkCoords.ChunkOf(FirstRaisedCell(RaisedGround.Ramp));
        Assert.That(MobsOf(coords), Is.Not.Empty, "the plateau's chunk holds mobs");
        string composed = overworld.ChunkAt(coords).ContentHash;
        Grid grid = perTest.NewChunk(overworldGenerator.World, coords);
        Tilemap mud = ListLayer(grid, TestWorldGenerators.GroundMaterial("Mud"));
        WorldGenerationWriter.WriteChunk(grid, overworld, coords);
        Assert.That(Hash(grid, coords), Is.EqualTo(composed), "read back as composed, its empty listed layer counting for nothing");

        Vector3Int cell = Cell3(ChunkCoords.Bounds(coords).center);
        ExpectChange(grid, coords, composed, "paint on the empty listed layer", () => mud.SetTile(cell, TileOf(TestWorldGenerators.GroundMaterial("Mud"))), () => mud.SetTile(cell, null));
        Tilemap grass = LayerOf(grid, TestWorldGenerators.GroundMaterial("Grass"));
        Vector3Int bare = FirstCellWithout(coords, grass);
        ExpectChange(grid, coords, composed, "grass painted where none was", () => grass.SetTile(bare, TileOf(TestWorldGenerators.GroundMaterial("Grass"))), () => grass.SetTile(bare, null));
        Tilemap raised = grid.GetComponent<TerrainBlendRenderer>().RaisedTilemap;
        Vector3Int level = FirstCellWithout(coords, raised);
        ExpectChange(grid, coords, composed, "a cell raised", () => raised.SetTile(level, TestWorldGenerators.Area("Coast").RaisedTile), () => raised.SetTile(level, null));
        Transform spawn = grid.transform.Find(WorldGenerationWriter.GeneratedMobSpawnsName).GetChild(0);
        Vector3 at = spawn.position;
        ExpectChange(grid, coords, composed, "a generated spawn point moved", () => spawn.position = at + Vector3.right, () => spawn.position = at);
        Transform prop = grid.transform.Find(WorldGenerationWriter.GeneratedPropsName).GetChild(0);
        Vector3 stood = prop.position;
        ExpectChange(grid, coords, composed, "a generated prop moved", () => prop.position = stood + Vector3.right, () => prop.position = stood);

        DualGridTilemapModule walls = WorldAuthoring.AddWalls(grid);
        walls.SetDataTiles(new List<Vector3Int> { cell });
        Tilemap decor = new GameObject("Decor", typeof(Tilemap)).GetComponent<Tilemap>();
        decor.transform.SetParent(grid.transform, false);
        decor.SetTile(cell, TileOf(TestWorldGenerators.GroundMaterial("Stone")));
        HandSpawnPoint(grid.transform.position + new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f), "hand-made");
        HandProp(grid, cell);
        Assert.That(Hash(grid, coords), Is.EqualTo(composed), "walls, a decor tilemap, a hand-made spawn point and a prop placed by hand change nothing");
    }

    /// <summary>
    /// Writing a chunk twice keeps every object outside its generated roots (a hand-made spawn point with its id, a decor
    /// object under the grid, a prop placed by hand under the scene's Props root, the walls), removes a painted layer of a
    /// material it neither lists nor keeps in its <see cref="AreaTerrain"/> and the raised tilemap of a chunk without
    /// raised ground, and leaves its generated spawn points and props the composition's, the second write reusing every
    /// prop instance the first made.
    /// </summary>
    [Test]
    public void Writing_KeepsEveryObjectOutsideTheGeneratedRoot_AndRemovesALayerNeitherListedNorKept_AndUnneededRaisedGround()
    {
        TerrainMaterial ice = TestWorldGenerators.GroundMaterial("Ice");
        Vector2Int coords = ChunkWithMobsAndNoRaised(ice);
        Grid grid = perTest.NewChunk(overworldGenerator.World, coords);
        Tilemap iceLayer = GroundLayerMigration.AddLayer(grid, ice);
        Vector3Int cell = Cell3(ChunkCoords.Bounds(coords).center);
        iceLayer.SetTile(cell, TileOf(ice));
        Tilemap raised = AreaGroundSetup.SetUpRaised(grid, new List<string>());
        raised.SetTile(cell, TestWorldGenerators.Area("Coast").RaisedTile);
        WorldAuthoring.AddWalls(grid);
        GameObject decor = new("Decor");
        decor.transform.SetParent(grid.transform, false);
        MobSpawnPoint hand = HandSpawnPoint(grid.transform.position + new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f), "hand-made");
        GameObject handProp = HandProp(grid, cell);
        List<GameObject> firstProps = null;

        for (int write = 0; write < 2; write++)
        {
            Assert.That(WorldGenerationWriter.WriteChunk(grid, overworld, coords), Is.Not.Null, $"write {write + 1}");
            Assert.That(LayerOf(grid, ice) == null, Is.True, $"write {write + 1}: the ice layer is no longer drawn");
            Assert.That(grid.transform.Find(GroundLayerMigration.TilemapName(ice)) == null, Is.True, $"write {write + 1}: the ice layer is removed");
            Assert.That(grid.GetComponent<TerrainBlendRenderer>().RaisedTilemap == null, Is.True, $"write {write + 1}: no raised ground is drawn");
            Assert.That(grid.GetComponent<NavigationGrid2D>().RaisedTilemap == null, Is.True, $"write {write + 1}: no cliff is read");
            Assert.That(grid.transform.Find(AreaGenerator.RaisedTilemapName) == null, Is.True, $"write {write + 1}: the raised tilemap is removed");
            Assert.That(hand != null && hand.GetComponent<PersistentId>().Id == "hand-made", Is.True, $"write {write + 1}: the hand-made spawn point and its id stay");
            Assert.That(decor != null && decor.transform.parent == grid.transform, Is.True, $"write {write + 1}: the decor object stays");
            Assert.That(grid.transform.Find(WorldAuthoring.WallsName) != null, Is.True, $"write {write + 1}: the walls stay");
            Assert.That(handProp != null && handProp.transform.parent.name == PropPlacementWindow.PropsRootName, Is.True, $"write {write + 1}: the prop placed by hand stays");
            ExpectWritten(grid, coords);
            List<GameObject> written = new();
            foreach (Transform prop in grid.transform.Find(WorldGenerationWriter.GeneratedPropsName))
            {
                written.Add(prop.gameObject);
            }

            Assert.That(written, Is.Not.Empty, $"write {write + 1}: the chunk holds props");
            if (firstProps != null)
            {
                Assert.That(written, Is.EqualTo(firstProps), "the second write reuses every prop instance");
            }

            firstProps = written;
        }
    }

    /// <summary>
    /// <see cref="WorldGenerationWriter.Write"/> refuses a composition with more than 16 materials in a chunk (three
    /// regions laid over one another) with an error, writing nothing; told to write anyway it passes that check and
    /// stops at the next, the world being no asset.
    /// </summary>
    [Test]
    public void Write_RefusesACompositionWithLayerListOrReachabilityWarnings_UnlessToldToWriteAnyway()
    {
        WorldGenerator generator = perTest.Generator("Crowded", new Vector2Int(3, 3), null);
        generator.SetRegions(
            new RegionPlacement(TestWorldGenerators.Lakeside, TestWorldGenerators.Area("Lakeside"), new Vector2Int(96, 96)),
            new RegionPlacement(TestWorldGenerators.Snowfield, TestWorldGenerators.Area("Snowfield"), new Vector2Int(96, 96)),
            new RegionPlacement(TestWorldGenerators.LavaFields, TestWorldGenerators.Area("LavaFields"), new Vector2Int(96, 96)));
        WorldComposition crowded = generator.Compose();
        Assert.That(crowded.BlockingWarnings, Is.Not.Empty, "the crowded chunk's warning stops a write");

        LogAssert.Expect(LogType.Error, new Regex("warnings about layer lists or reachability"));
        Assert.That(WorldGenerationWriter.Write(generator, crowded, null, false), Is.Empty, "nothing written");
        LogAssert.Expect(LogType.Error, new Regex("is not saved as an asset"));
        Assert.That(WorldGenerationWriter.Write(generator, crowded, null, false, true), Is.Empty, "nothing written to a world that is no asset");
    }

    /// <summary>
    /// The entries after a write are the generated ones in the composition's order, then the entries made by hand in
    /// their order; an entry the last write made that the composition no longer has goes, and a hand-made entry of a
    /// generated entry's name gives way to it.
    /// </summary>
    [Test]
    public void TheEntries_AreTheGeneratedOnesFirst_ThenTheHandMadeOnes_AndAStaleGeneratedOneGoes()
    {
        WorldDefinition.Entry[] current =
        {
            new("clearing", new Vector2(1f, 1f), true),
            new("camp", new Vector2(5f, 5f), false),
            new("clearingEast", new Vector2(2f, 1f), false),
            new("lakeside", new Vector2(9f, 9f), false),
            new("dock", new Vector2(7f, 7f), true)
        };
        WorldDefinition.Entry[] generated =
        {
            new("clearing", new Vector2(3f, 3f), true),
            new("lakeside", new Vector2(4f, 4f), true)
        };

        List<WorldDefinition.Entry> merged = WorldGenerationWriter.MergeEntries(current, generated, new[] { "clearing", "clearingEast" });

        Assert.That(merged.ConvertAll(entry => entry.Name), Is.EqualTo(new[] { "clearing", "lakeside", "camp", "dock" }));
        Assert.That(merged[0].Position, Is.EqualTo(new Vector2(3f, 3f)), "the generated entry's place");
        Assert.That(merged[1].Waystone, Is.True, "the generated entry's waystone");
        Assert.That(merged[3].Waystone, Is.True, "the hand-made entry stays as it was");
    }

    /// <summary>
    /// On disk, creating a chunk (as a write does for a missing one) closes a clean untitled scene and keeps every other
    /// open scene with its unsaved changes, and beside an untitled scene with unsaved changes it refuses, making nothing.
    /// </summary>
    [Test]
    public void CreatingAChunk_KeepsTheOpenScenesChanges_AndRefusesBesideAnUnsavedUntitledScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Assert.That(AssetDatabase.IsValidFolder(DiskFolder), Is.False, $"{DiskFolder} is left over from an earlier run");
        Assert.That(WorldLayout.TryFindFreeOrigin(new Vector2Int(2, 1), WorldLayout.AllWorlds(), out Vector2Int origin), Is.True, "a free slot");
        WorldDefinition world = WorldAuthoring.CreateWorld(DiskWorldName, origin, new Vector2Int(2, 1), DiskFolder);
        Scene root = EditorSceneManager.OpenScene($"{DiskFolder}/{DiskWorldName}/World_{DiskWorldName}.unity", OpenSceneMode.Additive);
        Assert.That(SceneManager.sceneCount, Is.EqualTo(1), "The world's scene replaced the clean untitled one, the only scene open.");
        SceneManager.MoveGameObjectToScene(new GameObject("Unsaved"), root);
        EditorSceneManager.MarkSceneDirty(root);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        WorldAuthoring.CreateChunk(world, Vector2Int.zero);

        Assert.That(world.HasChunk(Vector2Int.zero), Is.True);
        Assert.That(root.isLoaded && root.isDirty, Is.True, "The open scene keeps its unsaved changes.");
        Assert.That(Array.Exists(root.GetRootGameObjects(), gameObject => gameObject.name == "Unsaved"), Is.True);
        Assert.That(SceneManager.sceneCount, Is.EqualTo(1), "The clean untitled scene was closed, and the new chunk's scene too.");

        Scene untitled = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.MoveGameObjectToScene(new GameObject("Unsaved too"), untitled);
        EditorSceneManager.MarkSceneDirty(untitled);
        Vector2Int east = new(1, 0);

        Assert.Throws<InvalidOperationException>(() => WorldAuthoring.CreateChunk(world, east));

        Assert.That(world.HasChunk(east), Is.False, "Nothing was made.");
        Assert.That(File.Exists($"{DiskFolder}/{DiskWorldName}/Chunks/{WorldAuthoring.ChunkSceneName(world, east)}.unity"), Is.False);
        Assert.That(untitled.isLoaded && untitled.isDirty, Is.True, "The untitled scene keeps its changes.");
        Assert.That(root.isLoaded && root.isDirty, Is.True);
    }

    /// <summary>
    /// On disk, a Clearing on a 2 x 2 world created with no chunks and an entry made by hand: the first write creates
    /// and writes every chunk, records its hash, puts the Clearing's waystone first and keeps the hand-made entry after
    /// it, and with the whole-world steps the world validates, every coast map is baked and fresh, and Repair ids
    /// keeps every derived id; a second write opens no scene and, with the steps again, changes no file; after the world
    /// seed changes, a locked chunk is neither opened nor changed, a chunk painted by hand is refused, named and left
    /// as it was until written with force, and the others are written, a hand-made spawn point in one of them kept.
    /// </summary>
    [Test]
    public void Write_OnDisk_CreatesChunks_SkipsUnchanged_LeavesLocked_RefusesHandWorkUnlessForced_AndTheWorldValidates()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Assert.That(AssetDatabase.IsValidFolder(DiskFolder), Is.False, $"{DiskFolder} is left over from an earlier run");
        int buildScenes = EditorBuildSettings.scenes.Length;
        Assert.That(WorldLayout.TryFindFreeOrigin(new Vector2Int(2, 2), WorldLayout.AllWorlds(), out Vector2Int origin), Is.True, "a free slot");
        WorldDefinition world = WorldAuthoring.CreateWorld(DiskWorldName, origin, new Vector2Int(2, 2), DiskFolder);
        WorldAuthoring.SetEntry(world, "camp", new Vector2(40.5f, 40.5f), false);
        WorldGenerator generator = perTest.For(world);
        generator.SetRegions(new RegionPlacement(TestWorldGenerators.Clearing, TestWorldGenerators.Area("Clearing"), new Vector2Int(64, 64)));
        WorldComposition composition = generator.Compose();
        Assert.That(composition.BlockingWarnings, Is.Empty, "nothing stops the write");

        List<WorldGenerationWriter.ChunkWrite> first = WorldGenerationWriter.Write(generator, composition, null, false);
        Assert.That(first.ConvertAll(result => result.Outcome), Is.All.EqualTo(WorldGenerationWriter.Outcome.Created), string.Join("\n", first.ConvertAll(result => result.Line)));
        foreach (WorldComposition.Chunk chunk in composition.Chunks)
        {
            Assert.That(world.HasChunk(chunk.Coords), Is.True, $"chunk {chunk.Coords} is created");
            Assert.That(generator.RecordOf(chunk.Coords).WrittenHash, Is.EqualTo(chunk.ContentHash), $"chunk {chunk.Coords}'s record");
        }

        Assert.That(new List<WorldDefinition.Entry>(world.Entries).ConvertAll(entry => entry.Name), Is.EqualTo(new[] { TestWorldGenerators.Clearing, "camp" }), "the generated entries first, the hand-made one after");
        Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(buildScenes), "a dev world's chunks stay out of the build");

        List<string> steps = WorldGenerationWriter.RunWorldSteps(world);
        Assert.That(steps, Has.Some.StartsWith("Repair ids: 0 changed"), string.Join("\n", steps));
        Assert.That(WorldValidation.ValidateWorld(world, WorldLayout.AllWorlds()), Is.Empty, "the written world validates");
        Assert.That(WorldAuthoring.StaleCoastMaps(world), Is.Empty, "no coast map is stale");
        foreach (WorldComposition.Chunk chunk in composition.Chunks)
        {
            Assert.That(File.Exists(WorldAuthoring.CoastMapPath(world, chunk.Coords)), Is.True, $"chunk {chunk.Coords}'s coast map is baked");
        }

        ExpectScenesHold(world, composition, generator);
        Dictionary<string, byte[]> files = Snapshot();
        List<string> opened = new();
        List<WorldGenerationWriter.ChunkWrite> second = WatchingOpens(opened, () => WorldGenerationWriter.Write(generator, composition, null, false));
        WorldGenerationWriter.RunWorldSteps(world);
        Assert.That(second.ConvertAll(result => result.Outcome), Is.All.EqualTo(WorldGenerationWriter.Outcome.Unchanged), "a second write skips every chunk");
        Assert.That(opened, Has.None.Contains("/Chunks/"), "a skipped chunk is not opened");
        ExpectSameFiles(files, Snapshot(), "a second write");

        Vector2Int locked = new(1, 1);
        Vector2Int painted = new(1, 0);
        Vector2Int withHandWork = new(0, 1);
        Vector3Int handCell = PaintInChunk(world, painted);
        string handId = AddHandSpawnPoint(world, withHandWork);
        generator.SetRecord(new ChunkRecord(locked, true, false, 0));
        generator.SetSeed(2);
        WorldComposition reseeded = generator.Compose();
        foreach (Vector2Int coords in new[] { painted, withHandWork, new Vector2Int(0, 0) })
        {
            Assert.That(reseeded.ChunkAt(coords).ContentHash, Is.Not.EqualTo(composition.ChunkAt(coords).ContentHash), $"the new seed changes chunk {coords}");
        }

        files = Snapshot();
        opened.Clear();
        Vector2 clearingBefore = world.FindEntry(TestWorldGenerators.Clearing).Position;
        List<WorldGenerationWriter.ChunkWrite> third = WatchingOpens(opened, () => WorldGenerationWriter.Write(generator, reseeded, null, false));
        Assert.That(OutcomeOf(third, locked), Is.EqualTo(WorldGenerationWriter.Outcome.Locked), "the locked chunk");
        Assert.That(opened, Has.None.EndsWith(SceneFileOf(world, locked)), "the locked chunk is not opened");
        Assert.That(OutcomeOf(third, painted), Is.EqualTo(WorldGenerationWriter.Outcome.ChangedByHand), "the chunk painted by hand");
        Assert.That(third.Find(result => result.Coords == painted).Line, Does.Contain(WorldAuthoring.ChunkSceneName(world, painted)), "the refusal names the chunk");
        Assert.That(OutcomeOf(third, withHandWork), Is.EqualTo(WorldGenerationWriter.Outcome.Written), "the chunk with a hand-made spawn point");
        Assert.That(OutcomeOf(third, new Vector2Int(0, 0)), Is.EqualTo(WorldGenerationWriter.Outcome.Written), "chunk (0, 0)");
        Dictionary<string, byte[]> afterThird = Snapshot();
        Assert.That(afterThird[ScenePathOf(world, locked)], Is.EqualTo(files[ScenePathOf(world, locked)]), "the locked chunk's scene is unchanged");
        Assert.That(afterThird[ScenePathOf(world, painted)], Is.EqualTo(files[ScenePathOf(world, painted)]), "the refused chunk's scene is unchanged");
        Assert.That(HandSpawnPointIdIn(world, withHandWork), Is.EqualTo(handId), "the hand-made spawn point stays, with its id");
        Assert.That(new List<WorldDefinition.Entry>(world.Entries).ConvertAll(entry => entry.Name), Is.EqualTo(new[] { TestWorldGenerators.Clearing, "camp" }), "the entries after a rewrite");
        Vector2 clearingComposed = reseeded.Entries.Find(entry => entry.Name == TestWorldGenerators.Clearing).Position;
        Vector2Int under = ChunkCoords.ChunkOf(ChunkCoords.CellOf(clearingComposed));
        WorldGenerationWriter.Outcome underOutcome = OutcomeOf(third, under);
        bool groundWritten = underOutcome == WorldGenerationWriter.Outcome.Written || underOutcome == WorldGenerationWriter.Outcome.Unchanged;
        Assert.That(world.FindEntry(TestWorldGenerators.Clearing).Position, Is.EqualTo(groundWritten ? clearingComposed : clearingBefore), $"the Clearing's entry moves only once chunk {under} under its new place is written ({underOutcome})");

        List<WorldGenerationWriter.ChunkWrite> forced = WorldGenerationWriter.Write(generator, reseeded, new[] { painted }, true);
        Assert.That(OutcomeOf(forced, painted), Is.EqualTo(WorldGenerationWriter.Outcome.Written), "the painted chunk, forced");
        Assert.That(ReadChunk(world, painted, grid => LayerOf(grid, TestWorldGenerators.GroundMaterial("Grass")).GetTile(handCell) == null), Is.True, "the hand paint is gone");
        Assert.That(ReadChunk(world, painted, grid => WorldGenerationWriter.ContentHash(grid, painted, reseeded.LayerOrder)), Is.EqualTo(reseeded.ChunkAt(painted).ContentHash), "the forced chunk holds the composition");
    }

    /// <summary>
    /// On disk, a composed mob whose id another chunk keeping its content still holds by its record (as when a
    /// regeneration moved it out of a chunk since locked) is left out of the chunk it moved to, which is recorded with
    /// what it holds and says so, until that chunk no longer holds it; and a chunk written before such a chunk is refused
    /// in the same write is written again without the mob, so the world never holds two mobs of one id.
    /// </summary>
    [Test]
    public void AMobWhoseIdAKeptChunkStillHolds_IsLeftOutOfTheChunkItMovedTo()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Assert.That(AssetDatabase.IsValidFolder(DiskFolder), Is.False, $"{DiskFolder} is left over from an earlier run");
        Assert.That(WorldLayout.TryFindFreeOrigin(new Vector2Int(2, 2), WorldLayout.AllWorlds(), out Vector2Int origin), Is.True, "a free slot");
        WorldDefinition world = WorldAuthoring.CreateWorld(DiskWorldName, origin, new Vector2Int(2, 2), DiskFolder);
        WorldGenerator generator = perTest.For(world);
        generator.SetRegions(new RegionPlacement(TestWorldGenerators.Clearing, TestWorldGenerators.Area("Clearing"), new Vector2Int(64, 64)));
        WorldComposition composition = generator.Compose();
        Vector2Int last = new(1, 1);
        Vector2Int[] early = { new(0, 0), new(1, 0), new(0, 1) };
        int withMobs = Array.FindIndex(early, coords => ComposedIds(composition, coords).Count > 0);
        Assert.That(withMobs, Is.GreaterThanOrEqualTo(0), "a chunk before the last one has generated mobs");
        Vector2Int moved = early[withMobs];
        string id = ComposedIds(composition, moved)[0];
        List<Vector2Int> others = new() { Vector2Int.zero, new Vector2Int(1, 0), new Vector2Int(0, 1), last };
        others.Remove(moved);
        Assert.That(WorldGenerationWriter.Write(generator, composition, others, false).ConvertAll(result => result.Outcome), Is.All.EqualTo(WorldGenerationWriter.Outcome.Created));
        List<string> keptIds = new(generator.RecordOf(last).MobIds);

        HoldAs(generator, last, "a hash its scene does not have", keptIds, id, locked: false);
        List<WorldGenerationWriter.ChunkWrite> refusedAfter = WorldGenerationWriter.Write(generator, composition, null, false);
        Assert.That(OutcomeOf(refusedAfter, moved), Is.EqualTo(WorldGenerationWriter.Outcome.Created));
        Assert.That(OutcomeOf(refusedAfter, last), Is.EqualTo(WorldGenerationWriter.Outcome.ChangedByHand));
        Assert.That(refusedAfter.Find(result => result.Coords == moved).Line, Does.Contain("left out").And.Contain($"chunk {last}"), "Written again once the chunk after it was refused.");
        Assert.That(GeneratedIdsIn(world, moved), Has.No.Member(id));
        Assert.That(generator.RecordOf(moved).MobIds, Has.No.Member(id));
        Assert.That(generator.RecordOf(moved).WrittenHash, Is.EqualTo(ReadChunk(world, moved, grid => WorldGenerationWriter.ContentHash(grid, moved, composition.LayerOrder))).And.Not.EqualTo(composition.ChunkAt(moved).ContentHash));

        WorldGenerationWriter.Write(generator, composition, new[] { last }, true);
        HoldAs(generator, last, generator.RecordOf(last).WrittenHash, new List<string>(generator.RecordOf(last).MobIds), id, locked: true);
        List<WorldGenerationWriter.ChunkWrite> locked = WorldGenerationWriter.Write(generator, composition, new[] { moved }, false);
        Assert.That(OutcomeOf(locked, moved), Is.EqualTo(WorldGenerationWriter.Outcome.Written), "Not refused: its scene holds what was recorded.");
        Assert.That(GeneratedIdsIn(world, moved), Has.No.Member(id), "Still left out while the locked chunk holds it.");

        HoldAs(generator, last, generator.RecordOf(last).WrittenHash, keptIds, null, locked: false);
        List<WorldGenerationWriter.ChunkWrite> released = WorldGenerationWriter.Write(generator, composition, new[] { moved }, false);
        Assert.That(OutcomeOf(released, moved), Is.EqualTo(WorldGenerationWriter.Outcome.Written));
        Assert.That(GeneratedIdsIn(world, moved), Has.Member(id), "Written in once no other chunk holds it.");
        Assert.That(generator.RecordOf(moved).WrittenHash, Is.EqualTo(composition.ChunkAt(moved).ContentHash));
    }

    /// <summary>
    /// On disk, after the Clearing moves, writing only a chunk other than the one under its entry's new place leaves the
    /// entry where it was, with a warning; writing the chunk under it moves the entry there.
    /// </summary>
    [Test]
    public void AnEntry_MovesOnlyOnceTheChunkUnderItsNewPlaceIsWritten()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Assert.That(AssetDatabase.IsValidFolder(DiskFolder), Is.False, $"{DiskFolder} is left over from an earlier run");
        Assert.That(WorldLayout.TryFindFreeOrigin(new Vector2Int(2, 2), WorldLayout.AllWorlds(), out Vector2Int origin), Is.True, "a free slot");
        WorldDefinition world = WorldAuthoring.CreateWorld(DiskWorldName, origin, new Vector2Int(2, 2), DiskFolder);
        WorldGenerator generator = perTest.For(world);
        generator.SetRegions(new RegionPlacement(TestWorldGenerators.Clearing, TestWorldGenerators.Area("Clearing"), new Vector2Int(64, 64)));
        WorldGenerationWriter.Write(generator, generator.Compose(), null, false);
        Vector2 before = world.FindEntry(TestWorldGenerators.Clearing).Position;

        generator.SetRegions(new RegionPlacement(TestWorldGenerators.Clearing, TestWorldGenerators.Area("Clearing"), new Vector2Int(60, 60)));
        WorldComposition moved = generator.Compose();
        Assert.That(moved.BlockingWarnings, Is.Empty, "nothing stops the write");
        Vector2 after = moved.Entries.Find(entry => entry.Name == TestWorldGenerators.Clearing).Position;
        Assert.That(after, Is.Not.EqualTo(before), "the entry moves with the Clearing");
        Vector2Int under = ChunkCoords.ChunkOf(ChunkCoords.CellOf(after));
        Vector2Int other = under == Vector2Int.zero ? Vector2Int.one : Vector2Int.zero;

        LogAssert.Expect(LogType.Warning, new Regex("stay where they were"));
        List<WorldGenerationWriter.ChunkWrite> elsewhere = WorldGenerationWriter.Write(generator, moved, new[] { other }, false);
        Assert.That(OutcomeOf(elsewhere, other), Is.EqualTo(WorldGenerationWriter.Outcome.Written));
        Assert.That(world.FindEntry(TestWorldGenerators.Clearing).Position, Is.EqualTo(before), $"chunk {under}, under the new place, still holds the old ground");

        WorldGenerationWriter.Write(generator, moved, new[] { under }, false);
        Assert.That(world.FindEntry(TestWorldGenerators.Clearing).Position, Is.EqualTo(after), "the entry moves once its ground is written");
    }

    /// <summary>
    /// Records the chunk at <paramref name="coords"/> as written with <paramref name="hash"/> and holding
    /// <paramref name="ids"/> and <paramref name="extraId"/> (when given), locked or not.
    /// </summary>
    private static void HoldAs(WorldGenerator generator, Vector2Int coords, string hash, List<string> ids, string extraId, bool locked)
    {
        generator.SetRecord(new ChunkRecord(coords, locked, false, 0));
        List<string> held = new(ids);
        if (extraId != null)
        {
            held.Add(extraId);
        }

        generator.SetWrittenHash(coords, hash, held);
    }

    /// <summary>
    /// The ids of the composition's mobs in the chunk at <paramref name="coords"/>, in order.
    /// </summary>
    private static List<string> ComposedIds(WorldComposition composition, Vector2Int coords)
    {
        RectInt bounds = ChunkCoords.Bounds(coords);
        return composition.Mobs.FindAll(mob => bounds.Contains(mob.Cell)).ConvertAll(mob => mob.Id);
    }

    /// <summary>
    /// The ids of the generated spawn points the chunk at <paramref name="coords"/> of <paramref name="world"/> holds on
    /// disk.
    /// </summary>
    private static List<string> GeneratedIdsIn(WorldDefinition world, Vector2Int coords)
    {
        return ReadChunk(world, coords, grid =>
        {
            Transform root = grid.transform.Find(WorldGenerationWriter.GeneratedMobSpawnsName);
            List<string> ids = new();
            if (root != null)
            {
                foreach (PersistentId persistent in root.GetComponentsInChildren<PersistentId>(true))
                {
                    ids.Add(persistent.Id);
                }
            }

            return ids;
        });
    }

    /// <summary>
    /// Asserts that <paramref name="grid"/> holds the chunk at <paramref name="coords"/> as written.
    /// </summary>
    private void ExpectWritten(Grid grid, Vector2Int coords)
    {
        WorldComposition.Chunk chunk = overworld.ChunkAt(coords);
        RectInt bounds = ChunkCoords.Bounds(coords);
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Assert.That(grid.gameObject.activeSelf, Is.True, $"chunk {coords}'s grid is active again");
        Assert.That(renderer.Style == overworld.Style, Is.True, $"chunk {coords}'s style");

        List<TerrainMaterial> expected = new(chunk.Materials);
        foreach (Tilemap listed in Listed(grid))
        {
            TerrainMaterial material = MaterialOf(grid, listed);
            if (material != null && !expected.Contains(material))
            {
                expected.Add(material);
            }
        }

        expected.Sort((a, b) => overworld.IndexOf(a).CompareTo(overworld.IndexOf(b)));
        List<TerrainMaterial> drawn = new();
        for (int i = 0; i < renderer.Layers.Count; i++)
        {
            TerrainBlendRenderer.Layer layer = renderer.Layers[i];
            drawn.Add(layer.Material);
            Assert.That(layer.Tilemap.transform.parent == grid.transform, Is.True, $"chunk {coords}: {layer.Tilemap.name} is the grid's");
            Assert.That(layer.Tilemap.transform.GetSiblingIndex(), Is.EqualTo(i), $"chunk {coords}: {layer.Tilemap.name}'s place");
            NavigationTerrainSource2D source = layer.Tilemap.GetComponent<NavigationTerrainSource2D>();
            Assert.That(source.Priority, Is.EqualTo(TerrainBlendRenderer.NavigationPriorityOf(i)), $"chunk {coords}: {layer.Tilemap.name}'s navigation priority");
            Assert.That(source.TerrainType, Is.EqualTo(AssetDatabase.LoadAssetAtPath<TerrainType2D>(GroundLayerMigration.TerrainTypePath(layer.Material))), $"chunk {coords}: {layer.Tilemap.name}'s terrain type");
            HashSet<Vector2Int> composed = new(overworld.CellsOf(layer.Material, bounds));
            HashSet<Vector2Int> filled = new();
            TileBase tile = TileOf(layer.Material);
            foreach (Vector3Int cell in Filled(layer.Tilemap))
            {
                filled.Add(new Vector2Int(cell.x, cell.y));
                Expect(layer.Tilemap.GetTile(cell) == tile, $"chunk {coords}: {layer.Tilemap.name} holds another tile at {cell}");
            }

            Assert.That(filled.SetEquals(composed), Is.True, $"chunk {coords}: {layer.Tilemap.name} holds {filled.Count} cells, the composition {composed.Count}");
        }

        Assert.That(drawn, Is.EqualTo(expected), $"chunk {coords}'s layers: its list and its listed layers, in the style's order");

        Tilemap raised = renderer.RaisedTilemap;
        bool anyRaised = false;
        foreach (Vector2Int cell in bounds.allPositionsWithin)
        {
            byte kind = overworld.RaisedAt(cell);
            anyRaised |= kind != RaisedGround.Level;
            Expect(raised == null ? kind == RaisedGround.Level : RaisedGround.KindAt(raised, Cell3(cell)) == kind, $"chunk {coords}: raised ground at {cell}");
        }

        Assert.That(raised != null, Is.EqualTo(anyRaised), $"chunk {coords} has a raised tilemap only with raised ground");
        Assert.That(grid.GetComponent<NavigationGrid2D>().RaisedTilemap == raised, Is.True, $"chunk {coords}'s navigation reads its cliff");

        List<WorldComposition.Mob> mobs = MobsOf(coords);
        Transform root = grid.transform.Find(WorldGenerationWriter.GeneratedMobSpawnsName);
        MobSpawnPoint[] points = root != null ? root.GetComponentsInChildren<MobSpawnPoint>(true) : new MobSpawnPoint[0];
        Assert.That(points.Length, Is.EqualTo(mobs.Count), $"chunk {coords}'s generated spawn points");
        List<PersistentId> ids = new();
        for (int i = 0; i < mobs.Count; i++)
        {
            Assert.That(points[i].MobPrefab, Is.EqualTo(mobs[i].Prefab), $"chunk {coords}: spawn point {i}'s prefab");
            Assert.That(points[i].transform.position, Is.EqualTo(AreaLayoutWriter.CellCentre(grid, Cell3(mobs[i].Cell))), $"chunk {coords}: spawn point {i}'s place");
            PersistentId id = points[i].GetComponent<PersistentId>();
            Assert.That(id.Id, Is.EqualTo(mobs[i].Id), $"chunk {coords}: spawn point {i}'s derived id");
            ids.Add(id);
        }

        if (root != null)
        {
            Assert.That(PersistentIds.AddToSpawnPoints(new[] { root.gameObject }), Is.Empty, $"chunk {coords}: every generated spawn point has its id");
            Assert.That(PersistentIds.RepairNew(ids, Object.FindObjectsByType<PersistentId>(FindObjectsInactive.Include)), Is.Empty, $"chunk {coords}: the id watcher keeps every derived id");
        }

        List<WorldComposition.Prop> props = PropsOf(coords);
        Transform propsRoot = grid.transform.Find(WorldGenerationWriter.GeneratedPropsName);
        Assert.That(propsRoot != null ? propsRoot.childCount : 0, Is.EqualTo(props.Count), $"chunk {coords}'s generated props");
        Vector3 origin = grid.CellToWorld(Vector3Int.zero);
        for (int i = 0; i < props.Count; i++)
        {
            Transform instance = propsRoot.GetChild(i);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject) == props[i].Prefab, Is.True, $"chunk {coords}: prop {i} is an instance of {props[i].Prefab.name}");
            Vector2 root2 = ChunkContent.RootOf(props[i]);
            Assert.That(Vector2.Distance(instance.position, new Vector2(origin.x + root2.x, origin.y + root2.y)), Is.LessThan(0.001f), $"chunk {coords}: prop {i}'s place");
            if (props[i].Blocks && PropGridSnapping.TryGetFootprint(props[i].Prefab, out Rect footprint))
            {
                BoundsInt blocked = PropGridSnapping.BlockedCells(grid, instance.position, footprint);
                Assert.That((new Vector2Int(blocked.xMin, blocked.yMin), new Vector2Int(blocked.size.x, blocked.size.y)), Is.EqualTo((props[i].Cell, props[i].Covered)), $"chunk {coords}: prop {i} blocks its composed cells");
            }
        }

        Assert.That(Hash(grid, coords), Is.EqualTo(chunk.ContentHash), $"chunk {coords} read back hashes as composed");
    }

    /// <summary>
    /// Opens each chunk of <paramref name="world"/> and asserts that it hashes to its record and to
    /// <paramref name="composition"/>, its generated spawn points carrying their derived ids, unique in the world.
    /// </summary>
    private static void ExpectScenesHold(WorldDefinition world, WorldComposition composition, WorldGenerator generator)
    {
        List<Scene> opened = new();
        try
        {
            HashSet<string> ids = new();
            foreach (KeyValuePair<Vector2Int, Scene> chunk in WorldAuthoring.OpenChunkScenes(world, opened))
            {
                Grid grid = SceneQuery.FindFirst<WorldChunk>(chunk.Value).GetComponent<Grid>();
                string hash = WorldGenerationWriter.ContentHash(grid, chunk.Key, composition.LayerOrder);
                Assert.That(hash, Is.EqualTo(composition.ChunkAt(chunk.Key).ContentHash).And.EqualTo(generator.RecordOf(chunk.Key).WrittenHash), $"{chunk.Value.name} read back from disk");
                RectInt bounds = ChunkCoords.Bounds(chunk.Key);
                List<WorldComposition.Mob> mobs = composition.Mobs.FindAll(mob => bounds.Contains(mob.Cell));
                Transform root = grid.transform.Find(WorldGenerationWriter.GeneratedMobSpawnsName);
                PersistentId[] persistent = root != null ? root.GetComponentsInChildren<PersistentId>(true) : new PersistentId[0];
                Assert.That(Array.ConvertAll(persistent, id => id.Id), Is.EqualTo(mobs.ConvertAll(mob => mob.Id)), $"{chunk.Value.name}'s derived ids after Repair ids");
                foreach (PersistentId id in persistent)
                {
                    Assert.That(ids.Add(id.Id), Is.True, $"{id.Id} is unique in the world");
                }
            }

            Assert.That(ids, Is.Not.Empty, "the world holds generated spawn points");
        }
        finally
        {
            WorldAuthoring.CloseScenes(opened, false);
        }
    }

    /// <summary>
    /// Paints grass by hand on a cell of the chunk at <paramref name="coords"/> of <paramref name="world"/> that holds
    /// none, saving its scene, and returns the cell.
    /// </summary>
    private static Vector3Int PaintInChunk(WorldDefinition world, Vector2Int coords)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePathOf(world, coords), OpenSceneMode.Additive);
        try
        {
            Grid grid = SceneQuery.FindFirst<WorldChunk>(scene).GetComponent<Grid>();
            Tilemap grass = LayerOf(grid, TestWorldGenerators.GroundMaterial("Grass"));
            Vector3Int cell = FirstCellWithout(coords, grass);
            grass.SetTile(cell, TileOf(TestWorldGenerators.GroundMaterial("Grass")));
            EditorSceneManager.SaveScene(scene);
            return cell;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Adds a hand-made spawn point with an id of its own under a <c>MobSpawns</c> root of the chunk at
    /// <paramref name="coords"/> of <paramref name="world"/>, saving its scene, and returns the id.
    /// </summary>
    private static string AddHandSpawnPoint(WorldDefinition world, Vector2Int coords)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePathOf(world, coords), OpenSceneMode.Additive);
        Scene active = SceneManager.GetActiveScene();
        try
        {
            SceneManager.SetActiveScene(scene);
            GameObject root = new(AreaLayoutWriter.MobSpawnsRootName);
            GameObject point = new("HandMade", typeof(MobSpawnPoint), typeof(PersistentId));
            point.transform.SetParent(root.transform, false);
            point.transform.position = world.ToWorld(ChunkCoords.Bounds(coords).center);
            string id = PersistentIds.NewId();
            PersistentIds.SetId(point.GetComponent<PersistentId>(), id);
            EditorSceneManager.SaveScene(scene);
            return id;
        }
        finally
        {
            SceneManager.SetActiveScene(active);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// The id of the hand-made spawn point in the chunk at <paramref name="coords"/> of <paramref name="world"/>, read
    /// from its saved scene, or null.
    /// </summary>
    private static string HandSpawnPointIdIn(WorldDefinition world, Vector2Int coords)
    {
        return ReadChunk(world, coords, grid =>
        {
            foreach (GameObject root in grid.gameObject.scene.GetRootGameObjects())
            {
                if (root.name == AreaLayoutWriter.MobSpawnsRootName)
                {
                    return root.GetComponentInChildren<PersistentId>(true).Id;
                }
            }

            return null;
        });
    }

    /// <summary>
    /// <paramref name="read"/> of the grid of the chunk at <paramref name="coords"/> of <paramref name="world"/>, opened
    /// from disk and closed again.
    /// </summary>
    private static T ReadChunk<T>(WorldDefinition world, Vector2Int coords, Func<Grid, T> read)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePathOf(world, coords), OpenSceneMode.Additive);
        try
        {
            return read(SceneQuery.FindFirst<WorldChunk>(scene).GetComponent<Grid>());
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Runs <paramref name="write"/>, adding the path of every scene opened meanwhile to <paramref name="opened"/>.
    /// </summary>
    private static List<WorldGenerationWriter.ChunkWrite> WatchingOpens(List<string> opened, Func<List<WorldGenerationWriter.ChunkWrite>> write)
    {
        void Record(Scene scene, OpenSceneMode mode) => opened.Add(scene.path);
        EditorSceneManager.sceneOpened += Record;
        try
        {
            return write();
        }
        finally
        {
            EditorSceneManager.sceneOpened -= Record;
        }
    }

    /// <summary>
    /// Every file under the temporary folder, by path, with its bytes.
    /// </summary>
    private static Dictionary<string, byte[]> Snapshot()
    {
        Dictionary<string, byte[]> files = new();
        foreach (string path in Directory.GetFiles(DiskFolder, "*", SearchOption.AllDirectories))
        {
            files[path.Replace('\\', '/')] = File.ReadAllBytes(path);
        }

        return files;
    }

    /// <summary>
    /// Asserts that <paramref name="after"/> holds the same files with the same bytes as <paramref name="before"/>.
    /// </summary>
    private static void ExpectSameFiles(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after, string what)
    {
        Assert.That(after.Keys, Is.EquivalentTo(before.Keys), $"{what}: the same files");
        List<string> changed = new();
        foreach (KeyValuePair<string, byte[]> file in before)
        {
            if (!System.Linq.Enumerable.SequenceEqual(file.Value, after[file.Key]))
            {
                changed.Add(file.Key);
            }
        }

        Assert.That(changed, Is.Empty, $"{what} changed files");
    }

    /// <summary>
    /// The path of the scene of the chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    private static string ScenePathOf(WorldDefinition world, Vector2Int coords)
    {
        Assert.That(world.TryGetChunkScene(coords, out SceneDefinition definition), Is.True, $"{world.name} has chunk {coords}");
        return definition.ScenePath;
    }

    /// <summary>
    /// The file name of the scene of the chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    private static string SceneFileOf(WorldDefinition world, Vector2Int coords)
    {
        return $"{WorldAuthoring.ChunkSceneName(world, coords)}.unity";
    }

    /// <summary>
    /// What <paramref name="results"/> report for the chunk at <paramref name="coords"/>.
    /// </summary>
    private static WorldGenerationWriter.Outcome OutcomeOf(List<WorldGenerationWriter.ChunkWrite> results, Vector2Int coords)
    {
        int index = results.FindIndex(result => result.Coords == coords);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"a result for chunk {coords}");
        return results[index].Outcome;
    }

    /// <summary>
    /// Asserts that <paramref name="change"/> changes the hash of <paramref name="grid"/> from <paramref name="composed"/>
    /// and that <paramref name="undo"/> brings it back.
    /// </summary>
    private static void ExpectChange(Grid grid, Vector2Int coords, string composed, string what, Action change, Action undo)
    {
        change();
        Assert.That(Hash(grid, coords), Is.Not.EqualTo(composed), $"{what} changes the hash");
        undo();
        Assert.That(Hash(grid, coords), Is.EqualTo(composed), $"{what}, undone, changes nothing");
    }

    /// <summary>
    /// Adds a hand-made spawn point with <paramref name="id"/> at <paramref name="position"/> under a <c>MobSpawns</c>
    /// root of the active scene, for the test to destroy.
    /// </summary>
    private MobSpawnPoint HandSpawnPoint(Vector3 position, string id)
    {
        GameObject root = perTest.Track(new GameObject(AreaLayoutWriter.MobSpawnsRootName));
        GameObject point = new("HandMade", typeof(MobSpawnPoint), typeof(PersistentId));
        point.transform.SetParent(root.transform, false);
        point.transform.position = position;
        PersistentIds.SetId(point.GetComponent<PersistentId>(), id);
        return point.GetComponent<MobSpawnPoint>();
    }

    /// <summary>
    /// Adds <paramref name="material"/>'s layer to <paramref name="grid"/>, empty, and lists it in its
    /// <see cref="AreaTerrain"/>, after its other entries; returns its tilemap.
    /// </summary>
    private static Tilemap ListLayer(Grid grid, TerrainMaterial material)
    {
        Tilemap tilemap = GroundLayerMigration.AddLayer(grid, material);
        SerializedObject data = new(grid.GetComponent<AreaTerrain>());
        SerializedProperty layers = data.FindProperty("layers");
        layers.arraySize++;
        SerializedProperty entry = layers.GetArrayElementAtIndex(layers.arraySize - 1);
        entry.FindPropertyRelative("tilemap").objectReferenceValue = tilemap;
        entry.FindPropertyRelative("fillTile").objectReferenceValue = null;
        data.ApplyModifiedPropertiesWithoutUndo();
        return tilemap;
    }

    /// <summary>
    /// The tilemaps <paramref name="grid"/>'s <see cref="AreaTerrain"/> lists, in its order.
    /// </summary>
    private static List<Tilemap> Listed(Grid grid)
    {
        List<Tilemap> listed = new();
        SerializedProperty layers = new SerializedObject(grid.GetComponent<AreaTerrain>()).FindProperty("layers");
        for (int i = 0; i < layers.arraySize; i++)
        {
            listed.Add((Tilemap)layers.GetArrayElementAtIndex(i).FindPropertyRelative("tilemap").objectReferenceValue);
        }

        return listed;
    }

    /// <summary>
    /// The material <paramref name="grid"/>'s renderer draws <paramref name="tilemap"/> with, or null.
    /// </summary>
    private static TerrainMaterial MaterialOf(Grid grid, Tilemap tilemap)
    {
        foreach (TerrainBlendRenderer.Layer layer in grid.GetComponent<TerrainBlendRenderer>().Layers)
        {
            if (layer.Tilemap == tilemap)
            {
                return layer.Material;
            }
        }

        return null;
    }

    /// <summary>
    /// The tilemap <paramref name="grid"/>'s renderer draws <paramref name="material"/> on, or null.
    /// </summary>
    private static Tilemap LayerOf(Grid grid, TerrainMaterial material)
    {
        foreach (TerrainBlendRenderer.Layer layer in grid.GetComponent<TerrainBlendRenderer>().Layers)
        {
            if (layer.Material == material)
            {
                return layer.Tilemap;
            }
        }

        return null;
    }

    /// <summary>
    /// The project's plain tile of <paramref name="material"/>.
    /// </summary>
    private static TileBase TileOf(TerrainMaterial material)
    {
        return AssetDatabase.LoadAssetAtPath<TileBase>(TerrainTileWriter.TilePath(material));
    }

    /// <summary>
    /// The filled cells of <paramref name="tilemap"/>.
    /// </summary>
    private static List<Vector3Int> Filled(Tilemap tilemap)
    {
        List<Vector3Int> filled = new();
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.HasTile(cell))
            {
                filled.Add(cell);
            }
        }

        return filled;
    }

    /// <summary>
    /// The first cell of the chunk at <paramref name="coords"/>, in row order, where <paramref name="tilemap"/> holds no
    /// tile.
    /// </summary>
    private static Vector3Int FirstCellWithout(Vector2Int coords, Tilemap tilemap)
    {
        foreach (Vector2Int cell in ChunkCoords.Bounds(coords).allPositionsWithin)
        {
            if (!tilemap.HasTile(Cell3(cell)))
            {
                return Cell3(cell);
            }
        }

        Assert.Fail($"{tilemap.name} fills chunk {coords}");
        return default;
    }

    /// <summary>
    /// The first cell of the Overworld, in row order, whose raised ground is <paramref name="kind"/>.
    /// </summary>
    private Vector2Int FirstRaisedCell(byte kind)
    {
        foreach (Vector2Int cell in overworld.Bounds.allPositionsWithin)
        {
            if (overworld.RaisedAt(cell) == kind)
            {
                return cell;
            }
        }

        Assert.Fail($"the Overworld has no raised cell of kind {kind}");
        return default;
    }

    /// <summary>
    /// The first chunk of the Overworld, in row order, with cells of <paramref name="material"/> of its own.
    /// </summary>
    private Vector2Int ChunkWithOwn(TerrainMaterial material)
    {
        uint bit = 1u << overworld.IndexOf(material);
        WorldComposition.Chunk found = overworld.Chunks.Find(chunk => (chunk.OwnMask & bit) != 0);
        Assert.That(found, Is.Not.Null, $"a chunk of the Overworld lays {material.name}");
        return found.Coords;
    }

    /// <summary>
    /// The first chunk of the Overworld, in row order, with mobs, no raised ground and no <paramref name="material"/> in
    /// its list.
    /// </summary>
    private Vector2Int ChunkWithMobsAndNoRaised(TerrainMaterial material)
    {
        foreach (WorldComposition.Chunk chunk in overworld.Chunks)
        {
            bool raised = false;
            foreach (Vector2Int cell in ChunkCoords.Bounds(chunk.Coords).allPositionsWithin)
            {
                raised |= overworld.RaisedAt(cell) != RaisedGround.Level;
            }

            if (!raised && MobsOf(chunk.Coords).Count > 0 && !chunk.Materials.Contains(material))
            {
                return chunk.Coords;
            }
        }

        Assert.Fail($"no chunk of the Overworld has mobs, no raised ground and no {material.name}");
        return default;
    }

    /// <summary>
    /// The Overworld's props in the chunk at <paramref name="coords"/>, in the composition's order.
    /// </summary>
    private List<WorldComposition.Prop> PropsOf(Vector2Int coords)
    {
        return overworld.Props.FindAll(prop => prop.Chunk == coords);
    }

    /// <summary>
    /// A prop placed by hand on <paramref name="cell"/> of <paramref name="grid"/>'s chunk, as the Prop Placement window
    /// places one: an instance of a stone under its scene's Props root, which the test destroys.
    /// </summary>
    private GameObject HandProp(Grid grid, Vector3Int cell)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Props/Rocks/Stones_Small_0.prefab");
        Transform root = PropPlacementWindow.FindOrCreateRoot(grid.gameObject.scene, PropPlacementWindow.PropsRootName);
        perTest.Track(root.gameObject);
        GameObject prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grid.gameObject.scene);
        prop.transform.SetParent(root, false);
        prop.transform.position = grid.CellToWorld(cell) + new Vector3(0.5f, 0.5f, 0f);
        return prop;
    }

    /// <summary>
    /// The Overworld's mobs in the chunk at <paramref name="coords"/>, in the composition's order.
    /// </summary>
    private List<WorldComposition.Mob> MobsOf(Vector2Int coords)
    {
        RectInt bounds = ChunkCoords.Bounds(coords);
        return overworld.Mobs.FindAll(mob => bounds.Contains(mob.Cell));
    }

    /// <summary>
    /// The hash of what <paramref name="grid"/> holds as the Overworld's chunk at <paramref name="coords"/>.
    /// </summary>
    private static string Hash(Grid grid, Vector2Int coords)
    {
        return WorldGenerationWriter.ContentHash(grid, coords, grid.GetComponent<TerrainBlendRenderer>().Style.LayerOrder);
    }

    /// <summary>
    /// <paramref name="cell"/> at z = 0.
    /// </summary>
    private static Vector3Int Cell3(Vector2Int cell)
    {
        return new Vector3Int(cell.x, cell.y, 0);
    }

    /// <summary>
    /// The cell holding <paramref name="point"/>, at z = 0.
    /// </summary>
    private static Vector3Int Cell3(Vector2 point)
    {
        return new Vector3Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), 0);
    }

    /// <summary>
    /// Fails with <paramref name="message"/> when <paramref name="condition"/> is false, formatting the message only
    /// then, for checks run for every cell.
    /// </summary>
    private static void Expect(bool condition, FormattableString message)
    {
        if (!condition)
        {
            Assert.Fail(message.ToString());
        }
    }
}
