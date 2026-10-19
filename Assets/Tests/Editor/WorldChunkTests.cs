using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="WorldChunk"/>: linked neighbours give each other the strip of their border cells as a skirt, a
/// diagonal neighbour its corner, and a disabled chunk takes its strip back from its neighbours; a chunk of another
/// world or one that is not a neighbour is not linked. Walls drawn across the borders of four chunks show the same
/// render tiles, sprite for sprite, as the same walls on one grid, each render tile drawn by one chunk only; once a
/// neighbour is gone, the chunk left draws its walls' edge along that border as a grid of its cells alone would. Each
/// test builds its own grids, style, materials and world, and reads the project's walls rule tile.
/// </summary>
public class WorldChunkTests
{
    private const string WallsRuleTilePath = "Assets/Tiles/tilesets/walls_DualGridRuleTile.asset";

    private readonly List<Object> created = new();
    private Tile tile;
    private TerrainStyle style;
    private TerrainMaterial grass;
    private WorldDefinition world;
    private DualGridRuleTile walls;

    /// <summary>
    /// Creates a tile, a plain grass material in a style, a 2 x 2 world and loads the walls rule tile.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        tile = Track(ScriptableObject.CreateInstance<Tile>());
        TerrainMaterial[] plain = TestTerrainStyle.PlainMaterials(created);
        grass = plain[0];
        style = TestTerrainStyle.Create(created, plain);
        world = Track(WorldDefinition.Create("ChunkTest", new Vector2Int(2048, 0), new Vector2Int(2, 2), new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }));
        walls = AssetDatabase.LoadAssetAtPath<DualGridRuleTile>(WallsRuleTilePath);
        Assert.That(walls, Is.Not.Null);
    }

    /// <summary>
    /// Destroys what the test created.
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
    /// Linked side by side, each chunk's terrain reads the other's grass in a strip 4 cells wide along their border;
    /// a diagonal neighbour gives a 4 x 4 corner; disabling a chunk takes its strips back from the others. A chunk of
    /// another world, or two chunks apart, is not linked.
    /// </summary>
    [Test]
    public void LinkedNeighbours_ReadEachOthersBorderStrip_UntilOneIsDisabled()
    {
        WorldChunk a = NewChunk(new Vector2Int(0, 0), world);
        WorldChunk b = NewChunk(new Vector2Int(1, 0), world);
        WorldChunk c = NewChunk(new Vector2Int(1, 1), world);
        a.Link(b);
        a.Link(c);

        Assert.That(a.Neighbours, Is.EquivalentTo(new[] { b, c }));
        Assert.That(b.Neighbours, Is.EquivalentTo(new[] { a }));
        Assert.That(StripsOf(a), Is.EquivalentTo(new[] { new RectInt(64, 0, 4, 64), new RectInt(64, 64, 4, 4) }));
        Assert.That(StripsOf(b), Is.EquivalentTo(new[] { new RectInt(60, 0, 4, 64) }));
        Assert.That(a.Terrain.ControlMap.GetCell(new Vector3Int(67, 10, 0)), Is.EqualTo(1u));
        Assert.That(a.Terrain.ControlMap.GetCell(new Vector3Int(66, 66, 0)), Is.EqualTo(1u));
        Assert.That(a.Terrain.ControlMap.GetCell(new Vector3Int(10, 66, 0)), Is.EqualTo(0u), "No chunk above is loaded.");

        b.enabled = false;

        Assert.That(a.Neighbours, Is.EquivalentTo(new[] { c }));
        Assert.That(a.Terrain.ControlMap.GetCell(new Vector3Int(67, 10, 0)), Is.EqualTo(0u));

        WorldDefinition other = Track(WorldDefinition.Create("OtherWorld", new Vector2Int(4096, 0), new Vector2Int(2, 2), new[] { new Vector2Int(0, 1) }));
        WorldChunk stranger = NewChunk(new Vector2Int(0, 1), other);
        a.Link(stranger);
        Assert.That(a.Neighbours, Has.No.Member(stranger), "A chunk of another world is not linked.");
    }

    /// <summary>
    /// Walls painted across the borders of four chunks, through each chunk's own module, show at every render cell
    /// around the meeting corner the sprite and transform the same walls show on one grid, drawn by exactly one chunk.
    /// With the chunk at (1, 0) gone, the chunk at (0, 0) draws the render cells along their border, matching one grid
    /// holding only its own walls.
    /// </summary>
    [Test]
    public void WallsAcrossBorders_MatchTheSameWallsOnOneGrid()
    {
        List<Vector3Int> cells = new();
        for (int x = 58; x <= 69; x++)
        {
            cells.Add(new Vector3Int(x, 10, 0));
        }

        for (int y = 58; y <= 69; y++)
        {
            cells.Add(new Vector3Int(10, y, 0));
        }

        for (int y = 63; y <= 64; y++)
        {
            for (int x = 63; x <= 64; x++)
            {
                cells.Add(new Vector3Int(x, y, 0));
            }
        }

        Dictionary<Vector2Int, WorldChunk> chunks = new();
        foreach (Vector2Int coords in new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) })
        {
            chunks[coords] = NewChunk(coords, world, true);
        }

        foreach (WorldChunk chunk in chunks.Values)
        {
            foreach (WorldChunk other in chunks.Values)
            {
                chunk.Link(other);
            }

            chunk.GetComponentInChildren<DualGridTilemapModule>().SetDataTiles(cells.FindAll(cell => chunk.Cells.Contains((Vector2Int)cell)));
        }

        DualGridTilemapModule whole = NewWalls(NewGrid("Whole").transform);
        whole.SetDataTiles(cells);
        List<string> problems = new();
        foreach (Vector2Int at in new RectInt(4, 4, 70, 70).allPositionsWithin)
        {
            Vector3Int render = new(at.x, at.y, 0);
            int drawn = 0;
            Tilemap drawing = null;
            foreach (WorldChunk chunk in chunks.Values)
            {
                Tilemap tilemap = chunk.GetComponentInChildren<DualGridTilemapModule>().RenderTilemap;
                if (tilemap.HasTile(render))
                {
                    drawn++;
                    drawing = tilemap;
                }
            }

            bool expected = whole.RenderTilemap.HasTile(render);
            if (drawn != (expected ? 1 : 0))
            {
                problems.Add($"{render}: drawn by {drawn} chunks, {(expected ? 1 : 0)} on one grid");
            }
            else if (expected && (drawing.GetSprite(render) != whole.RenderTilemap.GetSprite(render) || drawing.GetTransformMatrix(render) != whole.RenderTilemap.GetTransformMatrix(render)))
            {
                problems.Add($"{render}: {Name(drawing.GetSprite(render))} where one grid shows {Name(whole.RenderTilemap.GetSprite(render))}");
            }
        }

        Assert.That(problems, Is.Empty);

        chunks[new Vector2Int(1, 0)].enabled = false;
        DualGridTilemapModule alone = NewWalls(NewGrid("Alone").transform);
        alone.SetDataTiles(cells.FindAll(cell => chunks[new Vector2Int(0, 0)].Cells.Contains((Vector2Int)cell)));
        Tilemap left = chunks[new Vector2Int(0, 0)].GetComponentInChildren<DualGridTilemapModule>().RenderTilemap;
        for (int y = 4; y < 20; y++)
        {
            Vector3Int render = new(64, y, 0);
            Assert.That(left.GetSprite(render), Is.EqualTo(alone.RenderTilemap.GetSprite(render)), $"{render} shows the wall's edge once its neighbour is gone");
        }

        Assert.That(left.HasTile(new Vector3Int(64, 10, 0)), Is.True);
    }

    /// <summary>
    /// An enabled chunk of <paramref name="chunkWorld"/> at <paramref name="coords"/>: a grid at the world's origin
    /// whose terrain renderer draws one grass layer filling the chunk's cells, with walls when <paramref name="withWalls"/>.
    /// </summary>
    private WorldChunk NewChunk(Vector2Int coords, WorldDefinition chunkWorld, bool withWalls = false)
    {
        GameObject gridObject = NewGrid($"Chunk {coords}");
        gridObject.transform.position = new Vector3(chunkWorld.Origin.x, chunkWorld.Origin.y, 0f);
        gridObject.SetActive(false);
        Tilemap ground = new GameObject("Grass", typeof(Tilemap)).GetComponent<Tilemap>();
        ground.transform.SetParent(gridObject.transform, false);
        List<Vector3Int> positions = new();
        foreach (Vector2Int cell in ChunkCoords.Bounds(coords).allPositionsWithin)
        {
            positions.Add(new Vector3Int(cell.x, cell.y, 0));
        }

        TileBase[] tiles = new TileBase[positions.Count];
        System.Array.Fill(tiles, tile);
        ground.SetTiles(positions.ToArray(), tiles);
        if (withWalls)
        {
            NewWalls(gridObject.transform);
        }

        WorldChunk chunk = gridObject.AddComponent<WorldChunk>();
        chunk.Configure(chunkWorld, coords, null);
        gridObject.AddComponent<TerrainBlendRenderer>().Configure(style, new TerrainBlendRenderer.Layer(ground, grass));
        gridObject.SetActive(true);
        return chunk;
    }

    /// <summary>
    /// A walls data tilemap under <paramref name="parent"/> with its module and half-cell render tilemap, painting the
    /// project's walls rule tile.
    /// </summary>
    private DualGridTilemapModule NewWalls(Transform parent)
    {
        GameObject data = new("Walls", typeof(Tilemap));
        data.transform.SetParent(parent, false);
        GameObject render = new("RenderTilemap", typeof(Tilemap));
        render.transform.SetParent(data.transform, false);
        render.transform.localPosition = new Vector3(-0.5f, -0.5f, 0f);
        DualGridTilemapModule module = data.AddComponent<DualGridTilemapModule>();
        module.RenderTile = walls;
        return module;
    }

    /// <summary>
    /// A new grid object, destroyed at tear down.
    /// </summary>
    private GameObject NewGrid(string gridName)
    {
        return Track(new GameObject(gridName, typeof(Grid)));
    }

    /// <summary>
    /// The cells of the ground sources of <paramref name="chunk"/>'s skirt.
    /// </summary>
    private static List<RectInt> StripsOf(WorldChunk chunk)
    {
        List<RectInt> strips = new();
        foreach (TerrainSkirtSource source in chunk.Terrain.Skirt)
        {
            if (!source.IsRaised)
            {
                strips.Add(source.Cells);
            }
        }

        return strips;
    }

    /// <summary>
    /// A sprite's name, or "nothing".
    /// </summary>
    private static string Name(Sprite sprite)
    {
        return sprite != null ? sprite.name : "nothing";
    }

    /// <summary>
    /// Records <paramref name="target"/> for tear down and returns it.
    /// </summary>
    private T Track<T>(T target) where T : Object
    {
        created.Add(target);
        return target;
    }
}
