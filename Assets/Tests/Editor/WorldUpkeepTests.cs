using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the upkeep of a world's chunk scenes in the editor (Docs/KnownGaps.md, Phase 5), on a world of 2 x 1 chunks
/// created on disk in a temporary folder under <c>Assets/Dev</c> and deleted after, both chunks grass with a strip of
/// ocean by their border: a coast map out of date is a validation problem that stops a build, until the maps are baked;
/// Sync layer lists adds the layer a neighbour's border shows and removes an empty one nothing shows any more, with its
/// runtime edits' entry; a chunk scene saved by a script gives its spawn points ids and its ids no duplicate; the
/// maps baked with the chunks closed are imported together and saved with their chunks (Docs/WorldGeneration.md, Phase
/// 6); and the chunks opened in the editor draw with one shared set of texture arrays.
/// </summary>
public class WorldUpkeepTests
{
    private const string Folder = "Assets/Dev/WorldUpkeepTests";
    private const string WorldName = "UpkeepTest";

    private WorldDefinition world;
    private TerrainMaterial grass;
    private TerrainMaterial ocean;

    /// <summary>
    /// Creates the world with both its chunks open, grass on every cell and ocean on the strip x 56 to 63, y 0 to 15 of
    /// chunk (0, 0), within its neighbour's skirt, saved.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Assert.That(AssetDatabase.IsValidFolder(Folder), Is.False, $"{Folder} is left over from an earlier run");
        Assert.That(WorldLayout.TryFindFreeOrigin(new Vector2Int(2, 1), WorldLayout.AllWorlds(), out Vector2Int origin), Is.True, "a free slot");
        world = WorldAuthoring.CreateWorld(WorldName, origin, new Vector2Int(2, 1), Folder);
        WorldAuthoring.CreateChunk(world, Vector2Int.zero);
        WorldAuthoring.CreateChunk(world, Vector2Int.right);
        WorldAuthoring.OpenChunk(world, Vector2Int.zero);
        grass = WorldAuthoring.MaterialNamed("Grass");
        ocean = WorldAuthoring.MaterialNamed("Ocean");
        List<Vector2Int> land = new();
        foreach (Vector2Int cell in new RectInt(0, 0, 128, 64).allPositionsWithin)
        {
            land.Add(cell);
        }

        WorldAuthoring.Paint(world, grass, land);
        WorldAuthoring.Paint(world, ocean, Cells(new RectInt(56, 0, 8, 16)));
        EditorSceneManager.SaveOpenScenes();
    }

    /// <summary>
    /// Closes the world's scenes and deletes its folder.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (AssetDatabase.IsValidFolder(Folder))
        {
            AssetDatabase.DeleteAsset(Folder);
        }
    }

    /// <summary>
    /// With ocean painted and no map baked, both chunks' coast maps are out of date: validation names them and the build
    /// stops (<see cref="WorldBuildValidator"/>); once the changed maps are baked, nothing is out of date.
    /// </summary>
    [Test]
    public void AStaleMap_StopsABuild_UntilTheMapsAreBaked()
    {
        List<string> problems = WorldValidation.ValidateWorld(world, WorldLayout.AllWorlds());
        Assert.That(problems, Has.Some.Contains(WorldAuthoring.ChunkSceneName(world, Vector2Int.zero) + ": its coast map is out of date"), string.Join("\n", problems));
        Assert.That(problems, Has.Some.Contains(WorldAuthoring.ChunkSceneName(world, Vector2Int.right) + ": its coast map is out of date"));

        BuildFailedException refused = Assert.Throws<BuildFailedException>(() => new WorldBuildValidator().OnPreprocessBuild(null));
        Assert.That(refused.Message, Does.Contain(WorldAuthoring.ChunkSceneName(world, Vector2Int.zero) + ": its coast map is out of date"));

        WorldAuthoring.BakeCoastMaps(world, true);
        WorldAuthoring.BakeGlowMaps(world, true);
        Assert.That(WorldValidation.ValidateWorld(world, WorldLayout.AllWorlds()), Has.None.Contains(": its coast").And.None.Contains(": its glow"), "the baked maps are up to date");
        Assert.That(WorldAuthoring.MarkedStale(world), Is.Empty);
    }

    /// <summary>
    /// Sync layer lists gives chunk (1, 0) the ocean layer its neighbour's border strip shows; once that ocean is gone,
    /// the empty ocean layers of both chunks are removed with their runtime edits' entries, the grass layers staying.
    /// </summary>
    [Test]
    public void SyncLayerLists_AddsTheLayersABorderShows_AndRemovesTheEmptyOnesNothingShows()
    {
        Grid east = ChunkGrid(Vector2Int.right);
        Tilemap eastOcean = LayerOf(east, ocean);
        Assert.That(eastOcean != null, Is.True, "painting by the border gave chunk (1, 0) the ocean layer");
        SerializedObject listing = new(east.GetComponent<AreaTerrain>());
        SerializedProperty entries = listing.FindProperty("layers");
        entries.arraySize++;
        entries.GetArrayElementAtIndex(entries.arraySize - 1).FindPropertyRelative("tilemap").objectReferenceValue = eastOcean;
        listing.ApplyModifiedPropertiesWithoutUndo();

        Grid west = ChunkGrid(Vector2Int.zero);
        LayerOf(west, ocean).ClearAllTiles();
        EditorSceneManager.MarkSceneDirty(west.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();

        List<string> lines = WorldAuthoring.SyncLayerLists(world);
        Assert.That(lines, Has.Some.Contains("removed Ocean"), string.Join("\n", lines));
        west = ChunkGrid(Vector2Int.zero);
        east = ChunkGrid(Vector2Int.right);
        Assert.That(LayerOf(west, ocean) == null && LayerOf(east, ocean) == null, Is.True, "the empty ocean layers are removed");
        Assert.That(LayerOf(west, grass) != null && LayerOf(east, grass) != null, Is.True, "the grass layers stay");
        SerializedProperty listed = new SerializedObject(east.GetComponent<AreaTerrain>()).FindProperty("layers");
        for (int i = 0; i < listed.arraySize; i++)
        {
            Assert.That(listed.GetArrayElementAtIndex(i).FindPropertyRelative("tilemap").objectReferenceValue != null, Is.True, $"entry {i} still names a tilemap");
        }
    }

    /// <summary>
    /// Saving a chunk scene changed by a script that records no undo (a spawn point added without an id, an id copied
    /// onto another object) repairs its ids as it saves: the spawn point gets one and the later of the two holders a new
    /// one, the first keeping its own.
    /// </summary>
    [Test]
    public void SavingAChunkScene_RepairsItsIds()
    {
        Grid grid = ChunkGrid(Vector2Int.zero);
        Scene scene = grid.gameObject.scene;
        SceneManager.SetActiveScene(scene);
        GameObject point = new("SpawnWithoutId");
        point.transform.SetParent(grid.transform, false);
        point.transform.position = grid.transform.position + new Vector3(5.5f, 5.5f, 0f);
        point.AddComponent<MobSpawnPoint>();
        PersistentId first = new GameObject("First").AddComponent<PersistentId>();
        first.transform.SetParent(grid.transform, false);
        PersistentId second = new GameObject("Second").AddComponent<PersistentId>();
        second.transform.SetParent(grid.transform, false);
        PersistentIds.SetId(first, "shared-id");
        PersistentIds.SetId(second, "shared-id");

        Assert.That(EditorSceneManager.SaveScene(scene), Is.True);

        Assert.That(point.TryGetComponent(out PersistentId added) && added.HasId, Is.True, "the spawn point got an id as the scene saved");
        Assert.That(first.Id, Is.EqualTo("shared-id"), "the first holder keeps its id");
        Assert.That(second.HasId && second.Id != "shared-id", Is.True, "the later holder got a new one");
        Assert.That(WorldValidation.ValidateIds(SceneQuery.FindAll<PersistentId>(scene)), Is.Empty);
    }

    /// <summary>
    /// With lava painted in chunk (1, 0) and both chunks closed, Bake changed maps opens them, bakes a coast map for
    /// each (the ocean strip lies within the reach of both) and a glow map for the lava's chunk, imports the maps of each
    /// bake together and saves the chunks together (Docs/WorldGeneration.md, Phase 6), closing the chunks again: read back
    /// from disk, each chunk records its maps, the maps have their import settings, and no map is out of date.
    /// </summary>
    [Test]
    public void BakingTheMaps_WithTheChunksClosed_SavesEachChunkWithItsMaps()
    {
        WorldAuthoring.Paint(world, WorldAuthoring.MaterialNamed("Lava"), Cells(new RectInt(96, 24, 8, 8)));
        EditorSceneManager.SaveOpenScenes();
        WorldAuthoring.CloseChunks(world, new List<Vector2Int>());
        Assert.That(WorldAuthoring.OpenChunkCoords(world), Is.Empty, "the chunks are closed");

        List<string> lines = WorldAuthoring.BakeCoastMaps(world, true);
        lines.AddRange(WorldAuthoring.BakeGlowMaps(world, true));

        Assert.That(WorldAuthoring.OpenChunkCoords(world), Is.Empty, "the bakes close the chunks they opened");
        Assert.That(lines, Has.Some.StartsWith("Imported 2 maps, deleted 0 and saved 2 chunks"), string.Join("\n", lines));
        Assert.That(lines, Has.Some.StartsWith("Imported 1 maps, deleted 0 and saved 1 chunks"), string.Join("\n", lines));
        foreach (Vector2Int coords in new[] { Vector2Int.zero, Vector2Int.right })
        {
            string coastSource = ReadChunk(coords, chunk => chunk.CoastMap != null ? chunk.CoastSource : null);
            Assert.That(coastSource, Is.Not.Null.And.Not.Empty.And.Not.EqualTo(WorldAuthoring.StaleSource), $"chunk {coords} saved with its coast map");
            TextureImporter coast = (TextureImporter)AssetImporter.GetAtPath(WorldAuthoring.CoastMapPath(world, coords));
            Assert.That(coast.GetDefaultPlatformTextureSettings().format, Is.EqualTo(TextureImporterFormat.R16), $"chunk {coords}'s coast map is R16");
            Assert.That(coast.textureType == TextureImporterType.Default && !coast.sRGBTexture && !coast.isReadable, Is.True, $"chunk {coords}'s coast map is a linear texture, not readable");
        }

        Assert.That(ReadChunk(Vector2Int.right, chunk => chunk.GlowMap != null && chunk.GlowArea.width > 0 && !string.IsNullOrEmpty(chunk.GlowSource)), Is.True, "the lava's chunk saved with its glow map");
        Assert.That(ReadChunk(Vector2Int.zero, chunk => chunk.GlowMap == null), Is.True, "nothing glows within reach of chunk (0, 0)");
        TextureImporter glow = (TextureImporter)AssetImporter.GetAtPath(WorldAuthoring.GlowMapPath(world, Vector2Int.right));
        Assert.That(glow.GetDefaultPlatformTextureSettings().format, Is.EqualTo(TextureImporterFormat.RGBA32), "the glow map is RGBA32");
        Assert.That(WorldValidation.ValidateWorld(world, WorldLayout.AllWorlds()), Has.None.Contains(": its coast").And.None.Contains(": its glow"), "no map is out of date");
    }

    /// <summary>
    /// The two chunks opened in the editor draw with one set of shared texture arrays (<see cref="EditModeTerrainArrays"/>),
    /// not each its own.
    /// </summary>
    [Test]
    public void ChunksOpenedInTheEditor_ShareTheirTextureArrays()
    {
        TerrainBlendRenderer west = ChunkGrid(Vector2Int.zero).GetComponent<TerrainBlendRenderer>();
        TerrainBlendRenderer east = ChunkGrid(Vector2Int.right).GetComponent<TerrainBlendRenderer>();
        Assert.That(west.SharesArrays && east.SharesArrays, Is.True, "both draw with shared arrays");
        Assert.That(west.AlbedoArray == east.AlbedoArray, Is.True, "the same arrays");
        Assert.That(EditModeTerrainArrays.Arrays.AliveCount, Is.EqualTo(1), "one set for the style");
    }

    /// <summary>
    /// The grid of the open chunk at <paramref name="coords"/>.
    /// </summary>
    private Grid ChunkGrid(Vector2Int coords)
    {
        Grid grid = WorldAuthoring.OpenChunkGrid(world, coords);
        Assert.That(grid != null, Is.True, $"chunk {coords} is open");
        return grid;
    }

    /// <summary>
    /// The layer drawing <paramref name="material"/> on <paramref name="grid"/>'s renderer, or null.
    /// </summary>
    private static Tilemap LayerOf(Grid grid, TerrainMaterial material)
    {
        foreach (TerrainBlendRenderer.Layer layer in grid.GetComponent<TerrainBlendRenderer>().Layers)
        {
            if (layer != null && layer.Material == material)
            {
                return layer.Tilemap;
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="read"/> of the chunk marker of the chunk at <paramref name="coords"/>, opened from disk and closed
    /// again.
    /// </summary>
    private T ReadChunk<T>(Vector2Int coords, System.Func<WorldChunk, T> read)
    {
        Assert.That(world.TryGetChunkScene(coords, out SceneDefinition definition), Is.True, $"chunk {coords}");
        Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Additive);
        try
        {
            return read(SceneQuery.FindFirst<WorldChunk>(scene));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// The cells of <paramref name="rect"/>.
    /// </summary>
    private static List<Vector2Int> Cells(RectInt rect)
    {
        List<Vector2Int> cells = new();
        foreach (Vector2Int cell in rect.allPositionsWithin)
        {
            cells.Add(cell);
        }

        return cells;
    }
}
