using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// Writes a <see cref="WorldComposition"/> into its world's chunk scenes (Docs/WorldGeneration.md: Architecture:
/// Writing). <see cref="WriteChunk"/> writes one open chunk grid, inactive meanwhile so its renderer builds once, with
/// its scene the active one: its ground layers become its list's materials in the style's order plus the layers its
/// <see cref="AreaTerrain"/> lists, each cleared and filled in one batch with its material's tile in exactly the
/// composition's cells, and a layer neither listed nor kept is removed; its raised tilemap is written, or removed when
/// it has no raised ground; the base ground layers it lays are appended to its <see cref="AreaTerrain"/>, which is never
/// removed from or reordered; the spawn points under its grid's <see cref="GeneratedMobSpawnsName"/> become its mobs,
/// each with its prefab and its derived <see cref="PersistentId"/>; and the prop instances under its grid's
/// <see cref="GeneratedPropsName"/> become its props. <see cref="Write"/> writes chunks of a world
/// one at a time: it creates the missing ones first, leaves the locked ones alone and skips those whose composed hash
/// equals their record without opening them, refuses, unless forced, a chunk whose scene no longer hashes to its record
/// (<see cref="ContentHash"/>), and opens, writes, saves and closes the others, recording their hashes; then it sets the
/// generated entries first in the world's list. <see cref="RunWorldSteps"/> runs the World Editor's whole-world steps
/// after a write. Nothing runs in play mode, and writing is no undo step: it spans closed scenes.
/// </summary>
public static class WorldGenerationWriter
{
    public const string GeneratedMobSpawnsName = "GeneratedMobSpawns";
    public const string GeneratedPropsName = "GeneratedProps";

    private const string Owner = "World generation";
    private const int ListedInError = 3;

    /// <summary>
    /// What <see cref="Write"/> did with a chunk.
    /// </summary>
    public enum Outcome
    {
        Written,
        Created,
        Unchanged,
        Locked,
        ChangedByHand,
        OutsideTheWorld,
        Failed
    }

    /// <summary>
    /// What <see cref="Write"/> did with one chunk, how long it took, and the line reporting it.
    /// </summary>
    public readonly struct ChunkWrite
    {
        /// <summary>
        /// The chunk at <paramref name="coords"/> had <paramref name="outcome"/> in <paramref name="milliseconds"/>,
        /// reported by <paramref name="line"/>.
        /// </summary>
        internal ChunkWrite(Vector2Int coords, Outcome outcome, double milliseconds, string line)
        {
            Coords = coords;
            Outcome = outcome;
            Milliseconds = milliseconds;
            Line = line;
        }

        public Vector2Int Coords { get; }
        public Outcome Outcome { get; }
        public double Milliseconds { get; }
        public string Line { get; }
    }

    /// <summary>
    /// Writes the chunks of <paramref name="generator"/>'s world at <paramref name="chunks"/> (every chunk of the world,
    /// in row order, when null) from <paramref name="composition"/>, one at a time, and returns what it did with each.
    /// The missing chunks are created first (<see cref="WorldAuthoring.CreateChunk"/>). A locked chunk is never opened,
    /// and a chunk whose composed hash equals its record is skipped without being opened. Each other chunk is opened
    /// (or used as it is when its scene is open), refused when it no longer hashes to its record (to an empty chunk's
    /// hash when it has none), written (<see cref="WriteChunk"/>), saved, recorded with the ids of the generated spawn
    /// points it wrote, and closed; one whose scene cannot be saved fails and is not recorded. With
    /// <paramref name="force"/> every listed chunk but the locked ones is written whatever its record says. A composed
    /// mob whose id a chunk keeping its content (locked, not listed, refused or failed) still holds by its record is left
    /// out of the chunk it moved to, so the world never holds two mobs of one id; a chunk written before such a chunk was
    /// refused is written again without them. The generated entries then go first in the world's list, in the
    /// composition's order, before the entries made by hand, each moving only once the chunk under its new place holds
    /// the composition's ground, and the generator (when it is an asset) and the world are saved. Refuses, with an
    /// error and writing nothing, in play mode, for a composition of another world, for a composition with warnings
    /// about layer lists or reachability (<see cref="WorldComposition.BlockingWarnings"/>) unless
    /// <paramref name="despiteWarnings"/>, for a world that is no asset, and while chunks are to be created and an
    /// untitled scene has unsaved changes (a new chunk scene cannot open beside it).
    /// </summary>
    public static List<ChunkWrite> Write(WorldGenerator generator, WorldComposition composition, IEnumerable<Vector2Int> chunks, bool force, bool despiteWarnings = false)
    {
        List<ChunkWrite> results = new();
        string problem = WriteProblem(generator, composition, despiteWarnings);
        List<Vector2Int> targets = problem == null ? Targets(generator.World, chunks) : null;
        List<Vector2Int> missing = problem == null ? Missing(generator, targets) : null;
        if (problem == null && missing.Count > 0 && HasUnsavedUntitledScene())
        {
            problem = $"{generator.World.name}: {missing.Count} chunks are to be created and an untitled scene has unsaved changes, beside which a new chunk scene cannot open; save or close it first.";
        }

        if (problem != null)
        {
            Debug.LogError(problem, generator);
            return results;
        }

        WorldDefinition world = generator.World;
        List<string> openBefore = OpenScenePaths();
        Scene activeBefore = SceneManager.GetActiveScene();
        bool generatorChanged = false;
        try
        {
            Dictionary<Vector2Int, double> created = new();
            foreach (Vector2Int coords in missing)
            {
                Stopwatch watch = Stopwatch.StartNew();
                WorldAuthoring.CreateChunk(world, coords);
                created[coords] = watch.Elapsed.TotalMilliseconds;
            }

            string emptyHash = ChunkContent.EmptyHash(composition.LayerOrder);
            Dictionary<string, List<Vector2Int>> holders = RecordedHolders(generator);
            Dictionary<Vector2Int, Outcome> outcomes = new();
            foreach (Vector2Int coords in targets)
            {
                ChunkWrite result = WriteOne(generator, composition, coords, force, created, emptyHash, LeftOut(generator, composition, coords, holders, targets, outcomes));
                outcomes[coords] = result.Outcome;
                results.Add(result);
            }

            foreach (Vector2Int coords in targets)
            {
                if (IsWritten(outcomes[coords]) && HoldsKeptIds(generator, coords, holders, targets, outcomes))
                {
                    ChunkWrite again = WriteOne(generator, composition, coords, true, created, emptyHash, LeftOut(generator, composition, coords, holders, targets, outcomes));
                    outcomes[coords] = again.Outcome;
                    results[results.FindIndex(result => result.Coords == coords)] = again;
                }
            }

            generatorChanged |= results.Exists(result => IsWritten(result.Outcome));
            generatorChanged |= SetEntries(generator, composition, outcomes);
        }
        finally
        {
            CloseOpenedChunks(world, openBefore);
            if (activeBefore.IsValid() && activeBefore.isLoaded)
            {
                SceneManager.SetActiveScene(activeBefore);
            }

            if (generatorChanged && EditorUtility.IsPersistent(generator))
            {
                EditorUtility.SetDirty(generator);
                AssetDatabase.SaveAssetIfDirty(generator);
            }

            AssetDatabase.SaveAssetIfDirty(world);
        }

        return results;
    }

    /// <summary>
    /// Writes the chunk at <paramref name="coords"/> of <paramref name="composition"/> into <paramref name="grid"/>, an
    /// open chunk's grid, and returns the hash of what it wrote (the chunk's composed
    /// <see cref="WorldComposition.Chunk.ContentHash"/>), or null after logging why it could not: in play mode, for a
    /// grid of other cells than one unit or of another chunk, for a chunk the composition does not have, or for a
    /// material without a tile or terrain type or raised ground without its tile. The grid is inactive while it is
    /// written, so its renderer builds once as it comes back, and its scene is the active scene, so every object made
    /// lands in it. Its ground layers become the chunk's list in the style's order plus each layer its
    /// <see cref="AreaTerrain"/> lists (made with one, <see cref="WorldAuthoring.SetUpTerrainEdits(Grid)"/>, when it has
    /// none), set up as <see cref="GroundLayerMigration.AddLayer(Grid, TerrainMaterial, bool)"/> sets a layer up
    /// (<see cref="AreaGroundSetup.SetUpLayers"/>): every layer is cleared and filled in one batch with its material's
    /// tile in exactly the composition's cells of the chunk, and a layer neither listed nor kept is removed. Each base
    /// ground layer (<see cref="WorldComposition.Grounds"/>) with cells in the chunk that its
    /// <see cref="AreaTerrain"/> does not list yet is appended to it, and each listed layer's fill tile is its tile. The
    /// raised tilemap is set up and written (<see cref="AreaGroundSetup.SetUpRaised"/>), with the tiles of the region
    /// owning each cell, or removed when the chunk has no raised ground. The spawn points under the grid's
    /// <see cref="GeneratedMobSpawnsName"/> (made when missing) become the chunk's mobs in order, each at its cell's
    /// centre with its prefab and its derived id set as it is made, but for the mobs whose ids are in
    /// <paramref name="leaveOut"/> (another chunk keeping its content still holds them), in which case the hash returned
    /// is the one read back from the grid. The prop instances under the grid's <see cref="GeneratedPropsName"/> (made
    /// when missing) become the chunk's props in order, each where <see cref="ChunkContent.RootOf"/> puts it; every object
    /// outside those two roots stays.
    /// </summary>
    public static string WriteChunk(Grid grid, WorldComposition composition, Vector2Int coords, ICollection<string> leaveOut = null)
    {
        string problem = ChunkProblem(grid, composition, coords);
        if (problem == null && !grid.TryGetComponent(out AreaTerrain _))
        {
            WorldAuthoring.SetUpTerrainEdits(grid);
        }

        List<AreaGenerator.TerrainLayer> layers = problem == null ? PlanLayers(grid, composition, coords, out problem) : null;
        List<Vector3Int> raisedCells = new();
        List<TileBase> raisedTiles = new();
        problem ??= PlanRaised(composition, coords, raisedCells, raisedTiles);
        if (problem != null)
        {
            Debug.LogError(problem, grid);
            return null;
        }

        Scene scene = grid.gameObject.scene;
        Scene active = SceneManager.GetActiveScene();
        bool wasActive = grid.gameObject.activeSelf;
        if (active != scene)
        {
            SceneManager.SetActiveScene(scene);
        }

        grid.gameObject.SetActive(false);
        try
        {
            List<string> changes = new();
            Tilemap[] tilemaps = WriteGround(grid, composition, coords, layers, changes);
            ListGrounds(grid, composition, coords, layers, tilemaps);
            WriteRaised(grid, raisedCells, raisedTiles, changes);
            WriteMobSpawns(grid, composition, coords, leaveOut);
            WriteProps(grid, composition, coords);
        }
        finally
        {
            grid.gameObject.SetActive(wasActive);
            if (active.IsValid() && active.isLoaded && SceneManager.GetActiveScene() != active)
            {
                SceneManager.SetActiveScene(active);
            }
        }

        if (grid.TryGetComponent(out NavigationGrid2D navigation))
        {
            navigation.MarkGizmoGridStale();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        return MobsOf(composition, coords, leaveOut).Count < MobsOf(composition, coords, null).Count
            ? ContentHash(grid, coords, composition.LayerOrder)
            : composition.ChunkAt(coords).ContentHash;
    }

    /// <summary>
    /// The hash of what <paramref name="grid"/>, the chunk at <paramref name="coords"/>, holds now over the materials of
    /// <paramref name="layerOrder"/>: its ground layers' and raised ground's cells and its generated spawn points
    /// (<see cref="ChunkContent"/>), which equals the composition's hash of the chunk right after it was written there.
    /// </summary>
    public static string ContentHash(Grid grid, Vector2Int coords, IReadOnlyList<TerrainMaterial> layerOrder)
    {
        return ChunkContent.Read(grid, coords, layerOrder).Hash();
    }

    /// <summary>
    /// Runs the World Editor's whole-world steps on <paramref name="world"/> after a write, in order: Sync layer lists
    /// (every chunk's list covering its neighbours' border materials, the locked chunks' hand work included), Bake
    /// changed coasts, Repair ids (which keeps derived ids, being set and unique) and Validate. Returns a line per step
    /// with its time, each followed by the step's own lines. Refuses in play mode.
    /// </summary>
    public static List<string> RunWorldSteps(WorldDefinition world)
    {
        List<string> report = new();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{world.name}: the whole-world steps run outside play mode only.", world);
            return report;
        }

        Stopwatch watch = Stopwatch.StartNew();
        List<string> added = WorldAuthoring.SyncLayerLists(world);
        report.Add($"Sync layer lists: {added.Count} layers added in {watch.ElapsedMilliseconds} ms.");
        report.AddRange(added);
        watch.Restart();
        List<string> baked = WorldAuthoring.BakeCoastMaps(world, true);
        report.Add($"Bake changed coasts: {watch.ElapsedMilliseconds} ms.");
        report.AddRange(baked);
        watch.Restart();
        List<string> repaired = PersistentIds.Repair(world);
        report.Add($"Repair ids: {repaired.Count} changed in {watch.ElapsedMilliseconds} ms.");
        report.AddRange(repaired);
        watch.Restart();
        List<string> problems = WorldValidation.ValidateWorld(world, WorldLayout.AllWorlds());
        report.Add($"Validate: {problems.Count} problems in {watch.ElapsedMilliseconds} ms.");
        report.AddRange(problems);
        return report;
    }

    /// <summary>
    /// The world's entries after a write: <paramref name="generated"/>, in order, then each of
    /// <paramref name="current"/>, in order, that is neither generated now nor named in
    /// <paramref name="previouslyGenerated"/> (the entries the last write set), so the entries made by hand stay after
    /// the generated ones and an entry the generator no longer makes goes.
    /// </summary>
    internal static List<WorldDefinition.Entry> MergeEntries(IReadOnlyList<WorldDefinition.Entry> current, IReadOnlyList<WorldDefinition.Entry> generated, IReadOnlyList<string> previouslyGenerated)
    {
        List<WorldDefinition.Entry> merged = new();
        List<string> names = new();
        foreach (WorldDefinition.Entry entry in generated)
        {
            merged.Add(new WorldDefinition.Entry(entry.Name, entry.Position, entry.Waystone));
            names.Add(entry.Name);
        }

        foreach (WorldDefinition.Entry entry in current)
        {
            if (entry != null && !names.Contains(entry.Name) && (previouslyGenerated == null || !Contains(previouslyGenerated, entry.Name)))
            {
                merged.Add(entry);
            }
        }

        return merged;
    }

    /// <summary>
    /// Why <see cref="Write"/> cannot write <paramref name="composition"/> for <paramref name="generator"/>, or null.
    /// </summary>
    private static string WriteProblem(WorldGenerator generator, WorldComposition composition, bool despiteWarnings)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return "World generation writes chunk scenes outside play mode only; a change to a scene during play is lost when play stops.";
        }

        if (generator == null || generator.World == null)
        {
            return "The world generator has no world to write.";
        }

        WorldDefinition world = generator.World;
        if (composition == null || composition.Style == null || composition.SizeInChunks != world.SizeInChunks)
        {
            return $"The composition is not one of {world.name} ({world.SizeInChunks.x} x {world.SizeInChunks.y} chunks); compose the world again.";
        }

        if (composition.BlockingWarnings.Count > 0 && !despiteWarnings)
        {
            List<string> listed = composition.BlockingWarnings.GetRange(0, Mathf.Min(ListedInError, composition.BlockingWarnings.Count));
            return $"{world.name} was not written: its composition has {composition.BlockingWarnings.Count} warnings about layer lists or reachability ({string.Join(" ", listed)}{(composition.BlockingWarnings.Count > ListedInError ? " ..." : string.Empty)}). Fix them, or write anyway.";
        }

        if (!EditorUtility.IsPersistent(world))
        {
            return $"{world.name} is not saved as an asset, so it has no chunk scenes to write.";
        }

        return null;
    }

    /// <summary>
    /// The chunks to write: <paramref name="chunks"/> once each in their order, or every chunk of
    /// <paramref name="world"/> in row order when null.
    /// </summary>
    private static List<Vector2Int> Targets(WorldDefinition world, IEnumerable<Vector2Int> chunks)
    {
        List<Vector2Int> targets = new();
        if (chunks == null)
        {
            for (int y = 0; y < world.SizeInChunks.y; y++)
            {
                for (int x = 0; x < world.SizeInChunks.x; x++)
                {
                    targets.Add(new Vector2Int(x, y));
                }
            }

            return targets;
        }

        foreach (Vector2Int coords in chunks)
        {
            if (!targets.Contains(coords))
            {
                targets.Add(coords);
            }
        }

        return targets;
    }

    /// <summary>
    /// The chunks of <paramref name="targets"/> inside the world, not locked, that the world has no scene for yet.
    /// </summary>
    private static List<Vector2Int> Missing(WorldGenerator generator, List<Vector2Int> targets)
    {
        List<Vector2Int> missing = new();
        foreach (Vector2Int coords in targets)
        {
            ChunkRecord record = generator.RecordOf(coords);
            if (Inside(generator.World, coords) && (record == null || !record.Locked) && !generator.World.HasChunk(coords))
            {
                missing.Add(coords);
            }
        }

        return missing;
    }

    /// <summary>
    /// Writes, or leaves, the chunk at <paramref name="coords"/> for <see cref="Write"/>, and reports it, leaving out
    /// the mobs of <paramref name="leftOut"/> (by id, with the chunk still holding each). A chunk whose scene could not
    /// be saved fails and is not recorded, so the next write tries it again.
    /// </summary>
    private static ChunkWrite WriteOne(WorldGenerator generator, WorldComposition composition, Vector2Int coords, bool force, Dictionary<Vector2Int, double> created, string emptyHash, Dictionary<string, Vector2Int> leftOut)
    {
        WorldDefinition world = generator.World;
        string name = WorldAuthoring.ChunkSceneName(world, coords);
        if (!Inside(world, coords))
        {
            return new ChunkWrite(coords, Outcome.OutsideTheWorld, 0, $"Chunk {coords}: outside {world.name}, which is {world.SizeInChunks.x} x {world.SizeInChunks.y} chunks; skipped.");
        }

        ChunkRecord record = generator.RecordOf(coords);
        if (record != null && record.Locked)
        {
            return new ChunkWrite(coords, Outcome.Locked, 0, $"{name}: locked; left alone.");
        }

        Stopwatch watch = Stopwatch.StartNew();
        bool isNew = created.TryGetValue(coords, out double create);
        string composed = composition.ChunkAt(coords).ContentHash;
        if (!force && !isNew && record != null && record.WrittenHash == composed)
        {
            return new ChunkWrite(coords, Outcome.Unchanged, watch.Elapsed.TotalMilliseconds, $"{name}: unchanged since its last write; skipped.");
        }

        if (!world.TryGetChunkScene(coords, out SceneDefinition definition) || !definition.IsValid)
        {
            return new ChunkWrite(coords, Outcome.Failed, 0, $"{name}: {world.name} lists no scene for it; not written.");
        }

        Scene scene = SceneManager.GetSceneByPath(definition.ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened)
        {
            scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Additive);
        }

        double open = Lap(watch);
        double check = 0;
        double write = 0;
        double save = 0;
        Outcome outcome = Outcome.Failed;
        string detail = "its scene has no grid; not written.";
        try
        {
            Grid grid = GridOf(scene);
            bool recorded = record != null && !string.IsNullOrEmpty(record.WrittenHash);
            bool changed = grid != null && !force && !isNew && ContentHash(grid, coords, composition.LayerOrder) != (recorded ? record.WrittenHash : emptyHash);
            check = Lap(watch);
            if (changed)
            {
                outcome = Outcome.ChangedByHand;
                detail = recorded
                    ? "changed by hand since its last write; not written. Lock it to keep the hand work, or write it with force."
                    : "holds ground, raised ground or generated spawn points the generator did not write; not written. Lock it to keep them, or write it with force.";
            }
            else if (grid != null)
            {
                string written = WriteChunk(grid, composition, coords, leftOut.Keys);
                write = Lap(watch);
                if (written == null)
                {
                    detail = "not written; see the console.";
                }
                else if (!EditorSceneManager.SaveScene(scene))
                {
                    save = Lap(watch);
                    detail = opened
                        ? "written, but its scene could not be saved (is the file read-only?); not recorded, so the next write tries it again."
                        : "written, but its scene could not be saved (is the file read-only?): the open scene holds the write unsaved, and it is not recorded.";
                }
                else
                {
                    save = Lap(watch);
                    generator.SetWrittenHash(coords, written, MobsOf(composition, coords, leftOut.Keys).ConvertAll(mob => mob.Id));
                    outcome = isNew ? Outcome.Created : Outcome.Written;
                    detail = Describe(grid, composition, coords, generator.RecordOf(coords), leftOut);
                }
            }
        }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        double close = Lap(watch);
        double total = create + open + check + write + save + close;
        string line = outcome switch
        {
            Outcome.Created => $"{name}: created and written in {total:0} ms (create {create:0}, open {open:0}, write {write:0}, save {save:0}, close {close:0}); {detail}",
            Outcome.Written => $"{name}: written in {total:0} ms (open {open:0}, check {check:0}, write {write:0}, save {save:0}, close {close:0}); {detail}",
            _ => $"{name}: {detail}"
        };
        return new ChunkWrite(coords, outcome, total, line);
    }

    /// <summary>
    /// What a written chunk holds, for its line: its layers (past 16 noted when it is not allowed wide), its generated
    /// spawn points, and the ones <paramref name="leftOut"/> with the chunks still holding them.
    /// </summary>
    private static string Describe(Grid grid, WorldComposition composition, Vector2Int coords, ChunkRecord record, Dictionary<string, Vector2Int> leftOut)
    {
        int layers = grid.TryGetComponent(out TerrainBlendRenderer renderer) ? renderer.Layers.Count : 0;
        int mobs = MobsOf(composition, coords, leftOut.Keys).Count;
        int props = composition.Props.FindAll(prop => prop.Chunk == coords).Count;
        string past = layers > TerrainControlMap.NarrowLayers && (record == null || !record.AllowWide) ? $", past {TerrainControlMap.NarrowLayers}: it draws in the wide variant" : string.Empty;
        if (leftOut.Count == 0)
        {
            return $"{layers} layers{past}, {mobs} generated spawn points, {props} props.";
        }

        List<string> holders = new();
        foreach (Vector2Int holder in leftOut.Values)
        {
            string name = $"chunk {holder}";
            if (!holders.Contains(name))
            {
                holders.Add(name);
            }
        }

        return $"{layers} layers{past}, {mobs} generated spawn points, {props} props; {leftOut.Count} left out, their ids still in {string.Join(", ", holders)}, which keeps its content: they are written here once it no longer holds them.";
    }

    /// <summary>
    /// The composition's mobs in the chunk at <paramref name="coords"/>, in order, but for those whose ids are in
    /// <paramref name="leaveOut"/> (none left out when null).
    /// </summary>
    private static List<WorldComposition.Mob> MobsOf(WorldComposition composition, Vector2Int coords, ICollection<string> leaveOut)
    {
        RectInt bounds = ChunkCoords.Bounds(coords);
        return composition.Mobs.FindAll(mob => bounds.Contains(mob.Cell) && (leaveOut == null || !leaveOut.Contains(mob.Id)));
    }

    /// <summary>
    /// Every generated spawn point id the generator's records say a chunk holds, with the chunks recorded holding it.
    /// </summary>
    private static Dictionary<string, List<Vector2Int>> RecordedHolders(WorldGenerator generator)
    {
        Dictionary<string, List<Vector2Int>> holders = new();
        foreach (ChunkRecord record in generator.Chunks)
        {
            if (record == null)
            {
                continue;
            }

            foreach (string id in record.MobIds)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!holders.TryGetValue(id, out List<Vector2Int> chunks))
                {
                    chunks = new List<Vector2Int>();
                    holders.Add(id, chunks);
                }

                chunks.Add(record.Coords);
            }
        }

        return holders;
    }

    /// <summary>
    /// The composed mobs of the chunk at <paramref name="coords"/> to leave out, by id, each with the chunk holding it:
    /// those another chunk's record says it holds while that chunk keeps its content through this write
    /// (<see cref="Keeps"/>), as written, the world would hold two mobs of one id. A chunk to be written later in the
    /// pass counts as giving its mobs up; <see cref="Write"/> writes again a chunk that turns out to share an id with one
    /// refused or failed after it.
    /// </summary>
    private static Dictionary<string, Vector2Int> LeftOut(WorldGenerator generator, WorldComposition composition, Vector2Int coords, Dictionary<string, List<Vector2Int>> holders, List<Vector2Int> targets, Dictionary<Vector2Int, Outcome> outcomes)
    {
        Dictionary<string, Vector2Int> leftOut = new();
        if (!Inside(generator.World, coords))
        {
            return leftOut;
        }

        foreach (WorldComposition.Mob mob in MobsOf(composition, coords, null))
        {
            if (string.IsNullOrEmpty(mob.Id) || !holders.TryGetValue(mob.Id, out List<Vector2Int> chunks))
            {
                continue;
            }

            foreach (Vector2Int holder in chunks)
            {
                if (holder != coords && Keeps(generator, holder, targets, outcomes))
                {
                    leftOut[mob.Id] = holder;
                    break;
                }
            }
        }

        return leftOut;
    }

    /// <summary>
    /// Whether the chunk at <paramref name="coords"/>, written in this pass, holds a generated spawn point id a chunk
    /// keeping its content holds too.
    /// </summary>
    private static bool HoldsKeptIds(WorldGenerator generator, Vector2Int coords, Dictionary<string, List<Vector2Int>> holders, List<Vector2Int> targets, Dictionary<Vector2Int, Outcome> outcomes)
    {
        ChunkRecord record = generator.RecordOf(coords);
        if (record == null)
        {
            return false;
        }

        foreach (string id in record.MobIds)
        {
            if (holders.TryGetValue(id, out List<Vector2Int> chunks) && chunks.Exists(holder => holder != coords && Keeps(generator, holder, targets, outcomes)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the chunk at <paramref name="coords"/> keeps what it holds through this write: it is locked, not to be
    /// written, or was refused or failed.
    /// </summary>
    private static bool Keeps(WorldGenerator generator, Vector2Int coords, List<Vector2Int> targets, Dictionary<Vector2Int, Outcome> outcomes)
    {
        ChunkRecord record = generator.RecordOf(coords);
        if ((record != null && record.Locked) || !targets.Contains(coords))
        {
            return true;
        }

        return outcomes.TryGetValue(coords, out Outcome outcome) && !IsWritten(outcome) && outcome != Outcome.Unchanged;
    }

    /// <summary>
    /// Whether <paramref name="outcome"/> wrote the chunk.
    /// </summary>
    private static bool IsWritten(Outcome outcome)
    {
        return outcome == Outcome.Written || outcome == Outcome.Created;
    }

    /// <summary>
    /// Whether the chunk at <paramref name="coords"/> holds the composition's ground once this write is done: written
    /// or unchanged in it, or, outside it, not locked and recorded as holding the composed content.
    /// </summary>
    private static bool HoldsComposedGround(WorldGenerator generator, WorldComposition composition, Vector2Int coords, Dictionary<Vector2Int, Outcome> outcomes)
    {
        if (outcomes.TryGetValue(coords, out Outcome outcome))
        {
            return IsWritten(outcome) || outcome == Outcome.Unchanged;
        }

        ChunkRecord record = generator.RecordOf(coords);
        return Inside(generator.World, coords) && record != null && !record.Locked && record.WrittenHash == composition.ChunkAt(coords).ContentHash;
    }

    /// <summary>
    /// Why <paramref name="grid"/> cannot take the chunk at <paramref name="coords"/> of <paramref name="composition"/>,
    /// or null.
    /// </summary>
    private static string ChunkProblem(Grid grid, WorldComposition composition, Vector2Int coords)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return "World generation writes chunks outside play mode only; a change to a scene during play is lost when play stops.";
        }

        if (grid == null)
        {
            return "World generation was given no grid to write into.";
        }

        if ((Vector2)grid.cellSize != Vector2.one)
        {
            return $"{grid.name} has {grid.cellSize} cells; a world's chunks have one-unit cells.";
        }

        if (composition == null || composition.Style == null || composition.ChunkAt(coords) == null)
        {
            return $"{grid.name}: the composition has no chunk {coords}.";
        }

        if (grid.TryGetComponent(out WorldChunk marker) && marker.Coords != coords)
        {
            return $"{grid.name} is chunk {marker.Coords}, not chunk {coords}.";
        }

        return null;
    }

    /// <summary>
    /// The ground layers the chunk at <paramref name="coords"/> gets on <paramref name="grid"/>, in the style's order:
    /// its list's materials and the materials of the layers its <see cref="AreaTerrain"/> lists, each on the tilemap
    /// drawing it now (or its material's own name, <see cref="GroundLayerMigration.TilemapName"/>) with its material's
    /// tile and terrain type. Null, with <paramref name="problem"/>, when a material lacks either or the layers cannot
    /// be set up (<see cref="AreaGroundSetup.Problem"/>).
    /// </summary>
    private static List<AreaGenerator.TerrainLayer> PlanLayers(Grid grid, WorldComposition composition, Vector2Int coords, out string problem)
    {
        TerrainStyle style = composition.Style;
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        List<TerrainMaterial> materials = new(composition.ChunkAt(coords).Materials);
        foreach (Tilemap listed in ListedTilemaps(grid))
        {
            TerrainMaterial material = MaterialDrawing(renderer, listed);
            if (material != null && style.LayerOrderIndexOf(material) >= 0 && !materials.Contains(material))
            {
                materials.Add(material);
            }
        }

        materials.Sort((a, b) => style.LayerOrderIndexOf(a).CompareTo(style.LayerOrderIndexOf(b)));
        List<AreaGenerator.TerrainLayer> layers = new();
        foreach (TerrainMaterial material in materials)
        {
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(TerrainTileWriter.TilePath(material));
            TerrainType2D terrainType = AssetDatabase.LoadAssetAtPath<TerrainType2D>(GroundLayerMigration.TerrainTypePath(material));
            if (tile == null || terrainType == null)
            {
                problem = $"{grid.name}: chunk {coords} lays {material.name}, which needs a tile at {TerrainTileWriter.TilePath(material)} and a terrain type at {GroundLayerMigration.TerrainTypePath(material)}.";
                return null;
            }

            Tilemap drawn = TilemapDrawing(renderer, material);
            string tilemapName = drawn != null && drawn.transform.parent == grid.transform ? drawn.name : GroundLayerMigration.TilemapName(material);
            layers.Add(new AreaGenerator.TerrainLayer(tilemapName, tile, terrainType, material));
        }

        problem = AreaGroundSetup.Problem($"{grid.name} (chunk {coords})", style, layers);
        return problem == null ? layers : null;
    }

    /// <summary>
    /// Fills <paramref name="cells"/> and <paramref name="tiles"/> with the raised ground of the chunk at
    /// <paramref name="coords"/>, in row order, each cell with the raised or ramp tile of the region owning it; returns
    /// why a cell has no tile, or null.
    /// </summary>
    private static string PlanRaised(WorldComposition composition, Vector2Int coords, List<Vector3Int> cells, List<TileBase> tiles)
    {
        RectInt bounds = ChunkCoords.Bounds(coords);
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                Vector2Int cell = new(x, y);
                byte kind = composition.RaisedAt(cell);
                if (kind == RaisedGround.Level)
                {
                    continue;
                }

                int owner = composition.RegionAt(cell);
                AreaGenerator generator = owner >= 0 ? composition.Regions[owner].Generator : null;
                TileBase tile = generator == null ? null : kind == RaisedGround.Ramp ? generator.RampTile : generator.RaisedTile;
                if (tile == null)
                {
                    return $"Chunk {coords}: the {(kind == RaisedGround.Ramp ? "ramp" : "raised")} cell {cell} has no tile: {(generator != null ? $"{generator.name} sets none" : "no region owns it")}.";
                }

                cells.Add(new Vector3Int(x, y, 0));
                tiles.Add(tile);
            }
        }

        return null;
    }

    /// <summary>
    /// Sets up <paramref name="layers"/> as <paramref name="grid"/>'s ground layers and fills each with its tile in
    /// exactly the composition's cells of the chunk at <paramref name="coords"/>: every layer the renderer draws is
    /// cleared first, so the ones <paramref name="layers"/> leave out are empty and removed, and none keeps a record of
    /// an old tile; then the <see cref="AreaTerrain"/> entries of the layers fill with their tiles. Returns the layers'
    /// tilemaps in order.
    /// </summary>
    private static Tilemap[] WriteGround(Grid grid, WorldComposition composition, Vector2Int coords, List<AreaGenerator.TerrainLayer> layers, List<string> changes)
    {
        if (grid.TryGetComponent(out TerrainBlendRenderer renderer))
        {
            foreach (TerrainBlendRenderer.Layer drawn in renderer.Layers)
            {
                if (drawn != null && drawn.Tilemap != null)
                {
                    drawn.Tilemap.ClearAllTiles();
                }
            }
        }

        Tilemap[] tilemaps = AreaGroundSetup.SetUpLayers(grid, composition.Style, layers, Owner, changes);
        RectInt bounds = ChunkCoords.Bounds(coords);
        TileBase[] fills = new TileBase[layers.Count];
        for (int i = 0; i < layers.Count; i++)
        {
            fills[i] = layers[i].Tile;
            List<Vector2Int> cells = composition.CellsOf(layers[i].Material, bounds);
            Vector3Int[] positions = new Vector3Int[cells.Count];
            for (int j = 0; j < cells.Count; j++)
            {
                positions[j] = new Vector3Int(cells[j].x, cells[j].y, 0);
            }

            TileBase[] tiles = new TileBase[cells.Count];
            Array.Fill(tiles, layers[i].Tile);
            tilemaps[i].ClearAllTiles();
            tilemaps[i].SetTiles(positions, tiles);
        }

        AreaGroundSetup.SetFillTiles(grid, tilemaps, fills, changes);
        return tilemaps;
    }

    /// <summary>
    /// Appends to <paramref name="grid"/>'s <see cref="AreaTerrain"/> the layer of each base ground material with cells
    /// in the chunk at <paramref name="coords"/> that it does not list yet, with its tile, after its other entries,
    /// which keep their places.
    /// </summary>
    private static void ListGrounds(Grid grid, WorldComposition composition, Vector2Int coords, List<AreaGenerator.TerrainLayer> layers, Tilemap[] tilemaps)
    {
        if (!grid.TryGetComponent(out AreaTerrain terrain))
        {
            return;
        }

        uint own = composition.ChunkAt(coords).OwnMask;
        SerializedObject data = new(terrain);
        SerializedProperty listed = data.FindProperty("layers");
        bool appended = false;
        foreach (TerrainMaterial ground in composition.Grounds)
        {
            int bit = composition.IndexOf(ground);
            int layer = layers.FindIndex(candidate => candidate.Material == ground);
            if (bit < 0 || (own & (1u << bit)) == 0 || layer < 0 || Lists(listed, tilemaps[layer]))
            {
                continue;
            }

            listed.arraySize++;
            SerializedProperty entry = listed.GetArrayElementAtIndex(listed.arraySize - 1);
            entry.FindPropertyRelative("tilemap").objectReferenceValue = tilemaps[layer];
            entry.FindPropertyRelative("fillTile").objectReferenceValue = layers[layer].Tile;
            appended = true;
        }

        if (appended)
        {
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Writes <paramref name="cells"/> with <paramref name="tiles"/> onto <paramref name="grid"/>'s raised tilemap, set
    /// up when missing (<see cref="AreaGroundSetup.SetUpRaised"/>) and cleared first; with no cells, removes the raised
    /// tilemap from the renderer and the navigation and destroys it, or only clears it when its
    /// <see cref="AreaTerrain"/> lists it.
    /// </summary>
    private static void WriteRaised(Grid grid, List<Vector3Int> cells, List<TileBase> tiles, List<string> changes)
    {
        if (cells.Count > 0)
        {
            Tilemap written = AreaGroundSetup.SetUpRaised(grid, changes);
            written.ClearAllTiles();
            written.SetTiles(cells.ToArray(), tiles.ToArray());
            return;
        }

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Tilemap raised = renderer != null ? renderer.RaisedTilemap : null;
        Transform child = grid.transform.Find(AreaGenerator.RaisedTilemapName);
        if (raised == null && child != null)
        {
            raised = child.GetComponent<Tilemap>();
        }

        if (raised == null)
        {
            return;
        }

        if (ListedTilemaps(grid).Contains(raised))
        {
            raised.ClearAllTiles();
            return;
        }

        ClearReference(renderer, "raisedTilemap");
        ClearReference(grid.GetComponent<NavigationGrid2D>(), "raisedTilemap");
        if (raised.transform.parent == grid.transform)
        {
            Object.DestroyImmediate(raised.gameObject);
        }
        else
        {
            raised.ClearAllTiles();
        }
    }

    /// <summary>
    /// Makes the spawn points under <paramref name="grid"/>'s <see cref="GeneratedMobSpawnsName"/> the mobs of the
    /// chunk at <paramref name="coords"/>, in the composition's order: the spawn points already there are reused in
    /// order, so an unchanged one keeps its place in the scene file, the others made, the rest destroyed, and each named
    /// after its prefab, scope and ordinal, at its cell's centre, with its prefab and its derived id set through its
    /// serialized object; the mobs whose ids are in <paramref name="leaveOut"/> are left out. The root is made when
    /// missing and destroyed when the chunk has no mobs.
    /// </summary>
    private static void WriteMobSpawns(Grid grid, WorldComposition composition, Vector2Int coords, ICollection<string> leaveOut)
    {
        List<WorldComposition.Mob> mobs = MobsOf(composition, coords, leaveOut);
        Transform root = grid.transform.Find(GeneratedMobSpawnsName);
        List<MobSpawnPoint> existing = new();
        if (root != null)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                GameObject child = root.GetChild(i).gameObject;
                if (child.TryGetComponent(out MobSpawnPoint point))
                {
                    existing.Insert(0, point);
                }
                else
                {
                    Object.DestroyImmediate(child);
                }
            }
        }

        if (mobs.Count == 0)
        {
            if (root != null)
            {
                Object.DestroyImmediate(root.gameObject);
            }

            return;
        }

        if (root == null)
        {
            root = new GameObject(GeneratedMobSpawnsName).transform;
            root.SetParent(grid.transform, false);
        }

        for (int i = existing.Count - 1; i >= mobs.Count; i--)
        {
            Object.DestroyImmediate(existing[i].gameObject);
        }

        for (int i = 0; i < mobs.Count; i++)
        {
            WorldComposition.Mob mob = mobs[i];
            GameObject marker = i < existing.Count ? existing[i].gameObject : new GameObject();
            marker.name = $"{(mob.Prefab != null ? mob.Prefab.name : "Mob")}_{mob.Scope}_{mob.Ordinal}";
            marker.transform.SetParent(root, false);
            marker.transform.position = AreaLayoutWriter.CellCentre(grid, new Vector3Int(mob.Cell.x, mob.Cell.y, 0));
            SerializedObject spawn = new(marker.TryGetComponent(out MobSpawnPoint point) ? point : marker.AddComponent<MobSpawnPoint>());
            spawn.FindProperty("mobPrefab").objectReferenceValue = mob.Prefab;
            spawn.ApplyModifiedPropertiesWithoutUndo();
            SerializedObject id = new(marker.TryGetComponent(out PersistentId persistentId) ? persistentId : marker.AddComponent<PersistentId>());
            id.FindProperty("id").stringValue = mob.Id;
            id.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Makes the prop instances under <paramref name="grid"/>'s <see cref="GeneratedPropsName"/> the props of the chunk
    /// at <paramref name="coords"/>, in the composition's order: an instance already there of the same prefab is reused
    /// in its place, so an unchanged prop keeps its place in the scene file, another is made in the chunk's scene and put
    /// there, the rest destroyed; each stands where <see cref="ChunkContent.RootOf"/> puts it on the grid. The root is
    /// made when missing and destroyed when the chunk has no props.
    /// </summary>
    private static void WriteProps(Grid grid, WorldComposition composition, Vector2Int coords)
    {
        List<WorldComposition.Prop> props = composition.Props.FindAll(prop => prop.Chunk == coords);
        Transform root = grid.transform.Find(GeneratedPropsName);
        if (props.Count == 0)
        {
            if (root != null)
            {
                Object.DestroyImmediate(root.gameObject);
            }

            return;
        }

        if (root == null)
        {
            root = new GameObject(GeneratedPropsName).transform;
            root.SetParent(grid.transform, false);
        }

        Vector3 origin = grid.CellToWorld(Vector3Int.zero);
        Scene scene = grid.gameObject.scene;
        for (int i = 0; i < props.Count; i++)
        {
            WorldComposition.Prop prop = props[i];
            GameObject instance = i < root.childCount ? root.GetChild(i).gameObject : null;
            if (instance != null && PrefabUtility.GetCorrespondingObjectFromSource(instance) != prop.Prefab)
            {
                Object.DestroyImmediate(instance);
                instance = null;
            }

            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prop.Prefab, scene);
                instance.transform.SetParent(root, false);
                instance.transform.SetSiblingIndex(i);
            }

            Vector2 local = ChunkContent.RootOf(prop);
            Vector3 position = new(origin.x + local.x * grid.cellSize.x, origin.y + local.y * grid.cellSize.y, 0f);
            if (instance.transform.position != position)
            {
                instance.transform.position = position;
            }
        }

        for (int i = root.childCount - 1; i >= props.Count; i--)
        {
            Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// Records the composition's entries first in the world's list (<see cref="MergeEntries"/>) and their names on the
    /// generator, changing neither when it already holds them. An entry whose place lies in a chunk that does not hold
    /// the composition's ground after this write (<see cref="HoldsComposedGround"/>: locked, refused, failed or not
    /// written) stays where the world has it, or waits when the world has none yet, with a warning: it would stand on
    /// ground that is not there. Returns whether the generator changed.
    /// </summary>
    private static bool SetEntries(WorldGenerator generator, WorldComposition composition, Dictionary<Vector2Int, Outcome> outcomes)
    {
        WorldDefinition world = generator.World;
        List<WorldDefinition.Entry> generated = new();
        List<string> held = new();
        foreach (WorldDefinition.Entry entry in composition.Entries)
        {
            if (HoldsComposedGround(generator, composition, ChunkCoords.ChunkOf(ChunkCoords.CellOf(entry.Position)), outcomes))
            {
                generated.Add(entry);
                continue;
            }

            held.Add(entry.Name);
            WorldDefinition.Entry current = FindEntry(world.Entries, entry.Name);
            if (current != null)
            {
                generated.Add(current);
            }
        }

        if (held.Count > 0)
        {
            Debug.LogWarning($"{world.name}: the entries {string.Join(", ", held)} stay where they were, or wait to be made, until the chunks under their composed places are written.", generator);
        }

        List<WorldDefinition.Entry> merged = MergeEntries(world.Entries, generated, generator.WrittenEntries);
        bool same = merged.Count == world.Entries.Count;
        for (int i = 0; same && i < merged.Count; i++)
        {
            WorldDefinition.Entry entry = world.Entries[i];
            same = entry != null && entry.Name == merged[i].Name && entry.Position == merged[i].Position && entry.Waystone == merged[i].Waystone;
        }

        if (!same)
        {
            world.SetEntries(merged.ToArray());
        }

        List<string> names = generated.ConvertAll(entry => entry.Name);
        bool recorded = names.Count == generator.WrittenEntries.Count;
        for (int i = 0; recorded && i < names.Count; i++)
        {
            recorded = names[i] == generator.WrittenEntries[i];
        }

        if (!recorded)
        {
            generator.SetWrittenEntries(names);
        }

        return !recorded;
    }

    /// <summary>
    /// The entry of <paramref name="entries"/> named <paramref name="entryName"/>, or null.
    /// </summary>
    private static WorldDefinition.Entry FindEntry(IReadOnlyList<WorldDefinition.Entry> entries, string entryName)
    {
        foreach (WorldDefinition.Entry entry in entries)
        {
            if (entry != null && entry.Name == entryName)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Closes every scene of <paramref name="world"/> open now that was not open before (<paramref name="openBefore"/>),
    /// making an empty untitled scene first when it is the last scene open.
    /// </summary>
    private static void CloseOpenedChunks(WorldDefinition world, List<string> openBefore)
    {
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || string.IsNullOrEmpty(scene.path) || openBefore.Contains(scene.path) || !world.OwnsScene(scene.path))
            {
                continue;
            }

            if (SceneManager.sceneCount == 1)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }

            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// The paths of the scenes open now.
    /// </summary>
    private static List<string> OpenScenePaths()
    {
        List<string> paths = new();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && !string.IsNullOrEmpty(scene.path))
            {
                paths.Add(scene.path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Whether an untitled scene with unsaved changes is open.
    /// </summary>
    private static bool HasUnsavedUntitledScene()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (string.IsNullOrEmpty(scene.path) && scene.isDirty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The grid of the chunk scene <paramref name="scene"/>: the one with its <see cref="WorldChunk"/>, else the first.
    /// </summary>
    private static Grid GridOf(Scene scene)
    {
        WorldChunk marker = SceneQuery.FindFirst<WorldChunk>(scene);
        return marker != null ? marker.GetComponent<Grid>() : SceneQuery.FindFirst<Grid>(scene);
    }

    /// <summary>
    /// The tilemaps <paramref name="grid"/>'s <see cref="AreaTerrain"/> lists, in its order; none without one.
    /// </summary>
    private static List<Tilemap> ListedTilemaps(Grid grid)
    {
        List<Tilemap> tilemaps = new();
        if (!grid.TryGetComponent(out AreaTerrain terrain))
        {
            return tilemaps;
        }

        SerializedProperty listed = new SerializedObject(terrain).FindProperty("layers");
        for (int i = 0; i < listed.arraySize; i++)
        {
            if (listed.GetArrayElementAtIndex(i).FindPropertyRelative("tilemap").objectReferenceValue is Tilemap tilemap)
            {
                tilemaps.Add(tilemap);
            }
        }

        return tilemaps;
    }

    /// <summary>
    /// Whether the <see cref="AreaTerrain"/> layers <paramref name="listed"/> hold <paramref name="tilemap"/>.
    /// </summary>
    private static bool Lists(SerializedProperty listed, Tilemap tilemap)
    {
        for (int i = 0; i < listed.arraySize; i++)
        {
            if (listed.GetArrayElementAtIndex(i).FindPropertyRelative("tilemap").objectReferenceValue == tilemap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The material <paramref name="renderer"/> draws <paramref name="tilemap"/> with, or null.
    /// </summary>
    private static TerrainMaterial MaterialDrawing(TerrainBlendRenderer renderer, Tilemap tilemap)
    {
        if (renderer == null)
        {
            return null;
        }

        foreach (TerrainBlendRenderer.Layer layer in renderer.Layers)
        {
            if (layer != null && layer.Tilemap == tilemap)
            {
                return layer.Material;
            }
        }

        return null;
    }

    /// <summary>
    /// The tilemap <paramref name="renderer"/> draws <paramref name="material"/> on, or null.
    /// </summary>
    private static Tilemap TilemapDrawing(TerrainBlendRenderer renderer, TerrainMaterial material)
    {
        if (renderer == null)
        {
            return null;
        }

        foreach (TerrainBlendRenderer.Layer layer in renderer.Layers)
        {
            if (layer != null && layer.Material == material && layer.Tilemap != null)
            {
                return layer.Tilemap;
            }
        }

        return null;
    }

    /// <summary>
    /// Clears the object reference <paramref name="property"/> of <paramref name="target"/>, when it has one, without
    /// undo.
    /// </summary>
    private static void ClearReference(Object target, string property)
    {
        if (target == null)
        {
            return;
        }

        SerializedObject data = new(target);
        SerializedProperty reference = data.FindProperty(property);
        if (reference != null && reference.objectReferenceValue != null)
        {
            reference.objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Whether the chunk at <paramref name="coords"/> lies inside <paramref name="world"/>.
    /// </summary>
    private static bool Inside(WorldDefinition world, Vector2Int coords)
    {
        return coords.x >= 0 && coords.y >= 0 && coords.x < world.SizeInChunks.x && coords.y < world.SizeInChunks.y;
    }

    /// <summary>
    /// Whether <paramref name="names"/> holds <paramref name="name"/>.
    /// </summary>
    private static bool Contains(IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The milliseconds since <paramref name="watch"/> last restarted, restarting it.
    /// </summary>
    private static double Lap(Stopwatch watch)
    {
        double elapsed = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        return elapsed;
    }
}
