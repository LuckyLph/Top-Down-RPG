using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools.Utils;

/// <summary>
/// Tests on the committed Overworld (Docs/WorldGeneration.md: Testing), its chunk scenes opened together once for the
/// fixture beside the test's scene and closed unchanged: each chunk holds what its generator asset composes and its
/// record says was written there, locked chunks aside, the world's entries are the composition's, and it is the
/// starting world at its default entry; every chunk draws 16 layers or fewer and together they lay all eighteen
/// materials; no coast map is stale and the world validates; on the open chunks' joined navigation every waystone,
/// gate and mob can be walked to from the default entry across chunk borders; and its campsites, village and waystones
/// are lit (Docs/Lighting.md, Phase 5), and its gates, its stone roads near them and its regions' spots too (Phase 6);
/// its lights cast shadows, no view holding more than eight of them (Phase 7); and each region has its own sky, the
/// wilds the day's (Phase 8).
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
    /// The Overworld's lights of Docs/Lighting.md's Phase 6: a pair of torches at each of its thirteen gates, lamp posts
    /// along its stone roads near them, braziers at either end of the Lava Fields' causeway and a beacon on the Coast's
    /// plateau, a torch at the Lakeside's dock and one at the Snowfield's trail's end.
    /// </summary>
    [Test]
    public void ItsGatesRoadsAndRegionsSpots_AreLit()
    {
        int gates = composition.Entries.FindAll(entry => !entry.Waystone).Count;
        Assert.That(gates, Is.EqualTo(13), "the gates");
        Assert.That(composition.Props.FindAll(prop => prop.Scope == "gates" && prop.Prefab.name.StartsWith("Torch")).Count, Is.EqualTo(gates * 2), "two torches at each gate");
        Assert.That(composition.Props.FindAll(prop => prop.Scope == "roads" && prop.Prefab.name.StartsWith("LampPost")).Count, Is.GreaterThanOrEqualTo(20), "lamp posts along the roads");
        Assert.That(composition.Props.FindAll(prop => prop.Scope == "lavaFields" && prop.Prefab.name.StartsWith("Brazier")).Count, Is.EqualTo(2), "the causeway's braziers");
        Assert.That(composition.Props.FindAll(prop => prop.Scope == "coast" && prop.Prefab.name.StartsWith("Brazier")).Count, Is.EqualTo(1), "the Coast's beacon");
        Assert.That(composition.Props.FindAll(prop => prop.Scope == "lakeside" && prop.Prefab.name.StartsWith("Torch")).Count, Is.EqualTo(1), "the dock's torch");
        Assert.That(composition.Props.FindAll(prop => prop.Scope == "snowfield" && prop.Prefab.name.StartsWith("Torch")).Count, Is.EqualTo(1), "the trail's torch");
    }

    /// <summary>
    /// The Overworld's region skies (Docs/Lighting.md, Phase 8), read from its chunks as the game reads them: at each
    /// waystone but the Clearing's, its region's sky, its generator's, to within a hundredth (a sky is full only 8 cells
    /// inside its region's outline, and blends between samples 4 cells apart); at the Clearing's waystone and its gates,
    /// out in the wilds, the day's own.
    /// </summary>
    [Test]
    public void EachRegion_HasItsOwnSky_AndTheWildsTheDays()
    {
        RegionSkies skies = new();
        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            skies.Add(world, chunk.Key, chunk.Value.GetComponent<ChunkSky>());
        }

        foreach (WorldDefinition.Entry entry in world.Entries)
        {
            if (!entry.Waystone && !entry.Name.StartsWith("clearing"))
            {
                continue;
            }

            RegionSkyBlend blend = skies.At(world, entry.Position);
            WorldComposition.Region region = composition.FindRegion(entry.Name);
            RegionSky sky = region != null && entry.Waystone ? region.Generator.Sky : null;
            Color tint = sky != null ? sky.Tint : Color.white;
            Assert.That(new Vector3(blend.Tint.r, blend.Tint.g, blend.Tint.b), Is.EqualTo(new Vector3(tint.r, tint.g, tint.b)).Using(new Vector3EqualityComparer(0.01f)), $"{entry.Name}'s tint");
            Assert.That(blend.CloudCover, Is.EqualTo(sky != null ? sky.CloudCover : 1f).Within(0.01f), $"{entry.Name}'s cloud cover");
        }

        Assert.That(composition.FindRegion("clearing").Generator.Sky == null, Is.True, "the Clearing keeps the day's own sky");
    }

    /// <summary>
    /// The Overworld's lights that are not spells (Docs/Lighting.md, Phase 5): a campfire at each of the four campsites,
    /// the Clearing's, the Snowfield's, the Coast's and the Lava Fields', lamp posts along the Village Forest's street
    /// and a lamp post on every waystone's plaza, within its radius of the waystone; the chunks hold a prop light for each
    /// light prop composed, and the Lava Fields' ground glows.
    /// </summary>
    [Test]
    public void ItsCampsitesVillageAndWaystones_AreLit()
    {
        HashSet<string> campsites = new();
        HashSet<string> litWaystones = new();
        int villageLamps = 0;
        int lights = 0;
        foreach (WorldComposition.Prop prop in composition.Props)
        {
            if (prop.Prefab.GetComponentInChildren<PropLight>(true) == null)
            {
                continue;
            }

            lights++;
            if (prop.Prefab.name.StartsWith("Campfire"))
            {
                campsites.Add(prop.Scope);
                continue;
            }

            Vector2 at = prop.Cell + new Vector2(0.5f, 0.5f);
            bool onPlaza = false;
            foreach (WorldDefinition.Entry entry in composition.Entries)
            {
                if (entry.Waystone && Vector2.Distance(at, entry.Position) <= generator.PlazaRadius)
                {
                    litWaystones.Add(entry.Name);
                    onPlaza = true;
                }
            }

            villageLamps += !onPlaza && prop.Scope == "villageForest" ? 1 : 0;
        }

        Assert.That(campsites, Is.EquivalentTo(new[] { "clearing", "snowfield", "coast", "lavaFields" }), "the campsites with a campfire");
        Assert.That(litWaystones, Is.EquivalentTo(composition.Entries.FindAll(entry => entry.Waystone).ConvertAll(entry => entry.Name)), "the waystones with a lamp post");
        Assert.That(villageLamps, Is.GreaterThanOrEqualTo(3), "the lamp posts along the village street");
        int inScenes = 0;
        bool glows = false;
        foreach (Scene scene in opened)
        {
            inScenes += SceneQuery.FindAll<PropLight>(scene).Length;
        }

        foreach (Grid grid in grids.Values)
        {
            glows |= grid.GetComponent<TerrainBlendRenderer>().GlowLight != null;
        }

        Assert.That(inScenes, Is.EqualTo(lights), "the chunks hold a light for each light prop");
        Assert.That(glows, Is.True, "the Lava Fields' ground glows");
    }

    /// <summary>
    /// Every light prop of the Overworld casts shadows (Docs/Lighting.md, Phase 7), and no view of 20 x 11.25 units,
    /// 1920 x 1080 screen pixels at 3 to an art pixel, holds more than the budget of 8 casting lights.
    /// </summary>
    [Test]
    public void ItsLightsCast_AndNoViewHoldsMoreThanEightOfThem()
    {
        const int budget = 8;
        Vector2 view = new(20f, 11.25f);
        List<Vector2> casting = new();
        foreach (WorldComposition.Prop prop in composition.Props)
        {
            PropLight light = prop.Prefab.GetComponentInChildren<PropLight>(true);
            if (light != null)
            {
                Assert.That(light.LightSource.shadowsEnabled, Is.True, $"{prop.Prefab.name} casts.");
                casting.Add(prop.Cell + new Vector2(0.5f, 0.5f));
            }
        }

        int most = 0;
        foreach (Vector2 left in casting)
        {
            foreach (Vector2 bottom in casting)
            {
                int inView = casting.FindAll(at => at.x >= left.x && at.x <= left.x + view.x && at.y >= bottom.y && at.y <= bottom.y + view.y).Count;
                most = Mathf.Max(most, inView);
            }
        }

        Assert.That(most, Is.InRange(1, budget), "the most casting lights a view holds");
    }

    /// <summary>
    /// No chunk's coast map or glow map (Docs/LightingGaps.md, Phase 5) is out of date against the world's ground, and
    /// the world passes validation.
    /// </summary>
    [Test]
    public void NoCoastMapIsStale_AndTheWorldValidates()
    {
        Assert.That(WorldAuthoring.StaleCoastMaps(world), Is.Empty, "stale coast maps");
        Assert.That(WorldAuthoring.StaleGlowMaps(world), Is.Empty, "stale glow maps");
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
