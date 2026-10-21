using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tests for the logic the World Generator window calls (Docs/WorldGeneration.md: Testing): the preview's pixels
/// (<see cref="WorldGeneratorPreview"/>) and a <see cref="WorldGeneratorSession"/>'s chunk states, write plans, record
/// toggles and write, in memory on the Overworld's Phase 0 settings on a world lacking chunk (4, 3), composed once,
/// and once on disk on a world of one chunk with no region, created in a temporary folder under <c>Assets/Dev</c> and
/// deleted after.
/// </summary>
public class WorldGeneratorWindowTests
{
    private const string DiskFolder = "Assets/Dev/WorldGeneratorWindowTests";
    private const string DiskWorldName = "WindowTest";
    private static readonly Vector2Int MissingChunk = new(4, 3);

    private TestWorldGenerators generators;
    private WorldGenerator generator;
    private WorldGeneratorSession session;
    private TestWorldGenerators perTest;

    /// <summary>
    /// Composes the Overworld's Phase 0 settings once, on a world that has every chunk but (4, 3).
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        generators = new TestWorldGenerators();
        List<Vector2Int> chunks = new();
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                if (new Vector2Int(x, y) != MissingChunk)
                {
                    chunks.Add(new Vector2Int(x, y));
                }
            }
        }

        generator = TestWorldGenerators.Overworld(generators.Generator("Overworld", new Vector2Int(5, 4), chunks));
        session = new WorldGeneratorSession(generator);
        session.Recompose();
        Assert.That(session.Composition.Warnings, Is.Empty, "the Overworld composes with no warnings");
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
    /// Starts a test's own set of objects with the fixture's generator holding no chunk records.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        perTest = new TestWorldGenerators();
        ClearRecords(generator);
        session.NoteChange(0.0);
        Assert.That(session.IsRecomposeScheduled, Is.False, "the fixture's composition is current");
    }

    /// <summary>
    /// Destroys what the test made, clears the fixture generator's records and undo, and deletes the temporary world on
    /// disk with its folder when the test made one.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        perTest.Destroy();
        ClearRecords(generator);
        Undo.ClearUndo(generator);
        if (AssetDatabase.IsValidFolder(DiskFolder))
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(DiskFolder);
        }
    }

    /// <summary>
    /// The preview paints a cell of the Lakeside's water in the water's base colour, a linked port magenta, a region's
    /// mob red and a wilds mob pink, the default waystone cyan and a gate yellow, and chunk (1, 0)'s count of 14 black; a
    /// locked chunk's stripes are darker and the cells between them as they were; and a chunk of 17 layers (three
    /// regions laid over one another) has its count red, and blue once its record allows it wide.
    /// </summary>
    [Test]
    public void ThePreview_DrawsMaterialsPortsMobsAndEntries_CountsRedPastSixteenUnlessAllowedWide_AndLockedChunksHatched()
    {
        WorldComposition composition = session.Composition;
        Color32[] pixels = WorldGeneratorPreview.Pixels(composition, generator);
        Assert.That(pixels.Length, Is.EqualTo(composition.Size.x * composition.Size.y), "a pixel per cell");

        TerrainMaterial water = TestWorldGenerators.GroundMaterial("Water");
        for (int i = 0; i < composition.IndexOf(water); i++)
        {
            Assert.That(composition.LayerOrder[i].BaseColour, Is.Not.EqualTo(water.BaseColour), $"{composition.LayerOrder[i].name} shares the water's colour");
        }

        Vector2Int lake = FirstCell(composition, cell => composition.TopAt(cell) == water && composition.RegionAt(cell) == composition.FindRegion(TestWorldGenerators.Lakeside).Index && IsPlain(composition, cell));
        Assert.That(PixelAt(pixels, composition, lake), Is.EqualTo(Opaque(water.BaseColour)), $"the water at {lake}");

        WorldComposition.Port port = composition.FindRegion(TestWorldGenerators.Clearing).Ports.Find(candidate => candidate.Linked && !InCount(candidate.Cells[0]));
        Assert.That(PixelAt(pixels, composition, port.Cells[0]), Is.EqualTo(WorldGeneratorPreview.PortColour), $"the Clearing's port at {port.Cells[0]}");
        WorldComposition.Mob regionMob = composition.Mobs.Find(mob => composition.RegionAt(mob.Cell) >= 0 && !NearEntry(composition, mob.Cell) && !InCount(mob.Cell));
        Assert.That(PixelAt(pixels, composition, regionMob.Cell), Is.EqualTo(WorldGeneratorPreview.MobColour), $"the region mob at {regionMob.Cell}");
        WorldComposition.Mob wildsMob = composition.Mobs.Find(mob => composition.RegionAt(mob.Cell) == WorldComposition.Wilds && !NearEntry(composition, mob.Cell) && !InCount(mob.Cell));
        Assert.That(PixelAt(pixels, composition, wildsMob.Cell), Is.EqualTo(WorldGeneratorPreview.WildsMobColour), $"the wilds mob at {wildsMob.Cell}");
        WorldDefinition.Entry waystone = composition.Entries.Find(entry => entry.Waystone && !InCount(Vector2Int.FloorToInt(entry.Position)));
        Assert.That(PixelAt(pixels, composition, Vector2Int.FloorToInt(waystone.Position)), Is.EqualTo(WorldGeneratorPreview.WaystoneColour), $"the waystone {waystone.Name}");
        WorldDefinition.Entry gate = composition.Entries.Find(entry => !entry.Waystone && !InCount(Vector2Int.FloorToInt(entry.Position)));
        Assert.That(PixelAt(pixels, composition, Vector2Int.FloorToInt(gate.Position)), Is.EqualTo(WorldGeneratorPreview.GateColour), $"the gate {gate.Name}");
        Assert.That(composition.ChunkAt(new Vector2Int(1, 0)).Materials.Count, Is.EqualTo(14), "chunk (1, 0) lists 14 materials");
        Assert.That(PixelAt(pixels, composition, CountFoot(new Vector2Int(1, 0))), Is.EqualTo(WorldGeneratorPreview.CountColour), "chunk (1, 0)'s count");

        Vector2Int locked = new(2, 2);
        generator.SetRecord(new ChunkRecord(locked, true, false, 0));
        Color32[] hatched = WorldGeneratorPreview.Pixels(composition, generator);
        Vector2Int stripe = FirstCell(composition, cell => ChunkCoords.ChunkOf(cell) == locked && (cell.x - cell.y) % 8 == 0 && IsPlain(composition, cell));
        Vector2Int between = stripe + Vector2Int.right;
        Assert.That(Brightness(PixelAt(hatched, composition, stripe)), Is.LessThan(Brightness(PixelAt(pixels, composition, stripe))), $"the locked chunk's stripe at {stripe} is darker");
        Assert.That(PixelAt(hatched, composition, between), Is.EqualTo(PixelAt(pixels, composition, between)), $"the cell {between} between its stripes is as it was");

        WorldGenerator crowded = perTest.Generator("Crowded", new Vector2Int(3, 3), null);
        crowded.SetRegions(
            new RegionPlacement(TestWorldGenerators.Lakeside, TestWorldGenerators.Area("Lakeside"), new Vector2Int(96, 96)),
            new RegionPlacement(TestWorldGenerators.Snowfield, TestWorldGenerators.Area("Snowfield"), new Vector2Int(96, 96)),
            new RegionPlacement(TestWorldGenerators.LavaFields, TestWorldGenerators.Area("LavaFields"), new Vector2Int(96, 96)));
        WorldComposition narrow = crowded.Compose();
        WorldComposition.Chunk past = narrow.Chunks.Find(chunk => chunk.Materials.Count > TerrainControlMap.NarrowLayers);
        Assert.That(past, Is.Not.Null, "a chunk past 16 materials");
        Assert.That(PixelAt(WorldGeneratorPreview.Pixels(narrow, crowded), narrow, CountFoot(past.Coords)), Is.EqualTo(WorldGeneratorPreview.PastNarrowColour), $"chunk {past.Coords}'s count of {past.Materials.Count}");
        crowded.SetRecord(new ChunkRecord(past.Coords, false, true, 0));
        WorldComposition wide = crowded.Compose();
        Assert.That(PixelAt(WorldGeneratorPreview.Pixels(wide, crowded), wide, CountFoot(past.Coords)), Is.EqualTo(WorldGeneratorPreview.WideColour), $"chunk {past.Coords}'s count, allowed wide");
    }

    /// <summary>
    /// Each chunk's state comes from its record and its composed hash alone, opening no scene: a chunk recorded with
    /// its composed hash is unchanged, one recorded with another hash or with no record changed, a locked one locked, the
    /// chunk the world lacks missing, and locked again once its record locks it.
    /// </summary>
    [Test]
    public void TheChunkStates_ComeFromTheRecordsAndTheComposedHashesAlone_OpeningNoScene()
    {
        Vector2Int unchanged = new(0, 0);
        Vector2Int stale = new(1, 0);
        Vector2Int locked = new(2, 0);
        generator.SetWrittenHash(unchanged, session.Composition.ChunkAt(unchanged).ContentHash);
        generator.SetWrittenHash(stale, "0123456789abcdef");
        generator.SetRecord(new ChunkRecord(locked, true, false, 0));
        List<string> opened = WatchingOpens(() => session.NoteChange(0.0));

        Assert.That(opened, Is.Empty, "no scene is opened");
        Assert.That(session.StateOf(unchanged), Is.EqualTo(WorldGeneratorSession.ChunkState.Unchanged), "the chunk recorded with its composed hash");
        Assert.That(session.StateOf(stale), Is.EqualTo(WorldGeneratorSession.ChunkState.Changed), "the chunk recorded with another hash");
        Assert.That(session.StateOf(new Vector2Int(3, 0)), Is.EqualTo(WorldGeneratorSession.ChunkState.Changed), "a chunk with no record");
        Assert.That(session.StateOf(locked), Is.EqualTo(WorldGeneratorSession.ChunkState.Locked), "the locked chunk");
        Assert.That(session.StateOf(MissingChunk), Is.EqualTo(WorldGeneratorSession.ChunkState.Missing), "the chunk the world lacks");
        Assert.That(session.CountOf(WorldGeneratorSession.ChunkState.Changed), Is.EqualTo(17), "the changed chunks");

        generator.SetRecord(new ChunkRecord(MissingChunk, true, false, 0));
        session.NoteChange(0.0);
        Assert.That(session.StateOf(MissingChunk), Is.EqualTo(WorldGeneratorSession.ChunkState.Locked), "a lock comes before a missing scene");
    }

    /// <summary>
    /// With one chunk unchanged, one locked and one missing: Write changed chunks covers every chunk and writes every
    /// other one in row order, the missing one created, and is not forced; Write every chunk writes the unchanged one
    /// too, forced; Write selected chunks covers the selected ones inside the world in row order and writes them, forced,
    /// but the locked one; none writes the locked chunk. Each names the problem that the world is no asset, and asks with the chunks it writes.
    /// </summary>
    [Test]
    public void TheWriteButtons_ChangedSkipsUnchangedAndLocked_SelectedAndEveryAreForcedWithUnchanged_NoneWritesLocked()
    {
        Vector2Int unchanged = new(0, 0);
        Vector2Int locked = new(2, 0);
        generator.SetWrittenHash(unchanged, session.Composition.ChunkAt(unchanged).ContentHash);
        generator.SetRecord(new ChunkRecord(locked, true, false, 0));
        session.NoteChange(0.0);
        List<Vector2Int> all = session.Composition.Chunks.ConvertAll(chunk => chunk.Coords);

        WorldGeneratorSession.WritePlan changed = session.Plan(WorldGeneratorSession.Scope.Changed, null);
        Assert.That(changed.Targets, Is.EqualTo(all), "Write changed chunks covers every chunk, so the writer reports each");
        Assert.That(changed.Chunks, Is.EqualTo(all.FindAll(coords => coords != unchanged && coords != locked)), "Write changed chunks");
        Assert.That(changed.Created, Is.EqualTo(new[] { MissingChunk }), "it creates the missing chunk");
        Assert.That(changed.Force, Is.False, "it is not forced");
        Assert.That((changed.Unchanged, changed.Locked), Is.EqualTo((1, 1)), "it leaves out one unchanged and one locked");

        WorldGeneratorSession.WritePlan every = session.Plan(WorldGeneratorSession.Scope.Every, null);
        Assert.That(every.Chunks, Is.EqualTo(all.FindAll(coords => coords != locked)), "Write every chunk");
        Assert.That(every.Force, Is.True, "it is forced");

        WorldGeneratorSession.WritePlan selected = session.Plan(WorldGeneratorSession.Scope.Selected, new[] { new Vector2Int(3, 1), locked, new Vector2Int(9, 9), unchanged });
        Assert.That(selected.Targets, Is.EqualTo(new[] { unchanged, locked, new Vector2Int(3, 1) }), "Write selected chunks covers the selected chunks inside the world");
        Assert.That(selected.Chunks, Is.EqualTo(new[] { unchanged, new Vector2Int(3, 1) }), "Write selected chunks");
        Assert.That((selected.Force, selected.Locked), Is.EqualTo((true, 1)), "it is forced and leaves out the locked one");

        foreach (WorldGeneratorSession.WritePlan plan in new[] { changed, every, selected })
        {
            Assert.That(plan.Problems, Has.Some.Contains("is not saved as an asset"), $"{plan.Scope}: the world is no asset");
            Assert.That(plan.Question(generator.World.name), Does.Contain($"({plan.Chunks[0].x}, {plan.Chunks[0].y})").And.Contain(plan.Force ? "Forced" : "refused"), $"{plan.Scope}'s question");
        }
    }

    /// <summary>
    /// Locking a chunk through the session adds a locked record for it in row order, leaves every other record and the
    /// settings the composition is made from as they were, and recomposes nothing; undoing it takes the record away.
    /// Allowing a chunk wide changes only that flag of its record, keeping its written hash, and has the world recomposed;
    /// undoing it puts the record back and nothing waits to recompose.
    /// </summary>
    [Test]
    public void LockingOrAllowingAChunkWide_ChangesOnlyItsRecord_WithUndo()
    {
        Vector2Int written = new(0, 0);
        Vector2Int target = new(3, 2);
        generator.SetWrittenHash(written, session.Composition.ChunkAt(written).ContentHash);
        generator.SetRecord(new ChunkRecord(new Vector2Int(1, 3), true, false, 0));
        session.NoteChange(0.0);
        string settings = session.SettingsKey();
        List<string> before = Records(generator, target);
        int version = session.PreviewVersion;

        session.SetLocked(new[] { target }, true);
        Assert.That(generator.RecordOf(target) != null && generator.RecordOf(target).Locked, Is.True, "the chunk is locked");
        Assert.That(generator.Chunks[1].Coords, Is.EqualTo(target), "its record goes in row order");
        Assert.That(Records(generator, target), Is.EqualTo(before), "the other records");
        Assert.That(session.SettingsKey(), Is.EqualTo(settings), "the settings the composition is made from");
        Assert.That(session.IsRecomposeScheduled, Is.False, "a lock recomposes nothing");
        Assert.That(session.StateOf(target), Is.EqualTo(WorldGeneratorSession.ChunkState.Locked), "its state");
        Assert.That(session.PreviewVersion, Is.Not.EqualTo(version), "the preview redraws its hatching");

        Undo.PerformUndo();
        session.NoteChange(0.0);
        Assert.That(generator.RecordOf(target), Is.Null, "undone, the record goes");
        Assert.That(Records(generator, target), Is.EqualTo(before), "undone, the other records");
        Assert.That(session.StateOf(target), Is.EqualTo(WorldGeneratorSession.ChunkState.Changed), "undone, its state");

        string hash = generator.RecordOf(written).WrittenHash;
        List<string> others = Records(generator, written);
        session.SetAllowWide(new[] { written }, true);
        ChunkRecord record = generator.RecordOf(written);
        Assert.That((record.AllowWide, record.Locked, record.WrittenHash), Is.EqualTo((true, false, hash)), "only its wide allowance changes");
        Assert.That(Records(generator, written), Is.EqualTo(others), "the other records");
        Assert.That(session.IsRecomposeScheduled, Is.True, "the world recomposes");
        Assert.That(session.SettingsKey(), Is.Not.EqualTo(settings), "the settings the composition is made from changed");

        Undo.PerformUndo();
        session.NoteChange(0.0);
        record = generator.RecordOf(written);
        Assert.That((record.AllowWide, record.WrittenHash), Is.EqualTo((false, hash)), "undone, the record is as it was");
        Assert.That(Records(generator, target), Is.EqualTo(before), "undone, every record");
        Assert.That(session.IsRecomposeScheduled, Is.False, "undone, nothing waits to recompose");
    }

    /// <summary>
    /// On disk, a world of one chunk with no region (whose one warning, no default entry, stops a write) and no chunk
    /// scene yet: an unsaved untitled scene stops Write changed chunks, which would create the chunk; then it asks once,
    /// and answered Write writes nothing; answered Write anyway it creates and writes the chunk and runs the whole-world
    /// steps, reporting each chunk's line and each step's line with its time, and the chunk is unchanged; Write changed
    /// chunks then has nothing to write and asks nothing, while Write every chunk lists it. A new world seed has the world
    /// recomposed after the delay and the chunk changed; its scene open with unsaved changes stops every write without
    /// asking; once locked, no button writes it.
    /// </summary>
    [Test]
    public void Write_OnDisk_AsksFirst_CreatesAndWritesTheChunk_RunsTheSteps_AndTheStatesFollow()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Assert.That(AssetDatabase.IsValidFolder(DiskFolder), Is.False, $"{DiskFolder} is left over from an earlier run");
        int buildScenes = EditorBuildSettings.scenes.Length;
        Assert.That(WorldLayout.TryFindFreeOrigin(Vector2Int.one, WorldLayout.AllWorlds(), out Vector2Int origin), Is.True, "a free slot");
        WorldDefinition world = WorldAuthoring.CreateWorld(DiskWorldName, origin, Vector2Int.one, DiskFolder);
        WorldGenerator alone = perTest.For(world);
        WorldGeneratorSession disk = new(alone);
        disk.Recompose();
        Vector2Int chunk = Vector2Int.zero;
        Assert.That(disk.StateOf(chunk), Is.EqualTo(WorldGeneratorSession.ChunkState.Missing), "the chunk has no scene yet");
        Assert.That(disk.Composition.BlockingWarnings, Has.Count.EqualTo(1), "one warning stops a write");

        Scene untitled = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.MarkSceneDirty(untitled);
        Assert.That(disk.Plan(WorldGeneratorSession.Scope.Changed, null).Problems, Has.Some.Contains("untitled scene has unsaved changes"), "an unsaved untitled scene stops creating chunks");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        List<WorldGeneratorSession.WritePlan> asked = new();
        WorldGeneratorSession.WritePlan plan = disk.Plan(WorldGeneratorSession.Scope.Changed, null);
        Assert.That(plan.Chunks, Is.EqualTo(new[] { chunk }), "Write changed chunks writes the chunk");
        Assert.That(plan.Created, Is.EqualTo(new[] { chunk }), "it creates it");
        Assert.That(plan.Problems, Is.Empty, "nothing stops it but the warning");
        List<string> refused = disk.Write(plan, question => Answer(asked, question, WorldGeneratorSession.Answer.Write));
        Assert.That((asked.Count, refused[0], world.HasChunk(chunk)), Is.EqualTo((1, "Cancelled; nothing written.", false)), "answered Write past a warning that stops it");

        List<string> report = disk.Write(disk.Plan(WorldGeneratorSession.Scope.Changed, null), question => Answer(asked, question, WorldGeneratorSession.Answer.WriteAnyway));
        string lines = string.Join("\n", report);
        Assert.That(world.HasChunk(chunk), Is.True, lines);
        Assert.That(report, Has.Some.StartsWith($"{WorldAuthoring.ChunkSceneName(world, chunk)}: created and written in"), "the chunk's line");
        foreach (string step in new[] { "Sync layer lists: ", "Bake changed coasts: ", "Repair ids: 0 changed in ", "Validate: 0 problems in " })
        {
            Assert.That(report.Exists(line => line.StartsWith(step) && line.EndsWith(" ms.")), Is.True, $"{step}line with its time\n{lines}");
        }

        Assert.That(disk.StateOf(chunk), Is.EqualTo(WorldGeneratorSession.ChunkState.Unchanged), "the chunk written is unchanged");
        Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(buildScenes), "a dev world's chunk stays out of the build");
        int questions = asked.Count;
        Assert.That(disk.Write(disk.Plan(WorldGeneratorSession.Scope.Changed, null), question => Answer(asked, question, WorldGeneratorSession.Answer.Write))[0], Does.StartWith("Nothing to write"), "Write changed chunks after the write");
        Assert.That(asked.Count, Is.EqualTo(questions), "nothing to write, nothing asked");
        Assert.That(disk.Plan(WorldGeneratorSession.Scope.Every, null).Chunks, Is.EqualTo(new[] { chunk }), "Write every chunk lists the unchanged chunk");

        alone.SetSeed(2);
        Assert.That(disk.NoteChange(10.0), Is.True, "a new seed has the world recomposed");
        Assert.That(disk.FollowRecompose(10.0 + WorldGeneratorSession.RecomposeDelay - 0.05), Is.False, "not before the delay");
        Assert.That(disk.FollowRecompose(10.0 + WorldGeneratorSession.RecomposeDelay + 0.05), Is.True, "after it");
        Assert.That(disk.StateOf(chunk), Is.EqualTo(WorldGeneratorSession.ChunkState.Changed), "the new seed changes the chunk");

        Scene scene = EditorSceneManager.OpenScene(world.Chunks[0].Scene.ScenePath, OpenSceneMode.Additive);
        EditorSceneManager.MarkSceneDirty(scene);
        WorldGeneratorSession.WritePlan unsaved = disk.Plan(WorldGeneratorSession.Scope.Every, null);
        Assert.That(unsaved.Problems, Has.Some.Contains(scene.name), "an open chunk scene with unsaved changes stops the write");
        Assert.That(disk.Write(unsaved, question => Answer(asked, question, WorldGeneratorSession.Answer.WriteAnyway))[0], Is.EqualTo("Nothing written:"), "nothing written");
        Assert.That(asked.Count, Is.EqualTo(questions), "nothing asked");
        EditorSceneManager.CloseScene(scene, true);

        disk.SetLocked(new[] { chunk }, true);
        Assert.That(disk.StateOf(chunk), Is.EqualTo(WorldGeneratorSession.ChunkState.Locked), "the chunk locked");
        Assert.That(disk.Plan(WorldGeneratorSession.Scope.Every, null).Chunks, Is.Empty, "no button writes a locked chunk");
        Undo.ClearUndo(alone);
    }

    /// <summary>
    /// Records <paramref name="question"/> in <paramref name="asked"/> and gives <paramref name="answer"/>.
    /// </summary>
    private static WorldGeneratorSession.Answer Answer(List<WorldGeneratorSession.WritePlan> asked, WorldGeneratorSession.WritePlan question, WorldGeneratorSession.Answer answer)
    {
        asked.Add(question);
        return answer;
    }

    /// <summary>
    /// Removes every chunk record of <paramref name="target"/>, without undo.
    /// </summary>
    private static void ClearRecords(WorldGenerator target)
    {
        SerializedObject data = new(target);
        data.FindProperty("chunks").arraySize = 0;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Every record of <paramref name="target"/> but the one of <paramref name="except"/>, as text, in order.
    /// </summary>
    private static List<string> Records(WorldGenerator target, Vector2Int except)
    {
        List<string> records = new();
        foreach (ChunkRecord record in target.Chunks)
        {
            if (record.Coords != except)
            {
                records.Add($"{record.Coords} {record.WrittenHash} {record.Locked} {record.AllowWide} {record.WildsSeed}");
            }
        }

        return records;
    }

    /// <summary>
    /// Runs <paramref name="action"/> and returns the path of every scene opened meanwhile.
    /// </summary>
    private static List<string> WatchingOpens(Action action)
    {
        List<string> opened = new();
        void Record(Scene scene, OpenSceneMode mode) => opened.Add(scene.path);
        EditorSceneManager.sceneOpened += Record;
        try
        {
            action();
        }
        finally
        {
            EditorSceneManager.sceneOpened -= Record;
        }

        return opened;
    }

    /// <summary>
    /// The first cell of <paramref name="composition"/>, in row order, that <paramref name="match"/> takes.
    /// </summary>
    private static Vector2Int FirstCell(WorldComposition composition, Func<Vector2Int, bool> match)
    {
        foreach (Vector2Int cell in composition.Bounds.allPositionsWithin)
        {
            if (match(cell))
            {
                return cell;
            }
        }

        Assert.Fail("no cell matches");
        return default;
    }

    /// <summary>
    /// Whether nothing is drawn over <paramref name="cell"/>'s ground: it lies off the chunk borders and the count
    /// digits, is no region's outline cell and holds no mob, raised ground or entry mark.
    /// </summary>
    private static bool IsPlain(WorldComposition composition, Vector2Int cell)
    {
        if (cell.x % ChunkCoords.Size == 0 || cell.y % ChunkCoords.Size == 0 || composition.RaisedAt(cell) != RaisedGround.Level || NearEntry(composition, cell) || InCount(cell))
        {
            return false;
        }

        foreach (WorldComposition.Region region in composition.Regions)
        {
            if (region.Outline.Contains(cell))
            {
                return false;
            }
        }

        return !composition.Mobs.Exists(mob => mob.Cell == cell);
    }

    /// <summary>
    /// Whether <paramref name="cell"/> lies within the box of its chunk's count of up to two digits, drawn over
    /// everything else.
    /// </summary>
    private static bool InCount(Vector2Int cell)
    {
        Vector2Int count = WorldGeneratorPreview.CountOrigin(ChunkCoords.ChunkOf(cell));
        return cell.x >= count.x && cell.x < count.x + 7 && cell.y <= count.y && cell.y > count.y - 5;
    }

    /// <summary>
    /// Whether <paramref name="cell"/> lies under an entry's cross.
    /// </summary>
    private static bool NearEntry(WorldComposition composition, Vector2Int cell)
    {
        foreach (WorldDefinition.Entry entry in composition.Entries)
        {
            Vector2Int at = Vector2Int.FloorToInt(entry.Position);
            if (Mathf.Abs(at.x - cell.x) + Mathf.Abs(at.y - cell.y) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The bottom-left pixel of the first digit of the count of the chunk at <paramref name="coords"/>, lit for every
    /// first digit from 1 to 3.
    /// </summary>
    private static Vector2Int CountFoot(Vector2Int coords)
    {
        return WorldGeneratorPreview.CountOrigin(coords) + new Vector2Int(0, -4);
    }

    /// <summary>
    /// The pixel of <paramref name="cell"/>.
    /// </summary>
    private static Color32 PixelAt(Color32[] pixels, WorldComposition composition, Vector2Int cell)
    {
        return pixels[cell.y * composition.Size.x + cell.x];
    }

    /// <summary>
    /// <paramref name="colour"/> made opaque.
    /// </summary>
    private static Color32 Opaque(Color colour)
    {
        colour.a = 1f;
        return colour;
    }

    /// <summary>
    /// The sum of <paramref name="colour"/>'s channels.
    /// </summary>
    private static int Brightness(Color32 colour)
    {
        return colour.r + colour.g + colour.b;
    }
}
