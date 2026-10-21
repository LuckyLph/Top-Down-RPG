using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tests on the committed Overworld (Docs/WorldGeneration.md: Testing), its chunk scenes opened together once for the
/// fixture beside the test's scene and closed unchanged: each chunk holds what its generator asset composes and its
/// record says was written there, locked chunks aside, the world's entries are the composition's, and it is the
/// starting world at its default entry; every chunk draws 16 layers or fewer and together they lay all eighteen
/// materials; no coast map is stale and the world validates; and on the open chunks' joined navigation every waystone,
/// gate and mob can be walked to from the default entry across chunk borders.
/// </summary>
public class OverworldTests
{
    private const string WorldPath = "Assets/Worlds/Overworld/Overworld.asset";
    private const string GeneratorPath = "Assets/Settings/Generation/WorldGenerator_Overworld.asset";

    private readonly List<Scene> opened = new();
    private WorldDefinition world;
    private WorldGenerator generator;
    private WorldComposition composition;
    private Dictionary<Vector2Int, Grid> grids;

    /// <summary>
    /// Loads the Overworld and its generator, composes it and opens every chunk scene.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath);
        generator = AssetDatabase.LoadAssetAtPath<WorldGenerator>(GeneratorPath);
        Assert.That(world != null && generator != null, Is.True, "the Overworld and its generator asset");
        Assert.That(generator.World == world, Is.True, "the generator writes the Overworld");
        composition = generator.Compose();
        grids = new Dictionary<Vector2Int, Grid>();
        foreach (KeyValuePair<Vector2Int, Scene> chunk in WorldAuthoring.OpenChunkScenes(world, opened))
        {
            grids[chunk.Key] = SceneQuery.FindFirst<WorldChunk>(chunk.Value).GetComponent<Grid>();
        }
    }

    /// <summary>
    /// Closes the chunk scenes the fixture opened, unchanged, and lets the edit-mode navigation follow.
    /// </summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        WorldAuthoring.CloseScenes(opened, false);
        EditModeWorldNavigation.Sync();
    }

    /// <summary>
    /// The generator asset composes with no warnings; every chunk the world has, locked ones aside, is recorded with its
    /// composed hash and its scene hashes to it, so the asset and the scenes have not drifted apart; the world's entries
    /// start with the composition's, waystones first, which the generator records as the ones it set; and the world is
    /// listed in Game Scenes as the starting world, at <c>clearing</c>, its default entry.
    /// </summary>
    [Test]
    public void TheChunks_HoldWhatTheGeneratorAssetComposes_AndTheEntriesAreItsEntries()
    {
        Assert.That(composition.Warnings, Is.Empty, "the generator asset composes with no warnings");
        Assert.That(grids.Count, Is.EqualTo(world.SizeInChunks.x * world.SizeInChunks.y), "every chunk has a scene");
        foreach (WorldComposition.Chunk chunk in composition.Chunks)
        {
            ChunkRecord record = generator.RecordOf(chunk.Coords);
            if (record != null && record.Locked)
            {
                continue;
            }

            Assert.That(record != null ? record.WrittenHash : null, Is.EqualTo(chunk.ContentHash), $"chunk {chunk.Coords}'s record");
            Assert.That(WorldGenerationWriter.ContentHash(grids[chunk.Coords], chunk.Coords, composition.LayerOrder), Is.EqualTo(chunk.ContentHash), $"chunk {chunk.Coords}'s scene");
        }

        Assert.That(world.Entries.Count, Is.GreaterThanOrEqualTo(composition.Entries.Count), "the world has the generated entries");
        for (int i = 0; i < composition.Entries.Count; i++)
        {
            WorldDefinition.Entry entry = world.Entries[i];
            WorldDefinition.Entry composed = composition.Entries[i];
            Assert.That((entry.Name, entry.Position, entry.Waystone), Is.EqualTo((composed.Name, composed.Position, composed.Waystone)), $"entry {i}");
        }

        Assert.That(world.DefaultEntry.Name, Is.EqualTo("clearing"), "the Clearing's waystone is the default entry");
        Assert.That(generator.WrittenEntries, Is.EqualTo(composition.Entries.ConvertAll(entry => entry.Name)), "the generator records the entries it set");
        GameScenes gameScenes = ProjectScenes.LoadGameScenes();
        Assert.That(gameScenes.Worlds, Has.Member(world), "Game Scenes lists the Overworld");
        Assert.That(gameScenes.StartingWorld == world, Is.True, "the Overworld is the starting world");
        Assert.That(world.FindEntry(gameScenes.StartingWorldEntry), Is.SameAs(world.DefaultEntry), "games start at its default entry, the Clearing's waystone");
        Assert.That(gameScenes.StartingWorldEntry, Is.EqualTo("clearing"));
    }

    /// <summary>
    /// Every chunk's renderer draws 16 layers or fewer (more only where its record allows it wide), and the chunks
    /// together lay every one of the style's eighteen materials.
    /// </summary>
    [Test]
    public void EveryChunk_DrawsSixteenLayersOrFewer_AndTheChunksLayAllEighteenMaterials()
    {
        HashSet<TerrainMaterial> laid = new();
        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            TerrainBlendRenderer renderer = chunk.Value.GetComponent<TerrainBlendRenderer>();
            ChunkRecord record = generator.RecordOf(chunk.Key);
            if (record == null || !record.AllowWide)
            {
                Assert.That(renderer.Layers.Count, Is.LessThanOrEqualTo(TerrainControlMap.NarrowLayers), $"chunk {chunk.Key}'s layers");
            }

            foreach (TerrainBlendRenderer.Layer layer in renderer.Layers)
            {
                if (layer.Tilemap != null && layer.Tilemap.GetUsedTilesCount() > 0)
                {
                    laid.Add(layer.Material);
                }
            }
        }

        Assert.That(composition.Style.LayerOrder.Count, Is.EqualTo(18), "the style's materials");
        Assert.That(laid, Is.EquivalentTo(composition.Style.LayerOrder), "the materials the chunks lay");
    }

    /// <summary>
    /// No chunk's coast map is out of date against the world's ground, and the world passes validation.
    /// </summary>
    [Test]
    public void NoCoastMapIsStale_AndTheWorldValidates()
    {
        Assert.That(WorldAuthoring.StaleCoastMaps(world), Is.Empty, "stale coast maps");
        Assert.That(WorldValidation.ValidateWorld(world, WorldLayout.AllWorlds()), Is.Empty, "validation problems");
    }

    /// <summary>
    /// With every chunk open, each joins the world's edit-mode navigation, and on it, by the generator's movement
    /// profile, every entry (the waystones and the gates) and every mob spawn point is connected to the default entry,
    /// across chunk borders; the chunks hold the composition's 48 spawn points.
    /// </summary>
    [Test]
    public void EveryWaystoneGateAndMob_IsConnectedToTheDefaultEntry_AcrossChunkBorders()
    {
        EditModeWorldNavigation.Sync();
        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            Assert.That(EditModeWorldNavigation.IsJoined(chunk.Value.GetComponent<NavigationGrid2D>()), Is.True, $"chunk {chunk.Key} joins the navigation");
        }

        WorldNavigation navigation = EditModeWorldNavigation.NavigationOf(world);
        TerrainMovementProfile2D profile = generator.MovementProfile;
        Vector3Int start = navigation.WorldToCell(world.ToWorld(world.DefaultEntry.Position));
        Assert.That(navigation.IsCellWalkable(start, profile), Is.True, "the default entry stands on walkable ground");
        foreach (WorldDefinition.Entry entry in world.Entries)
        {
            Vector3Int cell = navigation.WorldToCell(world.ToWorld(entry.Position));
            Assert.That(navigation.AreCellsConnected(start, cell, profile), Is.True, $"entry {entry.Name} at {entry.Position}");
        }

        int points = 0;
        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            foreach (MobSpawnPoint point in SceneQuery.FindAll<MobSpawnPoint>(chunk.Value.gameObject.scene))
            {
                points++;
                Vector3Int cell = navigation.WorldToCell(point.transform.position);
                Assert.That(navigation.AreCellsConnected(start, cell, profile), Is.True, $"{point.name} of chunk {chunk.Key}");
            }
        }

        Assert.That(points, Is.EqualTo(composition.Mobs.Count).And.EqualTo(48), "the spawn points");
    }
}
