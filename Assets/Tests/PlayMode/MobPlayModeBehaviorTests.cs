using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MobPlayModeBehaviorTests
{
    [UnityTest]
    public IEnumerator MobInSampleScene_DetectsAndLosesTarget()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        yield return null;

        MobController brain = Object.FindFirstObjectByType<MobController>();
        Assert.That(brain, Is.Not.Null, "SampleScene should contain at least one MobBrain.");

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Assert.That(player, Is.Not.Null, "SampleScene should contain a Player tag target.");

        Vector3 originalPlayerPosition = player.transform.position;
        Vector3 mobPosition = brain.transform.position;

        player.transform.position = mobPosition + Vector3.right * 2f;
        yield return WaitFrames(6);
        Assert.That(
            brain.CurrentStateId == MobStateId.Chase || brain.CurrentStateId == MobStateId.AttackRange,
            Is.True,
            $"Expected chase or attack state, got {brain.CurrentStateId}.");

        player.transform.position = mobPosition + Vector3.right * 30f;
        yield return WaitFrames(10);
        Assert.That(
            brain.CurrentStateId == MobStateId.Return || brain.CurrentStateId == MobStateId.Idle || brain.CurrentStateId == MobStateId.Patrol,
            Is.True,
            $"Expected return/idle/patrol after losing target, got {brain.CurrentStateId}.");

        player.transform.position = originalPlayerPosition;
    }

    [UnityTest]
    public IEnumerator MultipleMobs_ShareSameNavigationGrid()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        yield return null;

        NavigationGrid2D navGrid = Object.FindFirstObjectByType<NavigationGrid2D>();
        MobController firstMob = Object.FindFirstObjectByType<MobController>();

        Assert.That(navGrid, Is.Not.Null, "SampleScene should contain a NavigationGrid2D.");
        Assert.That(firstMob, Is.Not.Null, "SampleScene should contain at least one MobBrain.");
        Assert.That(firstMob.NavigationGrid, Is.SameAs(navGrid));

        GameObject clone = Object.Instantiate(firstMob.gameObject, firstMob.transform.position + Vector3.right * 2f, Quaternion.identity);
        yield return null;

        MobController secondMob = clone.GetComponent<MobController>();
        Assert.That(secondMob, Is.Not.Null);
        Assert.That(secondMob.NavigationGrid, Is.SameAs(navGrid));

        Object.Destroy(clone);
    }

    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
        {
            yield return null;
        }
    }
}
