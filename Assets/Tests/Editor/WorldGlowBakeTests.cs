using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="WorldGlowBake"/> (Docs/LightingGaps.md, Phase 5): each chunk's glow map, baked from the whole
/// world, covers the cells and holds every texel the chunk's renderer builds with its neighbour linked, and the chunk
/// draws the baked map rather than building one; a chunk with nothing glowing within reach bakes nothing. What a
/// chunk's map is baked from (<see cref="WorldGlowBake.SourceOf"/>) changes when the glowing ground within reach
/// changes, not for other ground nor for lava beyond the reach, and <see cref="WorldAuthoring.IsGlowStale"/> tells
/// from it which chunks need baking again. A world of two chunks side by side, grass with a block of lava across
/// their border.
/// </summary>
public class WorldGlowBakeTests
{
    private static readonly Vector2Int Origin = new(6144, 0);
    private static readonly Vector2Int[] ChunkCoordinates = { new(0, 0), new(1, 0) };
    private static readonly RectInt LavaCells = new(60, 20, 8, 8);

    private readonly List<Object> created = new();
    private WorldDefinition world;
    private TerrainMaterial grass;
    private TerrainMaterial lava;
    private Tile tile;

    /// <summary>
    /// Loads the project's grass and lava and makes the world and a tile.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        grass = WorldAuthoring.MaterialNamed("Grass");
        lava = WorldAuthoring.MaterialNamed("Lava");
        Assert.That(lava.Glows, Is.True);
        world = Track(WorldDefinition.Create("GlowWorld", Origin, new Vector2Int(2, 1), ChunkCoordinates));
        tile = Track(ScriptableObject.CreateInstance<Tile>());
    }

    /// <summary>
    /// Destroys everything the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Both chunks, their renderers linked as when streamed, build a glow map; each one baked from the whole world
    /// covers the same cells with the same texels, and given to its chunk the renderer draws it, its glow light's
    /// cookie made over the baked texture. Away from the lava nothing is baked.
    /// </summary>
    [Test]
    public void EachChunksBakedGlowMap_MatchesTheGlowItsRendererBuilds()
    {
        Dictionary<Vector2Int, WorldChunk> chunks = new();
        foreach (Vector2Int coords in ChunkCoordinates)
        {
            chunks[coords] = BuildChunk(coords);
        }

        foreach (WorldChunk chunk in chunks.Values)
        {
            foreach (WorldChunk other in chunks.Values)
            {
                chunk.Link(other);
            }
        }

        foreach (WorldChunk chunk in chunks.Values)
        {
            chunk.Terrain.RunScheduledRebuild();
            chunk.Terrain.FlushPendingChanges();
        }

        WorldCoastBake.WorldCells cells = Cells();
        TerrainMaterial[] materials = { grass, lava };
        foreach (KeyValuePair<Vector2Int, WorldChunk> chunk in chunks)
        {
            TerrainGlowMap built = chunk.Value.Terrain.GlowMap;
            Assert.That(built, Is.Not.Null, $"Chunk {chunk.Key} has a glow map.");
            Assert.That(built.IsEmpty, Is.False, $"Chunk {chunk.Key} glows.");
            Assert.That(chunk.Value.Terrain.IsGlowBaked, Is.False);
            Texture2D baked = Track(WorldGlowBake.BakeChunk(cells, materials, chunk.Value.Cells, $"Glow {chunk.Key}", out RectInt area));
            Assert.That(baked, Is.Not.Null);
            Assert.That(area, Is.EqualTo(built.Area), $"Chunk {chunk.Key}'s baked area.");
            Color32[] texels = baked.GetPixels32();
            int mismatches = 0;
            for (int y = 0; y < baked.height; y++)
            {
                for (int x = 0; x < baked.width; x++)
                {
                    mismatches += texels[y * baked.width + x].r == built.EncodedAt(x, y) ? 0 : 1;
                }
            }

            Assert.That(mismatches, Is.Zero, $"Chunk {chunk.Key}'s baked texels.");

            SerializedObject data = new(chunk.Value);
            data.FindProperty("glowMap").objectReferenceValue = baked;
            data.FindProperty("glowArea").rectIntValue = area;
            data.ApplyModifiedPropertiesWithoutUndo();
            chunk.Value.enabled = false;
            chunk.Value.enabled = true;
            chunk.Value.Terrain.RunScheduledRebuild();
            Assert.That(chunk.Value.Terrain.IsGlowBaked, Is.True, $"Chunk {chunk.Key} draws its baked map.");
            Assert.That(chunk.Value.Terrain.GlowMap.Area, Is.EqualTo(area));
            Assert.That(chunk.Value.Terrain.GlowLight, Is.Not.Null);
            Assert.That(chunk.Value.Terrain.GlowLight.lightCookieSprite.texture, Is.EqualTo(baked));
        }

        Assert.That(WorldGlowBake.BakeChunk(cells, materials, new RectInt(0, 0, 32, 10), "Far", out RectInt far), Is.Null, "Nothing glows within reach.");
        Assert.That(far.width, Is.Zero);
        Assert.That(WorldGlowBake.BakeChunk(cells, new[] { grass }, ChunkCoords.Bounds(new Vector2Int(0, 0)), "Grass", out _), Is.Null, "A chunk listing nothing that glows.");
    }

    /// <summary>
    /// The source of chunk (1, 0) stays the same when grass changes or lava is laid beyond the glow's reach, and
    /// changes when the lava grows within it; a chunk with nothing glowing within reach is <see cref="WorldGlowBake.NoGlow"/>.
    /// A chunk is stale when something glows and its map is missing or was baked from another source, or nothing glows
    /// and it still has a map.
    /// </summary>
    [Test]
    public void TheGlowSource_FollowsTheGlowingCells_AndTellsStaleChunks()
    {
        GameObject root = Track(new GameObject("Source test", typeof(Grid)));
        Tilemap grassMap = Layer(root, "Grass");
        Tilemap lavaMap = Layer(root, "Lava");
        Fill(grassMap, new RectInt(0, 0, 128, 64));
        Fill(lavaMap, LavaCells);
        RectInt east = ChunkCoords.Bounds(new Vector2Int(1, 0));
        TerrainMaterial[] listed = { grass, lava };
        string Source(RectInt chunk)
        {
            WorldCoastBake.WorldCells cells = new(new RectInt(0, 0, 128, 64));
            cells.Add(grassMap, grass, cells.Area);
            cells.Add(lavaMap, lava, cells.Area);
            return WorldGlowBake.SourceOf(cells, listed, chunk);
        }

        string before = Source(east);
        Assert.That(before, Is.Not.EqualTo(WorldGlowBake.NoGlow));
        grassMap.SetTile(new Vector3Int(70, 30, 0), null);
        Assert.That(Source(east), Is.EqualTo(before), "Grass is no glowing ground.");
        lavaMap.SetTile(new Vector3Int(58, 40, 0), tile);
        Assert.That(Source(east), Is.EqualTo(before), "Lava six cells west of the chunk is beyond the reach.");
        lavaMap.SetTile(new Vector3Int(70, 24, 0), tile);
        string grown = Source(east);
        Assert.That(grown, Is.Not.EqualTo(before), "The lava grew within the chunk.");
        Assert.That(Source(new RectInt(0, 50, 20, 14)), Is.EqualTo(WorldGlowBake.NoGlow), "Nothing glows within reach.");

        WorldChunk marker = Track(new GameObject("Marker")).AddComponent<WorldChunk>();
        Assert.That(WorldAuthoring.IsGlowStale(marker, WorldGlowBake.NoGlow), Is.False);
        Assert.That(WorldAuthoring.IsGlowStale(marker, before), Is.True, "Glowing and no map.");
        Texture2D map = Track(new Texture2D(4, 4));
        SerializedObject data = new(marker);
        data.FindProperty("glowMap").objectReferenceValue = map;
        data.FindProperty("glowSource").stringValue = before;
        data.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(WorldAuthoring.IsGlowStale(marker, before), Is.False);
        Assert.That(WorldAuthoring.IsGlowStale(marker, grown), Is.True, "Baked from other lava.");
        Assert.That(WorldAuthoring.IsGlowStale(marker, WorldGlowBake.NoGlow), Is.True, "Nothing glows but a map.");
    }

    /// <summary>
    /// The world's ground as the bake reads it: grass everywhere, the lava block.
    /// </summary>
    private WorldCoastBake.WorldCells Cells()
    {
        GameObject root = Track(new GameObject("Whole", typeof(Grid)));
        Tilemap grassMap = Layer(root, "Grass");
        Tilemap lavaMap = Layer(root, "Lava");
        Fill(grassMap, world.LocalBounds);
        Fill(lavaMap, LavaCells);
        WorldCoastBake.WorldCells cells = new(world.LocalBounds);
        cells.Add(grassMap, grass, world.LocalBounds);
        cells.Add(lavaMap, lava, world.LocalBounds);
        return cells;
    }

    /// <summary>
    /// An enabled grid at the world's origin for the chunk at <paramref name="coords"/>, with a grass and a lava layer
    /// holding only its own cells, its <see cref="WorldChunk"/> without a baked map, and a terrain renderer over them.
    /// </summary>
    private WorldChunk BuildChunk(Vector2Int coords)
    {
        RectInt cells = ChunkCoords.Bounds(coords);
        GameObject gridObject = Track(new GameObject($"Chunk {coords.x},{coords.y}", typeof(Grid)));
        gridObject.transform.position = new Vector3(Origin.x, Origin.y, 0f);
        gridObject.SetActive(false);
        Tilemap grassMap = Layer(gridObject, "Grass");
        Tilemap lavaMap = Layer(gridObject, "Lava");
        Fill(grassMap, cells);
        Fill(lavaMap, Intersect(LavaCells, cells));
        WorldChunk chunk = gridObject.AddComponent<WorldChunk>();
        chunk.Configure(world, coords, null);
        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(AssetDatabase.LoadAssetAtPath<TerrainStyle>(WorldAuthoring.StylePath), new TerrainBlendRenderer.Layer(grassMap, grass), new TerrainBlendRenderer.Layer(lavaMap, lava));
        gridObject.SetActive(true);
        return chunk;
    }

    /// <summary>
    /// A tilemap named <paramref name="layerName"/> under <paramref name="root"/>.
    /// </summary>
    private static Tilemap Layer(GameObject root, string layerName)
    {
        Tilemap tilemap = new GameObject(layerName, typeof(Tilemap)).GetComponent<Tilemap>();
        tilemap.transform.SetParent(root.transform, false);
        return tilemap;
    }

    /// <summary>
    /// Fills <paramref name="cells"/> of <paramref name="tilemap"/> with the tile.
    /// </summary>
    private void Fill(Tilemap tilemap, RectInt cells)
    {
        foreach (Vector2Int cell in cells.allPositionsWithin)
        {
            tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), tile);
        }
    }

    /// <summary>
    /// The cells <paramref name="a"/> and <paramref name="b"/> share; zero size when none.
    /// </summary>
    private static RectInt Intersect(RectInt a, RectInt b)
    {
        int xMin = Mathf.Max(a.xMin, b.xMin);
        int yMin = Mathf.Max(a.yMin, b.yMin);
        int xMax = Mathf.Min(a.xMax, b.xMax);
        int yMax = Mathf.Min(a.yMax, b.yMax);
        return xMax > xMin && yMax > yMin ? new RectInt(xMin, yMin, xMax - xMin, yMax - yMin) : new RectInt(0, 0, 0, 0);
    }

    /// <summary>
    /// Adds <paramref name="made"/> to what <see cref="TearDown"/> destroys.
    /// </summary>
    private T Track<T>(T made) where T : Object
    {
        created.Add(made);
        return made;
    }
}
