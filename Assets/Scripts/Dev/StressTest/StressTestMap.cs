using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;
using Random = System.Random;

[DefaultExecutionOrder(-1000)]
public class StressTestMap : MonoBehaviour
{
    public enum WallLayout
    {
        Open,
        Scattered,
        Corridors
    }

    [SerializeField] private DualGridTilemapModule ground;
    [Tooltip("Dual-grid walls: the data tilemap is the collision tilemap, its render tilemap draws them.")]
    [SerializeField] private DualGridTilemapModule walls;
    [SerializeField] private NavigationGrid2D navigationGrid;

    [Header("Layout")]
    [SerializeField, Min(8)] private int width = 128;
    [SerializeField, Min(8)] private int height = 128;
    [SerializeField] private WallLayout layout = WallLayout.Scattered;
    [Tooltip("Scattered layout: roughly the fraction of cells covered by wall segments.")]
    [SerializeField, Range(0f, 0.4f)] private float wallDensity = 0.1f;
    [Tooltip("Corridors layout: columns between walls.")]
    [SerializeField, Min(3)] private int corridorSpacing = 8;
    [SerializeField, Min(0)] private int clearRadius = 4;
    [SerializeField] private int seed = 1;

    public int Width => width;
    public int Height => height;

    private void Awake()
    {
        Generate();
    }

    public void Generate()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        int minX = -width / 2;
        int minY = -height / 2;

        List<Vector3Int> groundCells = new(width * height);
        for (int x = minX; x < minX + width; x++)
        {
            for (int y = minY; y < minY + height; y++)
            {
                groundCells.Add(new Vector3Int(x, y, 0));
            }
        }

        HashSet<Vector3Int> wallCells = new();
        AddBorder(wallCells, minX, minY);
        switch (layout)
        {
            case WallLayout.Scattered:
                AddScatteredWalls(wallCells, minX, minY);
                break;
            case WallLayout.Corridors:
                AddCorridorWalls(wallCells, minX, minY);
                break;
        }

        wallCells.RemoveWhere(cell => Mathf.Abs(cell.x) < clearRadius && Mathf.Abs(cell.y) < clearRadius);

        Paint(ground, groundCells);
        Paint(walls, new List<Vector3Int>(wallCells));

        navigationGrid.BuildGrid();

        Debug.Log($"[StressTest] Generated a {width}x{height} {layout} map with {wallCells.Count} wall cells " +
            $"in {stopwatch.ElapsedMilliseconds} ms.", this);
    }

    private static void Paint(DualGridTilemapModule layer, List<Vector3Int> cells)
    {
        layer.DataTilemap.ClearAllTiles();
        layer.RenderTilemap.ClearAllTiles();
        layer.SetDataTiles(cells);
    }

    private void AddBorder(HashSet<Vector3Int> cells, int minX, int minY)
    {
        for (int x = minX - 1; x <= minX + width; x++)
        {
            cells.Add(new Vector3Int(x, minY - 1, 0));
            cells.Add(new Vector3Int(x, minY + height, 0));
        }

        for (int y = minY - 1; y <= minY + height; y++)
        {
            cells.Add(new Vector3Int(minX - 1, y, 0));
            cells.Add(new Vector3Int(minX + width, y, 0));
        }
    }

    private void AddScatteredWalls(HashSet<Vector3Int> cells, int minX, int minY)
    {
        const int averageSegmentLength = 5;
        Random random = new(seed);
        int segmentCount = Mathf.RoundToInt(width * height * wallDensity / averageSegmentLength);
        for (int i = 0; i < segmentCount; i++)
        {
            Vector3Int start = new(random.Next(minX, minX + width), random.Next(minY, minY + height), 0);
            Vector3Int step = random.Next(2) == 0 ? Vector3Int.right : Vector3Int.up;
            int length = random.Next(2, averageSegmentLength * 2);
            for (int j = 0; j < length; j++)
            {
                Vector3Int cell = start + step * j;
                if (cell.x < minX + width && cell.y < minY + height)
                {
                    cells.Add(cell);
                }
            }
        }
    }

    private void AddCorridorWalls(HashSet<Vector3Int> cells, int minX, int minY)
    {
        Random random = new(seed);
        for (int x = minX + corridorSpacing / 2; x < minX + width; x += corridorSpacing)
        {
            int gapY = random.Next(minY, minY + height - 1);
            for (int y = minY; y < minY + height; y++)
            {
                if (y != gapY && y != gapY + 1)
                {
                    cells.Add(new Vector3Int(x, y, 0));
                }
            }
        }
    }
}
