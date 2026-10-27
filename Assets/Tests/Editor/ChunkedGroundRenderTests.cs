using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// The comparison WorldStreaming.md's Phase 0 spike made, kept as a test: ground drawn by a renderer per chunk, each
/// over only its own cells with a skirt of its neighbours' border cells and a baked coast map, shows exactly the pixels
/// one renderer over all of it shows, chunk by chunk, in the crisp and the soft look. Two grounds: the painted
/// <see cref="ChunkedGround"/> (the road, the trail, the bridge, the tall grass, the plateau's cliff and the sea's waves
/// across the borders), and a 2 x 2 block of the Overworld's Phase 0 composition (<see cref="TestWorldGenerators"/>)
/// written by <see cref="WorldGenerationWriter.WriteChunk"/> into chunk grids built as
/// <see cref="WorldAuthoring.CreateChunk"/> builds them, around the Lava Fields' edge (its dirt meeting the wilds'
/// grass with no grass under it), with the roads leaving its ports across that edge and the south shore, against one
/// renderer over the block's composed cells. The chunks draw with the texture arrays of the style's layer order shared
/// between them (<see cref="SharedTerrainArrays"/>, WorldScale.md's Phase 2), each layer at its material's slice, and
/// the whole renderer with its own, a slice per layer. Animated materials are drawn at a fixed time. Renders from a later
/// editor update, until every renderer draws.
/// </summary>
public class ChunkedGroundRenderTests
{
    private const float FixedTime = 2.75f;

    private static readonly Vector2Int[] GeneratedBlock = { new(2, 0), new(3, 0), new(2, 1), new(3, 1) };

    private ChunkedGround ground;
    private TestWorldGenerators generators;
    private SharedTerrainArrays shared;

    /// <summary>
    /// Destroys the ground and the generated block, then the shared arrays.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        ground?.Destroy();
        ground = null;
        generators?.Destroy();
        generators = null;
        shared?.Dispose();
        shared = null;
    }

    /// <summary>
    /// In each look, every pixel of each chunk of <see cref="ChunkedGround"/> drawn by its own renderer equals the same
    /// pixel drawn by the whole world's renderer.
    /// </summary>
    [UnityTest]
    public IEnumerator GroundDrawnPerChunk_MatchesTheGroundDrawnWhole_InBothLooks()
    {
        ground = new ChunkedGround();
        Share(ground.Chunks.Values);
        List<string> problems = new();
        IEnumerator comparing = CompareEachChunk(ground.Whole, ground.Chunks, ground.WorldRectOf, ChunkedGround.Origin, problems);
        while (comparing.MoveNext())
        {
            yield return comparing.Current;
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    /// <summary>
    /// In each look, every pixel of each chunk of a generated 2 x 2 block, around a non-grass region's edge with roads
    /// crossing it and the shore, drawn by its own renderer, equals the same pixel drawn by one renderer over the
    /// block's composed cells.
    /// </summary>
    [UnityTest]
    public IEnumerator GeneratedChunksDrawnPerChunk_MatchTheBlockDrawnWhole_InBothLooks()
    {
        generators = new TestWorldGenerators();
        WorldGenerator generator = generators.Overworld();
        WorldComposition composition = generator.Compose();
        WorldDefinition world = generator.World;
        RectInt block = new(GeneratedBlock[0] * ChunkCoords.Size, 2 * ChunkCoords.Size * Vector2Int.one);
        ExpectEdgeRoadsAndShore(composition, block);

        Dictionary<Vector2Int, WorldChunk> chunks = new();
        foreach (Vector2Int coords in GeneratedBlock)
        {
            Grid grid = generators.NewChunk(world, coords);
            Assert.That(WorldGenerationWriter.WriteChunk(grid, composition, coords), Is.Not.Null, $"chunk {coords} is written");
            chunks[coords] = grid.GetComponent<WorldChunk>();
        }

        BakeCoastMaps(world, composition.Style, chunks);
        foreach (WorldChunk chunk in chunks.Values)
        {
            foreach (WorldChunk other in chunks.Values)
            {
                chunk.Link(other);
            }
        }

        Share(chunks.Values);
        TerrainBlendRenderer whole = BuildWhole(composition, world, block, chunks.Values);
        List<string> problems = new();
        IEnumerator comparing = CompareEachChunk(whole, chunks, coords => WorldRectOf(world, coords), world.Origin, problems);
        while (comparing.MoveNext())
        {
            yield return comparing.Current;
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    /// <summary>
    /// Draws every chunk of <paramref name="chunks"/> and the same rectangle of <paramref name="whole"/> at a fixed
    /// time in the crisp and the soft look, once both draw, and adds a line to <paramref name="problems"/> for each
    /// chunk and look whose pixels differ, naming up to six of them in cells from <paramref name="origin"/>.
    /// </summary>
    private static IEnumerator CompareEachChunk(TerrainBlendRenderer whole, IReadOnlyDictionary<Vector2Int, WorldChunk> chunks, Func<Vector2Int, Rect> worldRectOf, Vector2Int origin, List<string> problems)
    {
        List<TerrainBlendRenderer> renderers = new() { whole };
        foreach (WorldChunk chunk in chunks.Values)
        {
            renderers.Add(chunk.Terrain);
        }

        foreach (TerrainBlendRenderer renderer in renderers)
        {
            renderer.SetFixedTime(FixedTime);
        }

        foreach (TerrainBlendRenderer.BlendMode mode in new[] { TerrainBlendRenderer.BlendMode.Crisp, TerrainBlendRenderer.BlendMode.Soft })
        {
            foreach (TerrainBlendRenderer renderer in renderers)
            {
                renderer.Mode = mode;
            }

            bool drawn = false;
            for (int frame = 0; frame < 30 && !drawn; frame++)
            {
                yield return null;
                drawn = true;
                foreach (KeyValuePair<Vector2Int, WorldChunk> chunk in chunks)
                {
                    Rect probe = new(worldRectOf(chunk.Key).center, new Vector2(2f, 2f));
                    drawn &= Draws(whole, probe) && Draws(chunk.Value.Terrain, probe);
                }
            }

            Assert.That(drawn, Is.True, $"Every renderer draws in the {mode} look.");
            foreach (KeyValuePair<Vector2Int, WorldChunk> chunk in chunks)
            {
                Rect area = worldRectOf(chunk.Key);
                Texture2D wholePixels = TerrainBlendProbe.Render(whole, area, 1, false, true, true);
                Texture2D own = TerrainBlendProbe.Render(chunk.Value.Terrain, area, 1, false, true, true);
                Color32[] expected = wholePixels.GetPixels32();
                Color32[] actual = own.GetPixels32();
                int width = wholePixels.width;
                Object.DestroyImmediate(wholePixels);
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
                            where.Add($"({area.xMin - origin.x + x / 32f:F2}, {area.yMin - origin.y + y / 32f:F2}) {expected[i]} vs {actual[i]}");
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
    }

    /// <summary>
    /// Has every chunk's renderer draw with the shared arrays of its style's layer order, and asserts each does.
    /// </summary>
    private void Share(IEnumerable<WorldChunk> chunks)
    {
        shared = new SharedTerrainArrays();
        foreach (WorldChunk chunk in chunks)
        {
            chunk.Terrain.Construct(shared);
            Assert.That(chunk.Terrain.SharesArrays, Is.True, $"{chunk.name} draws with the shared arrays.");
        }
    }

    /// <summary>
    /// Asserts that <paramref name="block"/> of <paramref name="composition"/> holds what the generated case is about:
    /// the Lava Fields' outline, whose ground is dirt and not grass, beside the wilds' grass; a linked port of it with
    /// its road; and the ocean.
    /// </summary>
    private static void ExpectEdgeRoadsAndShore(WorldComposition composition, RectInt block)
    {
        WorldComposition.Region lava = composition.FindRegion(TestWorldGenerators.LavaFields);
        Assert.That(lava.Generator.TerrainLayers[0].Material.name, Is.EqualTo("Dirt"), "the Lava Fields' ground is dirt");
        Assert.That(lava.Outline.FindAll(block.Contains), Is.Not.Empty, "the Lava Fields' edge lies in the block");
        List<WorldComposition.Port> linked = lava.Ports.FindAll(port => port.Linked && block.Contains(port.Cells[0]));
        Assert.That(linked, Is.Not.Empty, "a road crosses the Lava Fields' edge in the block");
        bool ocean = false;
        foreach (Vector2Int cell in block.allPositionsWithin)
        {
            ocean |= composition.TopAt(cell) != null && composition.TopAt(cell).name == "Ocean";
        }

        Assert.That(ocean, Is.True, "the shore lies in the block");
    }

    /// <summary>
    /// Bakes each chunk's coast map from the ground of <paramref name="chunks"/>, each read only inside its own cells, as
    /// <see cref="WorldAuthoring.BakeCoastMaps"/> does, and gives it to the chunk.
    /// </summary>
    private void BakeCoastMaps(WorldDefinition world, TerrainStyle style, Dictionary<Vector2Int, WorldChunk> chunks)
    {
        WorldCoastBake.WorldCells cells = new(world.LocalBounds);
        foreach (KeyValuePair<Vector2Int, WorldChunk> chunk in chunks)
        {
            foreach (TerrainBlendRenderer.Layer layer in chunk.Value.Terrain.Layers)
            {
                cells.Add(layer.Tilemap, layer.Material, ChunkCoords.Bounds(chunk.Key));
            }
        }

        foreach (KeyValuePair<Vector2Int, WorldChunk> chunk in chunks)
        {
            List<TerrainMaterial> materials = new();
            foreach (TerrainBlendRenderer.Layer layer in chunk.Value.Terrain.Layers)
            {
                materials.Add(layer.Material);
            }

            ushort[] encoded = WorldCoastBake.BakeChunk(cells, style, materials, ChunkCoords.Bounds(chunk.Key), out int width, out int height);
            Texture2D coast = encoded != null ? generators.Track(WorldCoastBake.ToTexture(encoded, width, height, $"Coast {chunk.Key}")) : null;
            chunk.Value.Configure(world, chunk.Key, coast);
            chunk.Value.enabled = false;
            chunk.Value.enabled = true;
        }
    }

    /// <summary>
    /// One renderer at <paramref name="world"/>'s origin over <paramref name="block"/>'s composed cells: a layer per
    /// material any of <paramref name="chunks"/> draws, in the style's order, each holding its material's tile in the
    /// composition's cells of the block, and its raised ground with its regions' tiles.
    /// </summary>
    private TerrainBlendRenderer BuildWhole(WorldComposition composition, WorldDefinition world, RectInt block, IEnumerable<WorldChunk> chunks)
    {
        GameObject gridObject = generators.Track(new GameObject("Whole", typeof(Grid)));
        gridObject.transform.position = new Vector3(world.Origin.x, world.Origin.y, 0f);
        gridObject.SetActive(false);
        List<TerrainMaterial> materials = new();
        foreach (WorldChunk chunk in chunks)
        {
            foreach (TerrainBlendRenderer.Layer layer in chunk.Terrain.Layers)
            {
                if (!materials.Contains(layer.Material))
                {
                    materials.Add(layer.Material);
                }
            }
        }

        materials.Sort((a, b) => composition.IndexOf(a).CompareTo(composition.IndexOf(b)));
        List<TerrainBlendRenderer.Layer> layers = new();
        foreach (TerrainMaterial material in materials)
        {
            Tilemap tilemap = new GameObject(material.name, typeof(Tilemap)).GetComponent<Tilemap>();
            tilemap.transform.SetParent(gridObject.transform, false);
            List<Vector3Int> cells = composition.CellsOf(material, block).ConvertAll(cell => new Vector3Int(cell.x, cell.y, 0));
            TileBase[] tiles = new TileBase[cells.Count];
            Array.Fill(tiles, AssetDatabase.LoadAssetAtPath<TileBase>(TerrainTileWriter.TilePath(material)));
            tilemap.SetTiles(cells.ToArray(), tiles);
            layers.Add(new TerrainBlendRenderer.Layer(tilemap, material));
        }

        Tilemap raised = null;
        foreach (Vector2Int cell in block.allPositionsWithin)
        {
            byte kind = composition.RaisedAt(cell);
            if (kind == RaisedGround.Level)
            {
                continue;
            }

            if (raised == null)
            {
                raised = new GameObject("Raised", typeof(Tilemap)).GetComponent<Tilemap>();
                raised.transform.SetParent(gridObject.transform, false);
            }

            AreaGenerator owner = composition.Regions[composition.RegionAt(cell)].Generator;
            raised.SetTile(new Vector3Int(cell.x, cell.y, 0), kind == RaisedGround.Ramp ? owner.RampTile : owner.RaisedTile);
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(composition.Style, raised, layers.ToArray());
        gridObject.SetActive(true);
        return renderer;
    }

    /// <summary>
    /// The world rectangle, in units, of the chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    private static Rect WorldRectOf(WorldDefinition world, Vector2Int coords)
    {
        RectInt cells = ChunkCoords.Bounds(coords);
        return new Rect(world.Origin.x + cells.xMin, world.Origin.y + cells.yMin, cells.width, cells.height);
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
