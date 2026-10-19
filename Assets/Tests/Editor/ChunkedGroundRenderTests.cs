using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// The comparison WorldStreaming.md's Phase 0 spike made, kept as a test: the ground of <see cref="ChunkedGround"/>
/// drawn by a renderer per chunk, each over only its own cells with a skirt of its neighbours' border cells and a
/// baked coast map, shows exactly the pixels one renderer over the whole world shows, chunk by chunk, in the crisp
/// and the soft look: the road, the trail, the bridge, the tall grass, the plateau's cliff and the sea's waves across
/// the borders included. Animated materials are drawn at a fixed time. Renders from a later editor update, until
/// every renderer draws.
/// </summary>
public class ChunkedGroundRenderTests
{
    private const float FixedTime = 2.75f;

    private ChunkedGround ground;

    /// <summary>
    /// Destroys the ground.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        ground?.Destroy();
        ground = null;
    }

    /// <summary>
    /// In each look, every pixel of each chunk drawn by its own renderer equals the same pixel drawn by the whole
    /// world's renderer.
    /// </summary>
    [UnityTest]
    public IEnumerator GroundDrawnPerChunk_MatchesTheGroundDrawnWhole_InBothLooks()
    {
        ground = new ChunkedGround();
        foreach (TerrainBlendRenderer renderer in ground.Renderers)
        {
            renderer.SetFixedTime(FixedTime);
        }

        List<string> problems = new();
        foreach (TerrainBlendRenderer.BlendMode mode in new[] { TerrainBlendRenderer.BlendMode.Crisp, TerrainBlendRenderer.BlendMode.Soft })
        {
            foreach (TerrainBlendRenderer renderer in ground.Renderers)
            {
                renderer.Mode = mode;
            }

            bool drawn = false;
            for (int frame = 0; frame < 30 && !drawn; frame++)
            {
                yield return null;
                drawn = true;
                foreach (KeyValuePair<Vector2Int, WorldChunk> chunk in ground.Chunks)
                {
                    Rect probe = new(ground.WorldRectOf(chunk.Key).center, new Vector2(2f, 2f));
                    drawn &= Draws(ground.Whole, probe) && Draws(chunk.Value.Terrain, probe);
                }
            }

            Assert.That(drawn, Is.True, $"Every renderer draws in the {mode} look.");
            foreach (KeyValuePair<Vector2Int, WorldChunk> chunk in ground.Chunks)
            {
                Rect area = ground.WorldRectOf(chunk.Key);
                Texture2D whole = TerrainBlendProbe.Render(ground.Whole, area, 1, false, true, true);
                Texture2D own = TerrainBlendProbe.Render(chunk.Value.Terrain, area, 1, false, true, true);
                Color32[] expected = whole.GetPixels32();
                Color32[] actual = own.GetPixels32();
                int width = whole.width;
                Object.DestroyImmediate(whole);
                Object.DestroyImmediate(own);
                int differing = 0;
                int nearestBorder = int.MaxValue;
                List<string> where = new();
                for (int i = 0; i < expected.Length; i++)
                {
                    if (!expected[i].Equals(actual[i]))
                    {
                        differing++;
                        int x = i % width;
                        int y = i / width;
                        nearestBorder = Mathf.Min(nearestBorder, Mathf.Min(Mathf.Min(x, width - 1 - x), Mathf.Min(y, width - 1 - y)));
                        if (where.Count < 6)
                        {
                            where.Add($"({area.xMin - ChunkedGround.Origin.x + x / 32f:F2}, {area.yMin + y / 32f:F2}) {expected[i]} vs {actual[i]}");
                        }
                    }
                }

                TestContext.Out.WriteLine($"{mode}, chunk {chunk.Key}: {differing} of {expected.Length} pixels differ.");
                if (differing > 0)
                {
                    problems.Add($"{mode}, chunk {chunk.Key}: {differing} pixels differ, the nearest {nearestBorder} px from the chunk's edge, at cells {string.Join(", ", where)}");
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    /// <summary>
    /// Whether <paramref name="renderer"/> draws anything but black in the middle of <paramref name="world"/>.
    /// </summary>
    private static bool Draws(TerrainBlendRenderer renderer, Rect world)
    {
        Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
        Color32 middle = pixels.GetPixel(pixels.width / 2, pixels.height / 2);
        Object.DestroyImmediate(pixels);
        return middle.r + middle.g + middle.b > 0;
    }
}
