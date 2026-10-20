using NUnit.Framework;
using UnityEditor;

/// <summary>
/// Tests for the rule that a mob's leash keeps its home chunk loaded while it is near a player
/// (<see cref="WorldStreamingSettings.KeepsHomeChunkLoaded"/>, Docs/WorldStreaming.md: Leash), over the project's mob
/// configs and streaming settings.
/// </summary>
public class MobLeashConstraintTests
{
    private const string SettingsPath = "Assets/Data/WorldStreamingSettings.asset";

    /// <summary>
    /// Every mob config in the project keeps leash plus detection radius under the load extents and leash plus lose
    /// distance under the keep extents of the project's streaming settings.
    /// </summary>
    [Test]
    public void EveryMobConfig_KeepsItsHomeChunkLoaded()
    {
        WorldStreamingSettings settings = AssetDatabase.LoadAssetAtPath<WorldStreamingSettings>(SettingsPath);
        Assert.That(settings, Is.Not.Null);
        string[] guids = AssetDatabase.FindAssets("t:" + nameof(MobConfig), new[] { "Assets" });
        Assert.That(guids, Is.Not.Empty);

        foreach (string guid in guids)
        {
            MobConfig config = AssetDatabase.LoadAssetAtPath<MobConfig>(AssetDatabase.GUIDToAssetPath(guid));
            Assert.That(settings.KeepsHomeChunkLoaded(config, out string problem), Is.True, problem);
        }
    }

    /// <summary>
    /// The check refuses a leash that, with the detection radius, reaches the load extents' smaller side (18 cells with
    /// the project's settings), or that, with the lose distance, reaches the keep extents' smaller side (26).
    /// </summary>
    [Test]
    public void ALeashReachingTheExtents_IsRefused()
    {
        WorldStreamingSettings settings = AssetDatabase.LoadAssetAtPath<WorldStreamingSettings>(SettingsPath);
        MobConfig fits = MobConfig.Create(detectionRadius: 6f, loseTargetDistance: 8f, leashCells: 11);
        MobConfig tooFarToDetect = MobConfig.Create(detectionRadius: 6f, loseTargetDistance: 8f, leashCells: 12);
        MobConfig tooFarToChase = MobConfig.Create(detectionRadius: 2f, loseTargetDistance: 16f, leashCells: 10);

        try
        {
            Assert.That(settings.KeepsHomeChunkLoaded(fits, out _), Is.True);
            Assert.That(settings.KeepsHomeChunkLoaded(tooFarToDetect, out string detectProblem), Is.False);
            Assert.That(detectProblem, Does.Contain("detection radius"));
            Assert.That(settings.KeepsHomeChunkLoaded(tooFarToChase, out string chaseProblem), Is.False);
            Assert.That(chaseProblem, Does.Contain("lose distance"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fits);
            UnityEngine.Object.DestroyImmediate(tooFarToDetect);
            UnityEngine.Object.DestroyImmediate(tooFarToChase);
        }
    }
}
