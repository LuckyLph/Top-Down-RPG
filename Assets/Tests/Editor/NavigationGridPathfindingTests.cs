using NUnit.Framework;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public class NavigationGridPathfindingTests
{
    private GameObject root;
    private NavigationGrid2D navigationGrid;
    private Tile fillTile;
    private Tile blockTile;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }

        if (fillTile != null)
        {
            Object.DestroyImmediate(fillTile);
        }

        if (blockTile != null)
        {
            Object.DestroyImmediate(blockTile);
        }
    }

    [Test]
    public void AStar_ReturnsShortestPath_OnSimpleBlockedGrid()
    {
        SetupNavigationGrid(addGapInBarrier: true);

        PathRequest request = new(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false);
        PathResult result = navigationGrid.Pathfinder.FindPath(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsPartial, Is.False);
        Assert.That(result.Cells.Count, Is.LessThan(9));
        Assert.That(result.Cells.Any(cell => cell.x == 2 && cell.y == 2), Is.True);
    }

    [Test]
    public void AStar_ReturnsPartial_WhenGoalUnreachableAndPartialAllowed()
    {
        SetupNavigationGrid(addGapInBarrier: false);

        PathRequest strictRequest = new(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false);
        PathResult strictResult = navigationGrid.Pathfinder.FindPath(strictRequest);
        Assert.That(strictResult.Success, Is.False);

        PathRequest partialRequest = new(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: true);
        PathResult partialResult = navigationGrid.Pathfinder.FindPath(partialRequest);

        Assert.That(partialResult.Success, Is.True);
        Assert.That(partialResult.IsPartial, Is.True);
        Assert.That(partialResult.Cells.Count, Is.GreaterThan(0));
        Assert.That(partialResult.Cells[^1].x, Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void AStar_DoesNotCutCorners_OnDiagonal()
    {
        root = new GameObject("NavGridCornerTestRoot");
        root.AddComponent<Grid>();

        GameObject dataObject = new("DataTilemap");
        dataObject.transform.SetParent(root.transform);
        Tilemap dataTilemap = dataObject.AddComponent<Tilemap>();
        dataObject.AddComponent<TilemapRenderer>();

        GameObject collisionObject = new("CollisionTilemap");
        collisionObject.transform.SetParent(root.transform);
        Tilemap collisionTilemap = collisionObject.AddComponent<Tilemap>();
        collisionObject.AddComponent<TilemapRenderer>();

        fillTile = ScriptableObject.CreateInstance<Tile>();
        blockTile = ScriptableObject.CreateInstance<Tile>();

        dataTilemap.SetTile(new Vector3Int(0, 0, 0), fillTile);
        dataTilemap.SetTile(new Vector3Int(1, 0, 0), fillTile);
        dataTilemap.SetTile(new Vector3Int(0, 1, 0), fillTile);
        dataTilemap.SetTile(new Vector3Int(1, 1, 0), fillTile);

        collisionTilemap.SetTile(new Vector3Int(1, 0, 0), blockTile);
        collisionTilemap.SetTile(new Vector3Int(0, 1, 0), blockTile);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.Configure(dataTilemap, collisionTilemap);
        navigationGrid.BuildGrid();

        PathRequest request = new(new Vector3Int(0, 0, 0), new Vector3Int(1, 1, 0), allowPartial: false);
        PathResult result = navigationGrid.Pathfinder.FindPath(request);

        Assert.That(result.Success, Is.False);
    }

    private void SetupNavigationGrid(bool addGapInBarrier)
    {
        root = new GameObject("NavGridTestRoot");
        root.AddComponent<Grid>();

        GameObject dataObject = new("DataTilemap");
        dataObject.transform.SetParent(root.transform);
        Tilemap dataTilemap = dataObject.AddComponent<Tilemap>();
        dataObject.AddComponent<TilemapRenderer>();

        GameObject collisionObject = new("CollisionTilemap");
        collisionObject.transform.SetParent(root.transform);
        Tilemap collisionTilemap = collisionObject.AddComponent<Tilemap>();
        collisionObject.AddComponent<TilemapRenderer>();

        fillTile = ScriptableObject.CreateInstance<Tile>();
        blockTile = ScriptableObject.CreateInstance<Tile>();

        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                dataTilemap.SetTile(new Vector3Int(x, y, 0), fillTile);
            }
        }

        for (int y = 0; y < 5; y++)
        {
            if (addGapInBarrier && y == 2)
            {
                continue;
            }

            collisionTilemap.SetTile(new Vector3Int(2, y, 0), blockTile);
        }

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.Configure(dataTilemap, collisionTilemap);
        navigationGrid.BuildGrid();
    }
}
