using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Keeps the stress scene bootable: it should load through the real scene flow, generate its map (plain ground drawn by
/// the blended terrain, dual-grid walls over it), spawn every initial mob on reachable cells, and get mobs engaging the
/// player.
/// </summary>
public class StressTestScenePlayModeTests
{
    private const string StressAreaPath = "Assets/Dev/StressTest/Area_StressTest.unity";

    /// <summary>
    /// Booting the stress area spawns its full initial mob count, and at least one mob goes on to chase or attack the
    /// player.
    /// </summary>
    [UnityTest]
    public IEnumerator StressArea_SpawnsInitialMobsThatEngageThePlayer()
    {
        yield return SceneBootTestHelper.BootIntoArea(StressAreaPath);

        StressTestSpawner spawner = Object.FindAnyObjectByType<StressTestSpawner>();
        Assert.That(spawner, Is.Not.Null, "The stress area should contain a StressTestSpawner.");
        AssertTheGroundIsBlendedUnderTheWalls();

        yield return SceneBootTestHelper.WaitUntil(() => spawner.MobCount == spawner.InitialMobCount, "the initial mobs to spawn");
        yield return SceneBootTestHelper.WaitUntil(() => AnyMobEngaged(spawner.Mobs), "a mob to chase or attack the player");
    }

    /// <summary>
    /// Asserts that the map's ground is a plain tilemap (no dual-grid module) that the area's terrain renderer is
    /// drawing, filled at the middle, and that the walls' render tilemap draws over the renderer's chunks.
    /// </summary>
    private static void AssertTheGroundIsBlendedUnderTheWalls()
    {
        TerrainBlendRenderer renderer = Object.FindAnyObjectByType<TerrainBlendRenderer>();
        Assert.That(renderer, Is.Not.Null, "The stress area's ground is the blended terrain.");
        Assert.That(renderer.Layers.Count, Is.EqualTo(1));
        Tilemap ground = renderer.Layers[0].Tilemap;
        Assert.That(ground.GetComponent<DualGridTilemapModule>(), Is.Null, "The ground is a plain tilemap.");
        Assert.That(renderer.IsDrawing(ground), Is.True);
        Assert.That(ground.HasTile(Vector3Int.zero), Is.True, "The map painted its ground.");
        renderer.FlushPendingChanges();
        Assert.That(renderer.ControlMap.GetCell(Vector3Int.zero) & 1, Is.EqualTo(1), "The renderer follows the painted ground.");

        TilemapRenderer walls = Object.FindAnyObjectByType<NavigationGrid2D>().BlockingTilemaps[0].GetComponent<DualGridTilemapModule>().RenderTilemap.GetComponent<TilemapRenderer>();
        foreach (MeshRenderer chunk in renderer.Chunks)
        {
            Assert.That(chunk.sortingLayerID, Is.EqualTo(walls.sortingLayerID));
            Assert.That(chunk.sortingOrder, Is.LessThan(walls.sortingOrder), "The walls draw over the ground.");
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
