using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="StressTestMap"/> on a grid built in the test like the stress area's: a plain ground data
/// tilemap with a navigation source, dual-grid walls of a rule tile made for the test, and a navigation grid blocked by
/// the walls.
/// </summary>
public class StressTestMapTests
{
    private const int Size = 8;

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
    /// Generating an open map fills exactly its rectangle of the plain ground tilemap with the ground tile, rings it
    /// with walls painted through the walls' module (data tiles and render tiles), and leaves the ground walkable on the
    /// rebuilt navigation grid.
    /// </summary>
    [Test]
    public void Generate_FillsThePlainGroundWithItsTile_AndWallsTheBorder()
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
        StressTestMap map = grid.gameObject.AddComponent<StressTestMap>();
        SerializedObject settings = new(map);
        settings.FindProperty("groundTilemap").objectReferenceValue = ground;
        settings.FindProperty("groundTile").objectReferenceValue = groundTile;
        settings.FindProperty("walls").objectReferenceValue = walls;
        settings.FindProperty("navigationGrid").objectReferenceValue = navigation;
        settings.FindProperty("width").intValue = Size;
        settings.FindProperty("height").intValue = Size;
        settings.FindProperty("layout").enumValueIndex = (int)StressTestMap.WallLayout.Open;
        settings.ApplyModifiedPropertiesWithoutUndo();

        map.Generate();

        int min = -Size / 2;
        int filled = 0;
        foreach (Vector3Int cell in ground.cellBounds.allPositionsWithin)
        {
            filled += ground.HasTile(cell) ? 1 : 0;
        }

        Assert.That(filled, Is.EqualTo(Size * Size), "Exactly the rectangle is ground.");
        for (int y = min; y < min + Size; y++)
        {
            for (int x = min; x < min + Size; x++)
            {
                Assert.That(ground.GetTile(new Vector3Int(x, y, 0)), Is.SameAs(groundTile), $"({x}, {y})");
            }
        }

        Vector3Int corner = new(min - 1, min - 1, 0);
        Assert.That(wallData.GetTile(corner), Is.SameAs(wallTile.DataTile), "The border is a wall.");
        Assert.That(walls.RenderTilemap.HasTile(DualGridUtils.GetRenderTilePosition(corner, 3)), Is.True, "The walls' render tilemap draws it.");
        Assert.That(navigation.IsCellWalkable(Vector3Int.zero), Is.True);
        Assert.That(navigation.IsCellWalkable(new Vector3Int(min, min, 0)), Is.True);
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
