using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="WorldCoastBake"/>: each chunk's coast map, baked from the whole world, holds exactly the
/// distances a renderer over the whole world builds for the same cells, so its waves meet the next chunk's, and each
/// chunk renderer draws the map baked for it rather than building one; a chunk whose layers, cells and skirt show no
/// sea with waves bakes nothing. Uses <see cref="ChunkedGround"/>, whose ocean crosses the border of the two southern
/// chunks.
/// </summary>
public class WorldCoastBakeTests
{
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
    /// Every chunk, all of them listing the ocean, gets a coast map over its control map whose every texel equals the
    /// whole renderer's at the same place (its edge texel past its edge, as a clamped sampler reads it), and draws it.
    /// A northern chunk listing only grass, far from the sea, bakes nothing. The bake's time per chunk is written to
    /// the test's output.
    /// </summary>
    [Test]
    public void EachChunksBakedCoastMap_MatchesTheWholeWorldsCoastMap()
    {
        ground = new ChunkedGround();
        Texture2D whole = (Texture2D)ground.Whole.CoastTexture;
        BoundsInt wholeBounds = ground.Whole.ControlMap.Bounds;
        Assert.That(whole.width, Is.EqualTo(wholeBounds.size.x * TerrainCoastMap.TexelsPerCell));
        ushort[] wholeTexels = whole.GetPixelData<ushort>(0).ToArray();
        List<string> problems = new();
        foreach (KeyValuePair<Vector2Int, WorldChunk> chunk in ground.Chunks)
        {
            TestContext.Out.WriteLine($"Chunk {chunk.Key}: baked in {ground.BakeMilliseconds[chunk.Key]:F1} ms.");
            Assert.That(chunk.Value.CoastMap, Is.Not.Null, $"Chunk {chunk.Key} has a baked coast map.");
            Assert.That(chunk.Value.Terrain.IsCoastBaked, Is.True, $"Chunk {chunk.Key} draws its baked map.");
            Assert.That(chunk.Value.Terrain.CoastTexture, Is.SameAs(chunk.Value.CoastMap));
            RectInt area = TerrainBlendRenderer.ChunkControlArea(chunk.Value.Cells, WorldChunk.SkirtCells);
            ushort[] baked = chunk.Value.CoastMap.GetPixelData<ushort>(0).ToArray();
            int width = area.width * TerrainCoastMap.TexelsPerCell;
            int wholeWidth = whole.width;
            int mismatches = 0;
            for (int y = 0; y < area.height * TerrainCoastMap.TexelsPerCell; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int wx = Mathf.Clamp((area.xMin - wholeBounds.xMin) * TerrainCoastMap.TexelsPerCell + x, 0, wholeWidth - 1);
                    int wy = Mathf.Clamp((area.yMin - wholeBounds.yMin) * TerrainCoastMap.TexelsPerCell + y, 0, whole.height - 1);
                    if (baked[y * width + x] != wholeTexels[wy * wholeWidth + wx])
                    {
                        mismatches++;
                        if (problems.Count < 10)
                        {
                            problems.Add($"chunk {chunk.Key} texel ({x}, {y}): {baked[y * width + x]}, whole {wholeTexels[wy * wholeWidth + wx]}");
                        }
                    }
                }
            }

            Assert.That(mismatches, Is.Zero, string.Join("\n", problems));
        }

        RectInt north = ChunkCoords.Bounds(new Vector2Int(0, 1));
        Assert.That(WorldCoastBake.BakeChunk(ground.Cells, ground.Style, new[] { ground.Material("Grass") }, north, out _, out _), Is.Null, "A chunk showing no sea bakes nothing.");
    }
}
