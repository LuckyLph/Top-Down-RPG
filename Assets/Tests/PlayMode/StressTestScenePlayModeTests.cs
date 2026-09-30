using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Keeps the stress scene bootable: it should load through the real scene flow, generate its map,
// spawn every initial mob on reachable cells, and get mobs engaging the player.
public class StressTestScenePlayModeTests
{
    private const string StressAreaPath = "Assets/Dev/StressTest/Area_StressTest.unity";

    [UnityTest]
    public IEnumerator StressArea_SpawnsInitialMobsThatEngageThePlayer()
    {
        yield return SceneBootTestHelper.BootIntoArea(StressAreaPath);

        StressTestSpawner spawner = Object.FindAnyObjectByType<StressTestSpawner>();
        Assert.That(spawner, Is.Not.Null, "The stress area should contain a StressTestSpawner.");

        yield return SceneBootTestHelper.WaitUntil(() => spawner.MobCount == spawner.InitialMobCount, "the initial mobs to spawn");
        yield return SceneBootTestHelper.WaitUntil(() => AnyMobEngaged(spawner.Mobs), "a mob to chase or attack the player");
    }

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
