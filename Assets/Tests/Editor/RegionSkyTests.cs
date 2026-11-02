using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the region skies at runtime (Docs/Lighting.md, Phase 8): a chunk's <see cref="ChunkSky"/> blends a sky's
/// weight between its samples, every <see cref="ChunkSky.Spacing"/> cells; <see cref="RegionSkies"/> blends the skies
/// reaching a place of a world by their weights into one tint and cloud cover, the day's own by what weight is left and
/// scaled down where the weights add up to more than one, and the day's own where no chunk with skies is held or for
/// another world; and a chunk's <see cref="ChunkRegionSky"/> hands its sky over as it starts and takes it back when
/// disposed.
/// </summary>
public class RegionSkyTests
{
    private static readonly Color Cold = new(0.9f, 0.96f, 1.1f, 1f);
    private static readonly Color Warm = new(1.04f, 0.86f, 0.74f, 1f);

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A sky full at one sample and none at the others weighs 1 there, half way to the next sample along a side, a
    /// quarter on the diagonal, nothing two samples off, and nothing for a sky the chunk does not list; places past the
    /// chunk's edges read its edge samples.
    /// </summary>
    [Test]
    public void AChunksSky_BlendsBetweenItsSamples()
    {
        byte[] weights = new byte[ChunkSky.SamplesPerSky];
        weights[1 * ChunkSky.Samples + 1] = 255;
        weights[ChunkSky.SamplesPerSky - 1] = 255;
        ChunkSky sky = CreateChunkSky(new[] { Track(RegionSky.Create(Cold)) }, weights);
        float spacing = ChunkSky.Spacing;
        Assert.That(sky.WeightAt(0, new Vector2(spacing, spacing)), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(sky.WeightAt(0, new Vector2(spacing * 1.5f, spacing)), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(sky.WeightAt(0, new Vector2(spacing * 0.5f, spacing * 0.5f)), Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(sky.WeightAt(0, new Vector2(spacing * 3f, spacing)), Is.EqualTo(0f));
        Assert.That(sky.WeightAt(1, new Vector2(spacing, spacing)), Is.EqualTo(0f), "A sky the chunk does not list.");
        Assert.That(sky.WeightAt(0, new Vector2(ChunkCoords.Size + 5f, ChunkCoords.Size + 5f)), Is.EqualTo(1f).Within(1e-5f), "Past the edge, the corner sample.");
    }

    /// <summary>
    /// Where one sky is full its tint and cover are the blend; where it is half, half of it and half the day's own; where
    /// two skies are full together, half of each; where none weighs, and in a chunk not held, of another world or let
    /// go, the day's own.
    /// </summary>
    [Test]
    public void TheRegionSkies_BlendTheSkiesReachingAPlace_ByTheirWeights()
    {
        RegionSky cold = Track(RegionSky.Create(Cold, 1.3f));
        RegionSky warm = Track(RegionSky.Create(Warm, 0.7f));
        byte[] weights = new byte[2 * ChunkSky.SamplesPerSky];
        for (int i = 0; i < ChunkSky.Samples; i++)
        {
            weights[0 * ChunkSky.Samples + i] = 255;
            weights[1 * ChunkSky.Samples + i] = 255;
            weights[2 * ChunkSky.Samples + i] = 128;
            weights[ChunkSky.SamplesPerSky + 0 * ChunkSky.Samples + i] = 255;
        }

        ChunkSky chunk = CreateChunkSky(new[] { cold, warm }, weights);
        WorldDefinition world = Track(ScriptableObject.CreateInstance<WorldDefinition>());
        WorldDefinition other = Track(ScriptableObject.CreateInstance<WorldDefinition>());
        Vector2Int coords = new(1, 2);
        Vector2 origin = coords * ChunkCoords.Size;
        RegionSkies skies = new();
        skies.Add(world, coords, chunk);
        Assert.That(skies.Count, Is.EqualTo(1));

        RegionSkyBlend both = skies.At(world, origin + new Vector2(10f, 0f));
        ExpectColour(both.Tint, (Cold + Warm) * 0.5f, "Two full skies, half of each.");
        Assert.That(both.CloudCover, Is.EqualTo(1f).Within(1e-4f));

        RegionSkyBlend one = skies.At(world, origin + new Vector2(10f, ChunkSky.Spacing));
        ExpectColour(one.Tint, Cold, "One full sky.");
        Assert.That(one.CloudCover, Is.EqualTo(1.3f).Within(1e-4f));

        RegionSkyBlend half = skies.At(world, origin + new Vector2(10f, 2f * ChunkSky.Spacing));
        float share = 128f / 255f;
        ExpectColour(half.Tint, Color.white * (1f - share) + Cold * share, "Half a sky, half the day's own.");
        Assert.That(half.CloudCover, Is.EqualTo(1f + 0.3f * share).Within(1e-4f));

        ExpectNone(skies.At(world, origin + new Vector2(10f, 4f * ChunkSky.Spacing)), "No weight.");
        ExpectNone(skies.At(world, origin + new Vector2(-10f, 0f)), "Another chunk, not held.");
        ExpectNone(skies.At(other, origin + new Vector2(10f, 0f)), "Another world.");
        skies.Remove(world, coords, chunk);
        Assert.That(skies.Count, Is.EqualTo(0));
        ExpectNone(skies.At(world, origin + new Vector2(10f, ChunkSky.Spacing)), "Let go.");
    }

    /// <summary>
    /// A chunk's entry point hands its sky to the region skies as it starts, and takes it back when disposed.
    /// </summary>
    [Test]
    public void AChunk_HandsItsSkyOver_AndTakesItBack()
    {
        byte[] weights = new byte[ChunkSky.SamplesPerSky];
        System.Array.Fill(weights, (byte)255);
        ChunkSky chunk = CreateChunkSky(new[] { Track(RegionSky.Create(Cold)) }, weights);
        WorldDefinition world = Track(ScriptableObject.CreateInstance<WorldDefinition>());
        Vector2Int coords = new(0, 1);
        RegionSkies skies = new();
        ChunkRegionSky entry = new(skies, chunk, new ChunkIdentity(world, coords));
        entry.Start();
        ExpectColour(skies.At(world, new Vector2(5f, ChunkCoords.Size + 5f)).Tint, Cold, "Handed over.");
        entry.Dispose();
        Assert.That(skies.Count, Is.EqualTo(0), "Taken back.");
    }

    /// <summary>
    /// A chunk sky holding <paramref name="skies"/> and <paramref name="weights"/>.
    /// </summary>
    private ChunkSky CreateChunkSky(RegionSky[] skies, byte[] weights)
    {
        ChunkSky sky = Track(new GameObject("Grid")).AddComponent<ChunkSky>();
        sky.Configure(skies, weights);
        return sky;
    }

    /// <summary>
    /// Asserts <paramref name="blend"/> is the day's own sky.
    /// </summary>
    private static void ExpectNone(RegionSkyBlend blend, string what)
    {
        ExpectColour(blend.Tint, Color.white, what);
        Assert.That(blend.CloudCover, Is.EqualTo(1f), what);
    }

    /// <summary>
    /// Asserts <paramref name="actual"/>'s colour channels are <paramref name="expected"/>'s, to within rounding.
    /// </summary>
    private static void ExpectColour(Color actual, Color expected, string what)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-4f), what);
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-4f), what);
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-4f), what);
    }

    /// <summary>
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
