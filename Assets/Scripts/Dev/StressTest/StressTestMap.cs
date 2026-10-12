using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;
using Random = System.Random;

/// <summary>
/// Dev-only generator for the stress test area: fills a rectangle of its plain ground tilemap with the ground tile (the
/// area's <see cref="TerrainBlendRenderer"/> draws it), rings it with a dual-grid wall border, adds walls in the chosen
/// <see cref="WallLayout"/>, keeps the centre clear and rebuilds the navigation grid. Runs early so the map exists
/// before anything spawns on it.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class StressTestMap : MonoBehaviour
{
    /// <summary>
    /// How interior walls are laid out inside the border.
    /// </summary>
    public enum WallLayout
    {
        Open,
        Scattered,
        Corridors
    }

    [Tooltip("Plain ground data tilemap, a layer of the area's Terrain Blend Renderer.")]
    [SerializeField] private Tilemap groundTilemap;
    [Tooltip("Plain tile written into every ground cell (Tile_Grass).")]
    [SerializeField] private TileBase groundTile;
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

    /// <summary>
    /// Generates the map as soon as the area loads.
    /// </summary>
    private void Awake()
    {
        Generate();
    }

    /// <summary>
    /// Clears and repaints the ground and wall layers from the current settings and seed, rebuilds the
    /// <see cref="NavigationGrid2D"/> and logs the size, wall count and time taken. Logs an error and does nothing when
    /// a reference is missing.
    /// </summary>
    public void Generate()
    {
        if (groundTilemap == null || groundTile == null || walls == null || navigationGrid == null)
        {
            Debug.LogError($"{name}: {nameof(StressTestMap)} needs its ground tilemap, ground tile, walls and navigation grid.", this);
            return;
        }

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

        PaintGround(groundCells);
        PaintWalls(new List<Vector3Int>(wallCells));

        navigationGrid.BuildGrid();

        Debug.Log($"[StressTest] Generated a {width}x{height} {layout} map with {wallCells.Count} wall cells " +
            $"in {stopwatch.ElapsedMilliseconds} ms.", this);
    }

    /// <summary>
    /// Wipes the ground tilemap and fills <paramref name="cells"/> with the ground tile in one batch.
    /// </summary>
    private void PaintGround(List<Vector3Int> cells)
    {
        TileBase[] tiles = new TileBase[cells.Count];
        System.Array.Fill(tiles, groundTile);
        groundTilemap.ClearAllTiles();
        groundTilemap.SetTiles(cells.ToArray(), tiles);
    }

    /// <summary>
    /// Wipes both tilemaps of the walls and paints <paramref name="cells"/> as their data tiles.
    /// </summary>
    private void PaintWalls(List<Vector3Int> cells)
    {
        walls.DataTilemap.ClearAllTiles();
        walls.RenderTilemap.ClearAllTiles();
        walls.SetDataTiles(cells);
    }

    /// <summary>
    /// Adds a one-cell wall ring just outside the ground rectangle that starts at <paramref name="minX"/>,
    /// <paramref name="minY"/>.
    /// </summary>
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

    /// <summary>
    /// Adds seeded random horizontal and vertical wall segments, enough to cover roughly the configured wall
    /// density, clipped to the ground rectangle.
    /// </summary>
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

    /// <summary>
    /// Adds full-height wall columns every corridor spacing, each with a two-cell gap at a seeded random height.
    /// </summary>
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
