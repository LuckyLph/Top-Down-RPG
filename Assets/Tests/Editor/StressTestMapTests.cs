using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="StressTestMap"/> on a grid built in the test like the stress world's chunks: a plain ground data
/// tilemap with a navigation source, dual-grid walls of a rule tile made for the test, and a navigation grid blocked by
/// the walls, standalone or a chunk's.
/// </summary>
public class StressTestMapTests
{
    private const int Size = 8;
    private const int ChunkMapWidth = 128;

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Generating an open map on a standalone grid fills exactly its rectangle from cell (0, 0) of the plain ground
    /// tilemap with the ground tile, walls its outer ring through the walls' module (data tiles and render tiles), and
    /// leaves the ground inside walkable on the rebuilt navigation grid.
    /// </summary>
    [Test]
    public void Generate_FillsThePlainGroundWithItsTile_AndWallsTheBorder()
    {
        (StressTestMap map, Tilemap ground, Tile groundTile, DualGridTilemapModule walls, DualGridRuleTile wallTile, NavigationGrid2D navigation) = CreateMap(null, Size, Size);

        map.Generate();

        int filled = 0;
        foreach (Vector3Int cell in ground.cellBounds.allPositionsWithin)
        {
            filled += ground.HasTile(cell) ? 1 : 0;
        }

        Assert.That(filled, Is.EqualTo(Size * Size), "Exactly the rectangle is ground.");
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Assert.That(ground.GetTile(new Vector3Int(x, y, 0)), Is.SameAs(groundTile), $"({x}, {y})");
            }
        }

        Assert.That(walls.DataTilemap.GetTile(Vector3Int.zero), Is.SameAs(wallTile.DataTile), "The outer ring is a wall.");
        Assert.That(walls.DataTilemap.GetTile(new Vector3Int(Size - 1, Size / 2, 0)), Is.SameAs(wallTile.DataTile));
        Assert.That(walls.RenderTilemap.HasTile(DualGridUtils.GetRenderTilePosition(Vector3Int.zero, 3)), Is.True, "The walls' render tilemap draws it.");
        Assert.That(navigation.IsCellWalkable(new Vector3Int(Size / 2, Size / 2, 0)), Is.True, "The middle is open ground.");
        Assert.That(navigation.IsCellWalkable(new Vector3Int(1, 1, 0)), Is.True, "The ground inside the ring is open.");
        Assert.That(navigation.IsCellWalkable(Vector3Int.zero), Is.False, "The wall blocks its cell.");
    }

    /// <summary>
    /// On a chunk's grid the map paints only that chunk's cells of the map, its walls included, and leaves the other
    /// chunks' cells to their own grids.
    /// </summary>
    [Test]
    public void Generate_OnAChunk_PaintsOnlyTheChunksCells()
    {
        WorldDefinition world = Track(WorldDefinition.Create("StressMap", Vector2Int.zero, new Vector2Int(2, 1), new[] { Vector2Int.zero, Vector2Int.right }));
        (StressTestMap map, Tilemap ground, _, DualGridTilemapModule walls, _, _) = CreateMap(world, ChunkMapWidth, ChunkCoords.Size);

        map.Generate();

        RectInt cells = ChunkCoords.Bounds(Vector2Int.right);
        int filled = 0;
        foreach (Vector3Int cell in ground.cellBounds.allPositionsWithin)
        {
            if (ground.HasTile(cell))
            {
                filled++;
                Assert.That(cells.Contains(new Vector2Int(cell.x, cell.y)), Is.True, $"{cell} lies in chunk (1, 0).");
            }
        }

        Assert.That(filled, Is.EqualTo(ChunkCoords.Size * ChunkCoords.Size));
        Assert.That(walls.DataTilemap.HasTile(new Vector3Int(ChunkMapWidth - 1, 0, 0)), Is.True, "The map's corner in this chunk is walled.");
        Assert.That(walls.DataTilemap.HasTile(Vector3Int.zero), Is.False, "Chunk (0, 0)'s corner is left to its own grid.");
    }

    /// <summary>
    /// A grid set up like a stress chunk's (a <see cref="WorldChunk"/> for chunk (1, 0) of <paramref name="world"/>, or
    /// standalone when null) with a <see cref="StressTestMap"/> of an open map <paramref name="width"/> x
    /// <paramref name="height"/> cells, and its parts.
    /// </summary>
    private (StressTestMap, Tilemap, Tile, DualGridTilemapModule, DualGridRuleTile, NavigationGrid2D) CreateMap(WorldDefinition world, int width, int height)
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        Tilemap ground = new GameObject("GrassTilemap", typeof(Tilemap)).GetComponent<Tilemap>();
        ground.transform.SetParent(grid.transform, false);
        ground.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(Track(TerrainType2D.Create("grass")));
        Tile groundTile = Track(ScriptableObject.CreateInstance<Tile>());
        Tilemap wallData = new GameObject("CollisionTilemap", typeof(Tilemap)).GetComponent<Tilemap>();
        wallData.transform.SetParent(grid.transform, false);
        GameObject wallRender = new("RenderTilemap", typeof(Tilemap));
        wallRender.transform.SetParent(wallData.transform, false);
        DualGridRuleTile wallTile = Track(ScriptableObject.CreateInstance<DualGridRuleTile>());
        Track(wallTile.RefreshDataTile());
        DualGridTilemapModule walls = wallData.gameObject.AddComponent<DualGridTilemapModule>();
        walls.RenderTile = wallTile;
        NavigationGrid2D navigation = grid.gameObject.AddComponent<NavigationGrid2D>();
        navigation.ConfigureBlockingTilemaps(wallData);
        if (world != null)
        {
            grid.gameObject.AddComponent<WorldChunk>().Configure(world, Vector2Int.right, null);
        }

        StressTestMap map = grid.gameObject.AddComponent<StressTestMap>();
        SerializedObject settings = new(map);
        settings.FindProperty("groundTilemap").objectReferenceValue = ground;
        settings.FindProperty("groundTile").objectReferenceValue = groundTile;
        settings.FindProperty("walls").objectReferenceValue = walls;
        settings.FindProperty("navigationGrid").objectReferenceValue = navigation;
        settings.FindProperty("width").intValue = width;
        settings.FindProperty("height").intValue = height;
        settings.FindProperty("layout").enumValueIndex = (int)StressTestMap.WallLayout.Open;
        settings.FindProperty("clearRadius").intValue = 1;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return (map, ground, groundTile, walls, wallTile, navigation);
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
