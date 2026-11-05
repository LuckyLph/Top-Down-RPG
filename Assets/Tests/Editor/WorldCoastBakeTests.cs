using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="WorldCoastBake"/>: each chunk's coast map, baked from the whole world, holds exactly the
/// distances a renderer over the whole world builds for the same cells, so its waves meet the next chunk's, and each
/// chunk renderer draws the map baked for it rather than building one; a chunk whose layers, cells and skirt show no
/// sea with waves bakes nothing. Uses <see cref="ChunkedGround"/>, whose ocean crosses the border of the two southern
/// chunks. What a chunk's map is baked from (<see cref="WorldCoastBake.SourceOf"/>) changes when the sea or its shore
/// within the bake's reach changes, not for ground under the sea's order or beyond the reach, and
/// <see cref="WorldAuthoring.IsCoastStale"/> tells from it which chunks need baking again.
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
    /// Every chunk gets a coast map over its control map whose every texel equals the whole renderer's at the same place
    /// (its edge texel past its edge, as a clamped sampler reads it), and each listing the ocean draws it; one whose own
    /// cells and skirt hold no ocean lists none (Docs/KnownGaps.md, Phase 6) and has nothing to draw it with.
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
            bool listsWaves = false;
            foreach (TerrainBlendRenderer.Layer layer in chunk.Value.Terrain.Layers)
            {
                listsWaves |= layer.Material.HasWaves;
            }

            Assert.That(chunk.Value.Terrain.IsCoastBaked, Is.EqualTo(listsWaves), $"Chunk {chunk.Key} draws its baked map when it lists the ocean.");
            if (listsWaves)
            {
                Assert.That(chunk.Value.Terrain.CoastTexture, Is.SameAs(chunk.Value.CoastMap));
            }

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

    /// <summary>
    /// On a world of grass with an ocean in the south-east of chunk (1, 0): the source of chunk (1, 0) stays the same
    /// when gravel is laid on the grass near the shore or grass is removed more than the bake's reach away, and changes
    /// when the ocean grows by a cell or a hole opens in the grass within reach; chunk (0, 1), far from the sea, shows
    /// no waves. A chunk is stale when it shows waves and its map is missing or was baked from another source, or shows
    /// none and still has a map.
    /// </summary>
    [Test]
    public void TheBakeSource_FollowsTheSeaAndItsShore_AndTellsStaleChunks()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(WorldAuthoring.StylePath);
        TerrainMaterial grass = WorldAuthoring.MaterialNamed("Grass");
        TerrainMaterial gravel = WorldAuthoring.MaterialNamed("Gravel");
        TerrainMaterial ocean = WorldAuthoring.MaterialNamed("Ocean");
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        GameObject root = new("Source test", typeof(Grid));
        try
        {
            Tilemap grassMap = Layer(root, "Grass");
            Tilemap gravelMap = Layer(root, "Gravel");
            Tilemap oceanMap = Layer(root, "Ocean");
            Fill(grassMap, new RectInt(0, 0, 128, 128), tile);
            Fill(oceanMap, new RectInt(100, 0, 28, 20), tile);
            RectInt east = ChunkCoords.Bounds(new Vector2Int(1, 0));
            TerrainMaterial[] listed = { grass, gravel, ocean };
            string Source()
            {
                WorldCoastBake.WorldCells cells = new(new RectInt(0, 0, 128, 128));
                cells.Add(grassMap, grass, cells.Area);
                cells.Add(gravelMap, gravel, cells.Area);
                cells.Add(oceanMap, ocean, cells.Area);
                return WorldCoastBake.SourceOf(cells, style, listed, east);
            }

            string before = Source();
            Assert.That(before, Is.Not.EqualTo(WorldCoastBake.NoWaves));
            Fill(gravelMap, new RectInt(95, 0, 3, 20), tile);
            Assert.That(Source(), Is.EqualTo(before), "Gravel on the grass is no sea.");
            grassMap.SetTile(new Vector3Int(2, 120, 0), null);
            Assert.That(Source(), Is.EqualTo(before), "A hole more than 40 cells away is out of reach.");
            oceanMap.SetTile(new Vector3Int(99, 5, 0), tile);
            string grown = Source();
            Assert.That(grown, Is.Not.EqualTo(before), "The ocean grew.");
            grassMap.SetTile(new Vector3Int(70, 50, 0), null);
            Assert.That(Source(), Is.Not.EqualTo(grown), "A hole within reach is sea.");

            WorldCoastBake.WorldCells world = new(new RectInt(0, 0, 128, 128));
            world.Add(grassMap, grass, world.Area);
            world.Add(oceanMap, ocean, world.Area);
            Assert.That(WorldCoastBake.SourceOf(world, style, new[] { grass }, ChunkCoords.Bounds(new Vector2Int(0, 1))), Is.EqualTo(WorldCoastBake.NoWaves));

            WorldChunk marker = root.AddComponent<WorldChunk>();
            Texture2D map = new(2, 2);
            try
            {
                Assert.That(WorldAuthoring.IsCoastStale(marker, WorldCoastBake.NoWaves), Is.False);
                Assert.That(WorldAuthoring.IsCoastStale(marker, before), Is.True, "Waves and no map.");
                marker.Configure(null, new Vector2Int(1, 0), map);
                SerializedObject data = new(marker);
                data.FindProperty("coastSource").stringValue = before;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(WorldAuthoring.IsCoastStale(marker, before), Is.False);
                Assert.That(WorldAuthoring.IsCoastStale(marker, grown), Is.True, "Baked from another sea.");
                Assert.That(WorldAuthoring.IsCoastStale(marker, WorldCoastBake.NoWaves), Is.True, "No waves but a map.");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(tile);
        }
    }

    /// <summary>
    /// A plain tilemap named <paramref name="name"/> under <paramref name="root"/>.
    /// </summary>
    private static Tilemap Layer(GameObject root, string name)
    {
        GameObject layer = new(name, typeof(Tilemap));
        layer.transform.SetParent(root.transform, false);
        return layer.GetComponent<Tilemap>();
    }

    /// <summary>
    /// Fills <paramref name="cells"/> of <paramref name="tilemap"/> with <paramref name="tile"/>.
    /// </summary>
    private static void Fill(Tilemap tilemap, RectInt cells, TileBase tile)
    {
        foreach (Vector2Int cell in cells.allPositionsWithin)
        {
            tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), tile);
        }
    }
}
