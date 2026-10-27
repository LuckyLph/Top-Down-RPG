using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// The streaming benchmark shared by the PlayMode benchmark and a player run (Docs/WorldScale.md, Phase 0): its
/// statistics on made-up frame times, and its route, every stop of which is an entry of the Overworld, starting at the
/// Clearing, where a new game starts, and passing through every region.
/// </summary>
public class StreamingBenchmarkTests
{
    private const string OverworldPath = "Assets/Worlds/Overworld/Overworld.asset";
    private static readonly string[] Regions = { "clearing", "villageForest", "lakeside", "snowfield", "coast", "lavaFields" };

    /// <summary>
    /// The mean, the largest and the percentiles of a few frame times are theirs, the percentile being the smallest
    /// time at least that share of the frames reach.
    /// </summary>
    [Test]
    public void TheStatistics_OfAFewFrameTimes_AreTheirMeanLargestAndPercentiles()
    {
        List<double> frames = new() { 4, 2, 10, 6, 8 };

        Assert.That(StreamingBenchmark.Mean(frames), Is.EqualTo(6).Within(1e-9));
        Assert.That(StreamingBenchmark.Max(frames), Is.EqualTo(10));
        Assert.That(StreamingBenchmark.Percentile(frames, 0f), Is.EqualTo(2));
        Assert.That(StreamingBenchmark.Percentile(frames, 0.5f), Is.EqualTo(6));
        Assert.That(StreamingBenchmark.Percentile(frames, 0.99f), Is.EqualTo(10));
        Assert.That(frames, Is.EqualTo(new List<double> { 4, 2, 10, 6, 8 }), "the times are left in their order");
    }

    /// <summary>
    /// With no frames every statistic is 0.
    /// </summary>
    [Test]
    public void TheStatistics_OfNoFrames_AreZero()
    {
        List<double> none = new();

        Assert.That(StreamingBenchmark.Mean(none), Is.EqualTo(0));
        Assert.That(StreamingBenchmark.Max(none), Is.EqualTo(0));
        Assert.That(StreamingBenchmark.Percentile(none, 0.99f), Is.EqualTo(0));
    }

    /// <summary>
    /// The route starts at the Clearing and stops only at the Overworld's entries, every region's among them.
    /// </summary>
    [Test]
    public void TheRoute_StartsAtTheClearing_AndPassesThroughEveryRegionOfTheOverworld()
    {
        WorldDefinition overworld = AssetDatabase.LoadAssetAtPath<WorldDefinition>(OverworldPath);
        Assert.That(overworld != null, Is.True, $"a world at {OverworldPath}");
        HashSet<string> entries = new();
        foreach (WorldDefinition.Entry entry in overworld.Entries)
        {
            entries.Add(entry.Name);
        }

        Assert.That(StreamingBenchmark.Route[0], Is.EqualTo("clearing"));
        foreach (string stop in StreamingBenchmark.Route)
        {
            Assert.That(entries, Does.Contain(stop), $"{stop} is an entry of the Overworld");
        }

        Assert.That(StreamingBenchmark.Route, Is.SupersetOf(Regions));
    }
}
