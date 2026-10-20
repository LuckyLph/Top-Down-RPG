using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Keeps the stress world bootable: it should load through the real scene flow, generate its map in every chunk (plain
/// ground drawn by each chunk's blended terrain, dual-grid walls over it), spawn every initial mob on reachable cells
/// once the chunks' navigation is up, and get mobs engaging the player.
/// </summary>
public class StressTestScenePlayModeTests
{
    private const string StressWorldRootPath = "Assets/Dev/Worlds/StressTest/World_StressTest.unity";

    /// <summary>
    /// Booting the stress world spawns its full initial mob count, and at least one mob goes on to chase or attack the
    /// player.
    /// </summary>
    [UnityTest]
    public IEnumerator StressWorld_SpawnsInitialMobsThatEngageThePlayer()
    {
        yield return SceneBootTestHelper.BootIntoWorld(StressWorldRootPath);

        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Assert.That(world, Is.Not.Null);
        Assert.That(world.name, Is.EqualTo("StressTest"));
        StressTestSpawner spawner = Object.FindAnyObjectByType<StressTestSpawner>();
        Assert.That(spawner, Is.Not.Null, "The stress world's root scene should contain a StressTestSpawner.");

        yield return SceneBootTestHelper.WaitUntil(() => spawner.MobCount == spawner.InitialMobCount, "the initial mobs to spawn");
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded), Is.True);
        Assert.That(loaded.Chunks.Count, Is.EqualTo(world.Chunks.Count), "The player in the middle holds every chunk.");
        foreach (KeyValuePair<Vector2Int, Scene> chunk in loaded.Chunks)
        {
            AssertTheGroundIsBlendedUnderTheWalls(chunk.Key, chunk.Value);
        }

        yield return SceneBootTestHelper.WaitUntil(() => AnyMobEngaged(spawner.Mobs), "a mob to chase or attack the player");
    }

    /// <summary>
    /// Asserts that the chunk's ground is a plain tilemap (no dual-grid module) that its terrain renderer is drawing,
    /// filled at the chunk's middle, and that the walls' render tilemap draws over the renderer's chunks.
    /// </summary>
    private static void AssertTheGroundIsBlendedUnderTheWalls(Vector2Int coords, Scene chunk)
    {
        TerrainBlendRenderer renderer = SceneQuery.FindFirst<TerrainBlendRenderer>(chunk);
        Assert.That(renderer, Is.Not.Null, $"Chunk {coords}'s ground is the blended terrain.");
        Assert.That(renderer.Layers.Count, Is.EqualTo(1));
        Tilemap ground = renderer.Layers[0].Tilemap;
        Assert.That(ground.GetComponent<DualGridTilemapModule>(), Is.Null, "The ground is a plain tilemap.");
        Assert.That(renderer.IsDrawing(ground), Is.True, $"Chunk {coords} is drawn around the player.");
        Vector3Int middle = new(coords.x * ChunkCoords.Size + ChunkCoords.Size / 2, coords.y * ChunkCoords.Size + ChunkCoords.Size / 2, 0);
        Assert.That(ground.HasTile(middle), Is.True, $"The map painted chunk {coords}'s ground.");

        TilemapRenderer walls = SceneQuery.FindFirst<NavigationGrid2D>(chunk).BlockingTilemaps[0].GetComponent<DualGridTilemapModule>().RenderTilemap.GetComponent<TilemapRenderer>();
        Assert.That(SceneQuery.FindFirst<NavigationGrid2D>(chunk).BlockingTilemaps[0].GetUsedTilesCount(), Is.GreaterThan(0), $"Chunk {coords} has walls.");
        foreach (MeshRenderer quad in renderer.Chunks)
        {
            Assert.That(quad.sortingLayerID, Is.EqualTo(walls.sortingLayerID));
            Assert.That(quad.sortingOrder, Is.LessThan(walls.sortingOrder), "The walls draw over the ground.");
        }
    }

    /// <summary>
    /// Whether any of <paramref name="mobs"/> is chasing or in attack range.
    /// </summary>
    private static bool AnyMobEngaged(IReadOnlyList<MobController> mobs)
    {
        for (int i = 0; i < mobs.Count; i++)
        {
            MobStateId state = mobs[i].CurrentStateId;
            if (state == MobStateId.Chase || state == MobStateId.AttackRange)
            {
                return true;
            }
        }

        return false;
    }
}
