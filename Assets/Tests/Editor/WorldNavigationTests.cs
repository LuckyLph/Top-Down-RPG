using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.Tilemaps;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// Tests for <see cref="WorldNavigation"/>: pages built from chunk grids made in code, joined across their borders.
/// Paths, steps, costs, cliff sides, cliff faces and regions across borders match one grid over the same cells; a cell
/// beside an unloaded chunk counts as beside a blocked one; regions join and split as pages load and unload; partial
/// paths stop at the loaded edge; loading or unloading a page makes a follower check its path again; tile edits on a
/// border reach the neighbouring page; worlds share one pool; and searches do not allocate once warm.
/// </summary>
public class WorldNavigationTests
{
    private const int Size = ChunkCoords.Size * 2;
    private const int EdgeCost = 5;

    private readonly List<Object> created = new();
    private readonly List<WorldNavigation> navigations = new();
    private Tile groundTile;
    private Tile waterTile;
    private Tile wallTile;
    private RaisedGroundTile raisedTile;
    private RaisedGroundTile rampTile;
    private TerrainType2D groundTerrain;
    private TerrainType2D waterTerrain;
    private TerrainMovementProfile2D landProfile;
    private TerrainStyle style;
    private Layout layout;

    /// <summary>
    /// The cells of a test world, in world cells from its origin.
    /// </summary>
    private sealed class Layout
    {
        public readonly HashSet<Vector2Int> Ground = new();
        public readonly HashSet<Vector2Int> Water = new();
        public readonly HashSet<Vector2Int> Walls = new();
        public readonly HashSet<Vector2Int> Raised = new();
        public readonly HashSet<Vector2Int> Ramps = new();
    }

    /// <summary>
    /// Creates the tiles, terrain types, a land profile with an edge cost that cannot walk water, and a terrain style
    /// whose cliff face covers two rows.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        groundTile = Track(ScriptableObject.CreateInstance<Tile>());
        waterTile = Track(ScriptableObject.CreateInstance<Tile>());
        wallTile = Track(ScriptableObject.CreateInstance<Tile>());
        raisedTile = Track(RaisedGroundTile.Create(false));
        rampTile = Track(RaisedGroundTile.Create(true));
        groundTerrain = Track(TerrainType2D.Create("ground"));
        waterTerrain = Track(TerrainType2D.Create("water"));
        landProfile = Track(TerrainMovementProfile2D.Create(true, 10, EdgeCost,
            new TerrainMovementProfile2D.TerrainRule(groundTerrain, true, 10),
            new TerrainMovementProfile2D.TerrainRule(waterTerrain, false, 10)));
        style = Track(TerrainStyle.Create(null));
        SerializedObject settings = new(style);
        settings.FindProperty("cliffHeight").floatValue = 64f;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Takes every page out of its world and destroys every object and asset the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (WorldNavigation navigation in navigations)
        {
            navigation.Dispose();
        }

        navigations.Clear();
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
    /// A 2 x 2 chunk world with ground holes, water and walls across both borders and a plateau with ramps across the
    /// corner where the four chunks meet reads exactly like one grid over the same cells: walkability, what blocks each
    /// cell, its cliff sides and step costs for both profiles, connectivity, and the cells of strict and partial paths.
    /// </summary>
    [Test]
    public void AWorldOfPages_MatchesOneGridOverTheSameCells()
    {
        layout = RandomLayout(7);
        NavigationGrid2D whole = CreateGrid("Whole", null);
        whole.BuildGrid();
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1));

        TerrainMovementProfile2D[] profiles = { null, landProfile };
        foreach (TerrainMovementProfile2D profile in profiles)
        {
            for (int x = -1; x <= Size; x++)
            {
                for (int y = -1; y <= Size; y++)
                {
                    Vector3Int cell = new(x, y, 0);
                    ExpectSame(world.IsCellWalkable(cell, profile), whole.IsCellWalkable(cell, profile), "walkable", cell);
                    ExpectSame(world.GetBlocker(cell, profile), whole.GetBlocker(cell, profile), "blocker", cell);
                    ExpectSame(world.GetCliffSides(cell), whole.GetCliffSides(cell), "cliff sides", cell);
                    ExpectSame(world.MovementCost(cell, cell + Vector3Int.right, profile), whole.MovementCost(cell, cell + Vector3Int.right, profile), "step right", cell);
                    ExpectSame(world.MovementCost(cell, cell + Vector3Int.up, profile), whole.MovementCost(cell, cell + Vector3Int.up, profile), "step up", cell);
                    ExpectSame(world.MovementCost(cell, cell + new Vector3Int(1, 1, 0), profile), whole.MovementCost(cell, cell + new Vector3Int(1, 1, 0), profile), "diagonal step", cell);
                }
            }
        }

        Random random = new(11);
        List<Vector3Int> worldCells = new();
        List<Vector3Int> wholeCells = new();
        int crossing = 0;
        for (int i = 0; i < 300; i++)
        {
            TerrainMovementProfile2D profile = profiles[i % 2];
            Vector3Int from = new(random.Next(Size), random.Next(Size), 0);
            Vector3Int to = new(random.Next(Size), random.Next(Size), 0);
            bool partial = i % 3 == 0;
            Assert.That(world.AreCellsConnected(from, to, profile), Is.EqualTo(whole.AreCellsConnected(from, to, profile)), $"connected {from} {to}");

            PathResult worldPath = world.Pathfinder.FindPath(new PathRequest(from, to, partial, profile), worldCells);
            PathResult wholePath = whole.Pathfinder.FindPath(new PathRequest(from, to, partial, profile), wholeCells);
            Assert.That(worldPath.Success, Is.EqualTo(wholePath.Success), $"path {from} to {to}");
            Assert.That(worldPath.IsPartial, Is.EqualTo(wholePath.IsPartial), $"path {from} to {to}");
            Assert.That(worldCells, Is.EqualTo(wholeCells), $"the cells of the path from {from} to {to}");
            if (worldPath.Success && ChunkCoords.ChunkOf((Vector2Int)worldCells[0]) != ChunkCoords.ChunkOf((Vector2Int)worldCells[^1]))
            {
                crossing++;
            }
        }

        Assert.That(crossing, Is.GreaterThan(50), "Many of the paths cross a border.");
    }

    /// <summary>
    /// A step across a border reads the neighbouring page: a path from one chunk into the next succeeds through the
    /// border cells, the straight line across costs what its steps do, and both ends are connected.
    /// </summary>
    [Test]
    public void StepsCrossTheBorder_IntoTheNeighbouringPage()
    {
        layout = OpenLayout();
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0), new Vector2Int(1, 0));
        Vector3Int west = new(60, 10, 0);
        Vector3Int east = new(68, 10, 0);

        PathResult path = world.Pathfinder.FindPath(new PathRequest(west, east, false));

        Assert.That(path.Success, Is.True);
        Assert.That(path.Cells, Has.Member(new Vector3Int(63, 10, 0)).And.Member(new Vector3Int(64, 10, 0)));
        Assert.That(world.TryGetLineCost(west, east, null, out int cost), Is.True);
        Assert.That(cost, Is.EqualTo(80));
        Assert.That(world.AreCellsConnected(west, east, null), Is.True);
        Assert.That(world.MovementCost(new Vector3Int(63, 10, 0), new Vector3Int(64, 11, 0), null), Is.EqualTo(14));
    }

    /// <summary>
    /// A plateau crossing both borders has no cliff between its own cells on either side of a border, a cliff along its
    /// edge on both sides of it, and its face rows only at its foot: the rows above the horizontal border are not face
    /// while the chunk below is loaded, become face (nothing is known below) when it unloads, and walkable again when it
    /// loads back.
    /// </summary>
    [Test]
    public void TheCliff_IsReadAcrossBorders_AndItsFaceFollowsThePageBelow()
    {
        layout = OpenLayout();
        AddPlateau(layout, 50, 80, 54, 80, 62, 63);
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, 0));

        Assert.That(world.GetCliffSides(new Vector3Int(63, 70, 0)), Is.EqualTo(0), "raised on both sides of the border");
        Assert.That(world.GetCliffSides(new Vector3Int(64, 70, 0)), Is.EqualTo(0));
        Assert.That(world.GetCliffSides(new Vector3Int(70, 63, 0)), Is.EqualTo(0));
        Assert.That(world.GetCliffSides(new Vector3Int(70, 64, 0)), Is.EqualTo(0));
        Assert.That(world.GetCliffSides(new Vector3Int(80, 70, 0)) & NavigationGrid2D.RightSide, Is.Not.Zero);
        Assert.That(world.CrossesCliff(new Vector3Int(80, 70, 0), new Vector3Int(81, 70, 0)), Is.True);
        Assert.That(world.IsCellWalkable(new Vector3Int(70, 64, 0), null), Is.True, "not face: raised ground continues below the border");
        Assert.That(world.IsCellWalkable(new Vector3Int(70, 54, 0), null), Is.False, "the face at the plateau's foot");
        Assert.That(world.IsCellWalkable(new Vector3Int(70, 55, 0), null), Is.False);
        Assert.That(world.AreCellsConnected(new Vector3Int(62, 50, 0), new Vector3Int(70, 75, 0), null), Is.True, "up the ramp and across both borders");

        RemoveChunk(world, new Vector2Int(1, 0));
        Assert.That(world.IsCellWalkable(new Vector3Int(70, 64, 0), null), Is.False, "With nothing loaded below, the plateau's lowest rows are face.");
        Assert.That(world.IsCellWalkable(new Vector3Int(70, 65, 0), null), Is.False);
        Assert.That(world.IsCellWalkable(new Vector3Int(70, 66, 0), null), Is.True);

        AddChunk(world, new Vector2Int(1, 0));
        Assert.That(world.IsCellWalkable(new Vector3Int(70, 64, 0), null), Is.True);
        Assert.That(world.GetCliffSides(new Vector3Int(70, 64, 0)), Is.EqualTo(0));
    }

    /// <summary>
    /// A walkable cell beside an unloaded chunk pays the profile's edge cost, as beside a blocked cell, until that chunk
    /// loads, and again once it unloads.
    /// </summary>
    [Test]
    public void ACellBesideAnUnloadedChunk_PaysTheEdgeCost()
    {
        layout = OpenLayout();
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0));
        Vector3Int inner = new(62, 10, 0);
        Vector3Int border = new(63, 10, 0);

        Assert.That(world.MovementCost(inner, border, landProfile), Is.EqualTo(10 + EdgeCost));
        Assert.That(world.MovementCost(new Vector3Int(61, 10, 0), inner, landProfile), Is.EqualTo(10));

        AddChunk(world, new Vector2Int(1, 0));
        Assert.That(world.MovementCost(inner, border, landProfile), Is.EqualTo(10));

        RemoveChunk(world, new Vector2Int(1, 0));
        Assert.That(world.MovementCost(inner, border, landProfile), Is.EqualTo(10 + EdgeCost));
    }

    /// <summary>
    /// Two cells of one chunk, split by a wall across it, are connected only through the chunk above: their regions join
    /// when it loads and split again when it unloads, and strict paths follow.
    /// </summary>
    [Test]
    public void Regions_JoinAndSplit_AsPagesLoadAndUnload()
    {
        layout = OpenLayout();
        for (int y = 0; y < ChunkCoords.Size; y++)
        {
            layout.Walls.Add(new Vector2Int(32, y));
        }

        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0));
        Vector3Int west = new(10, 10, 0);
        Vector3Int east = new(50, 10, 0);
        Assert.That(world.AreCellsConnected(west, east, null), Is.False);
        Assert.That(world.Pathfinder.FindPath(new PathRequest(west, east, false)).Success, Is.False);

        AddChunk(world, new Vector2Int(0, 1));
        Assert.That(world.AreCellsConnected(west, east, null), Is.True);
        PathResult around = world.Pathfinder.FindPath(new PathRequest(west, east, false));
        Assert.That(around.Success, Is.True);
        Assert.That(around.Cells, Has.Some.Matches<Vector3Int>(cell => cell.y >= ChunkCoords.Size), "The path goes through the chunk above.");

        RemoveChunk(world, new Vector2Int(0, 1));
        Assert.That(world.AreCellsConnected(west, east, null), Is.False);
        Assert.That(world.Pathfinder.FindPath(new PathRequest(west, east, false)).Success, Is.False);
    }

    /// <summary>
    /// Toward a goal in a chunk that is not loaded, a strict request fails and a partial one ends at the loaded edge;
    /// a path follower's last waypoint is that edge cell's centre.
    /// </summary>
    [Test]
    public void APartialPathTowardAnUnloadedChunk_StopsAtTheLoadedEdge()
    {
        layout = OpenLayout();
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0));
        Vector3Int start = new(10, 10, 0);
        Vector3Int goal = new(100, 10, 0);

        Assert.That(world.Pathfinder.FindPath(new PathRequest(start, goal, false)).Success, Is.False);
        PathResult partial = world.Pathfinder.FindPath(new PathRequest(start, goal, true));
        Assert.That(partial.Success, Is.True);
        Assert.That(partial.IsPartial, Is.True);
        Assert.That(partial.ReachedResolvedGoal, Is.False);
        Assert.That(partial.Cells[^1], Is.EqualTo(new Vector3Int(63, 10, 0)));

        PathFollower2D follower = new();
        follower.Configure(world, null, new PathFollowerSettings2D(0.1f, 0.1f));
        Assert.That(follower.BuildPath(new Vector2(10.5f, 10.5f), new Vector2(100.5f, 10.5f), allowPartial: true), Is.True);
        Assert.That(follower.Waypoints[^1], Is.EqualTo(new Vector2(63.5f, 10.5f)));
        Assert.That(follower.CanReach(new Vector2(10.5f, 10.5f), new Vector2(100.5f, 10.5f)), Is.False);
    }

    /// <summary>
    /// Unloading the chunk a path runs into bumps the version, so the follower checks its path on the next tick: a
    /// strict path with no route left clears, a partial one is rebuilt to the loaded edge. Loading it back bumps the
    /// version again.
    /// </summary>
    [Test]
    public void LoadingOrUnloadingAPage_BumpsTheVersion_AndTheFollowerChecksItsPath()
    {
        layout = OpenLayout();
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0), new Vector2Int(1, 0));
        PathFollower2D strict = new();
        PathFollower2D partial = new();
        strict.Configure(world, null, new PathFollowerSettings2D(0.1f, 0.1f));
        partial.Configure(world, null, new PathFollowerSettings2D(0.1f, 0.1f));
        Vector2 start = new(10.5f, 10.5f);
        Vector2 goal = new(100.5f, 20.5f);
        Assert.That(strict.BuildPath(start, goal, allowPartial: false), Is.True);
        Assert.That(partial.BuildPath(start, goal, allowPartial: true), Is.True);
        Assert.That(partial.Waypoints[^1], Is.EqualTo(goal));
        int version = world.Version;

        RemoveChunk(world, new Vector2Int(1, 0));
        Assert.That(world.Version, Is.Not.EqualTo(version));

        Assert.That(strict.Tick(start, 5f, 0.02f), Is.EqualTo(Vector2.zero));
        Assert.That(strict.HasPath, Is.False, "No route to the goal is left.");
        Assert.That(partial.Tick(start, 5f, 0.02f), Is.Not.EqualTo(Vector2.zero));
        Assert.That(partial.Waypoints[^1].x, Is.EqualTo(63.5f), "The partial path now ends at the loaded edge.");

        version = world.Version;
        AddChunk(world, new Vector2Int(1, 0));
        Assert.That(world.Version, Is.Not.EqualTo(version));
    }

    /// <summary>
    /// Unloading every page of a world a path runs over (the world emptying as its last chunk goes) drops the path on
    /// the follower's next tick, partial or not, rather than walking it over unloaded cells.
    /// </summary>
    [Test]
    public void UnloadingEveryPage_DropsTheFollowersPath()
    {
        layout = OpenLayout();
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0), new Vector2Int(1, 0));
        PathFollower2D partial = new();
        partial.Configure(world, null, new PathFollowerSettings2D(0.1f, 0.1f));
        Vector2 start = new(10.5f, 10.5f);
        Assert.That(partial.BuildPath(start, new Vector2(100.5f, 20.5f), allowPartial: true), Is.True);

        RemoveChunk(world, new Vector2Int(1, 0));
        RemoveChunk(world, new Vector2Int(0, 0));

        Assert.That(partial.Tick(start, 5f, 0.02f), Is.EqualTo(Vector2.zero));
        Assert.That(partial.HasPath, Is.False, "No navigation is left to walk the path on.");
    }

    /// <summary>
    /// A tile painted on a chunk's raised tilemap at its border reaches the neighbouring page: lowering the plateau's
    /// edge cell puts a cliff side on the raised cell across the border.
    /// </summary>
    [Test]
    public void ARaisedTileEditOnABorder_ReachesTheNeighbouringPage()
    {
        layout = OpenLayout();
        AddPlateau(layout, 56, 72, 10, 30, 60, 61);
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0), new Vector2Int(1, 0));
        Vector3Int west = new(63, 20, 0);
        Vector3Int east = new(64, 20, 0);
        Assert.That(world.GetCliffSides(east) & NavigationGrid2D.LeftSide, Is.Zero);
        int version = world.Version;

        Tilemap westRaised = RaisedTilemapOf(new Vector2Int(0, 0));
        westRaised.SetTile(west, null);

        Assert.That(world.GetCliffSides(east) & NavigationGrid2D.LeftSide, Is.Not.Zero, "The edit outside play mode reached the page across the border.");
        Assert.That(world.CrossesCliff(west, east), Is.True);
        Assert.That(world.Version, Is.Not.EqualTo(version));
    }

    /// <summary>
    /// Two worlds share one pool, a page per chunk, and each frees its slots when disposed; a pool that runs out of slots
    /// grows, with a warning, and keeps working.
    /// </summary>
    [Test]
    public void WorldsShareOnePool_AndAFullPoolGrows()
    {
        layout = OpenLayout();
        NavigationPagePool pool = new(2, ChunkCoords.Size * ChunkCoords.Size);
        WorldNavigation first = CreateWorld(pool, new Vector2Int(0, 0));
        WorldNavigation second = CreateWorld(pool, new Vector2Int(0, 0));
        Assert.That(pool.UsedSlots, Is.EqualTo(2));

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("is full"));
        AddChunk(first, new Vector2Int(1, 0));
        Assert.That(pool.SlotCount, Is.EqualTo(4));
        Assert.That(first.Pathfinder.FindPath(new PathRequest(new Vector3Int(10, 10, 0), new Vector3Int(100, 10, 0), false)).Success, Is.True);
        Assert.That(second.Pathfinder.FindPath(new PathRequest(new Vector3Int(10, 10, 0), new Vector3Int(60, 60, 0), false)).Success, Is.True);
        Assert.That(second.IsCellWalkable(new Vector3Int(100, 10, 0), null), Is.False, "The second world has no page there.");

        first.Dispose();
        Assert.That(pool.UsedSlots, Is.EqualTo(1));
        Assert.That(first.IsBuilt, Is.False);
        Assert.That(second.Pathfinder.FindPath(new PathRequest(new Vector3Int(10, 10, 0), new Vector3Int(60, 60, 0), false)).Success, Is.True);
    }

    /// <summary>
    /// Once warm, a search across a border into a reused cell list, and the queries a follower makes, allocate nothing.
    /// </summary>
    [Test]
    public void SearchesAcrossBorders_DoNotAllocate_OnceWarm()
    {
        layout = RandomLayout(3);
        WorldNavigation world = CreateWorld(new NavigationPagePool(), new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1));
        PathRequest request = new(new Vector3Int(5, 5, 0), new Vector3Int(120, 120, 0), true, landProfile);
        List<Vector3Int> cells = new();
        world.Pathfinder.FindPath(request, cells);
        Assert.That(cells.Count, Is.GreaterThan(0));

        TestDelegate search = () =>
        {
            world.Pathfinder.FindPath(request, cells);
            world.AreCellsConnected(new Vector3Int(5, 5, 0), new Vector3Int(120, 120, 0), landProfile);
            world.TryGetLineCost(new Vector3Int(60, 60, 0), new Vector3Int(70, 70, 0), landProfile, out _);
            world.TryGetNearestWalkableCell(new Vector3Int(64, 64, 0), landProfile, out _);
        };
        Assert.That(search, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// Open ground over the whole world.
    /// </summary>
    private static Layout OpenLayout()
    {
        Layout open = new();
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                open.Ground.Add(new Vector2Int(x, y));
            }
        }

        return open;
    }

    /// <summary>
    /// Ground with seeded holes and walls, a wall line across each border with gaps, a pond of water across the
    /// vertical border, and a plateau over the corner where the four chunks meet with ramps on both sides of the
    /// vertical border.
    /// </summary>
    private static Layout RandomLayout(int seed)
    {
        Random random = new(seed);
        Layout cells = new();
        AddPlateau(cells, 50, 80, 54, 80, 62, 67);
        cells.Ramps.Add(new Vector2Int(63, 54));
        cells.Ramps.Add(new Vector2Int(66, 54));
        cells.Raised.Remove(new Vector2Int(63, 54));
        cells.Raised.Remove(new Vector2Int(66, 54));
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                Vector2Int cell = new(x, y);
                if (random.NextDouble() >= 0.03)
                {
                    cells.Ground.Add(cell);
                }

                if (x >= 58 && x <= 70 && y >= 20 && y <= 30)
                {
                    cells.Water.Add(cell);
                }

                bool onPlateau = cells.Raised.Contains(cell) || cells.Ramps.Contains(cell);
                if ((y == 40 && x % 16 != 3) || (x == 96 && y % 20 != 7) || (!onPlateau && random.NextDouble() < 0.06))
                {
                    cells.Walls.Add(cell);
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// Adds a plateau over columns <paramref name="xMin"/> to <paramref name="xMax"/> and rows <paramref name="yMin"/> to
    /// <paramref name="yMax"/>, with ramps in columns <paramref name="rampFrom"/> to <paramref name="rampTo"/> of its
    /// bottom row.
    /// </summary>
    private static void AddPlateau(Layout cells, int xMin, int xMax, int yMin, int yMax, int rampFrom, int rampTo)
    {
        for (int x = xMin; x <= xMax; x++)
        {
            for (int y = yMin; y <= yMax; y++)
            {
                bool ramp = y == yMin && x >= rampFrom && x <= rampTo;
                (ramp ? cells.Ramps : cells.Raised).Add(new Vector2Int(x, y));
            }
        }
    }

    /// <summary>
    /// A world navigation in <paramref name="pool"/> with a page for each chunk of <paramref name="chunks"/>, built from
    /// chunk grids holding the layout's cells of their chunk.
    /// </summary>
    private WorldNavigation CreateWorld(NavigationPagePool pool, params Vector2Int[] chunks)
    {
        WorldNavigation world = new(pool);
        navigations.Add(world);
        foreach (Vector2Int coords in chunks)
        {
            AddChunk(world, coords);
        }

        return world;
    }

    /// <summary>
    /// Adds the chunk at <paramref name="coords"/> to <paramref name="world"/>, from a chunk grid made for it.
    /// </summary>
    private void AddChunk(WorldNavigation world, Vector2Int coords)
    {
        NavigationGrid2D grid = CreateGrid($"Chunk {coords}", ChunkCoords.Bounds(coords));
        Assert.That(world.AddChunk(grid, coords), Is.True);
    }

    /// <summary>
    /// Takes the chunk at <paramref name="coords"/> out of <paramref name="world"/>.
    /// </summary>
    private static void RemoveChunk(WorldNavigation world, Vector2Int coords)
    {
        world.RemoveChunk(coords);
    }

    /// <summary>
    /// The raised tilemap of the most recently made grid for the chunk at <paramref name="coords"/>.
    /// </summary>
    private Tilemap RaisedTilemapOf(Vector2Int coords)
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] is GameObject root && root.name == $"Chunk {coords}")
            {
                return root.transform.Find("Raised").GetComponent<Tilemap>();
            }
        }

        throw new InvalidOperationException($"No grid was made for {coords}.");
    }

    /// <summary>
    /// A grid object holding the layout's cells inside <paramref name="owned"/> (all of them when null): ground and water
    /// layers (water on top), walls on a blocking tilemap, raised ground and ramps on a raised tilemap, and a disabled
    /// terrain renderer of the test style drawing it, from which the grid reads the cliff face's height. Not built.
    /// </summary>
    private NavigationGrid2D CreateGrid(string name, RectInt? owned)
    {
        GameObject root = Track(new GameObject(name));
        root.AddComponent<Grid>();
        Tilemap ground = CreateTilemap(root, "Ground", layout.Ground, owned, _ => groundTile);
        ground.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(groundTerrain, 0);
        Tilemap water = CreateTilemap(root, "Water", layout.Water, owned, _ => waterTile);
        water.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(waterTerrain, 1);
        Tilemap walls = CreateTilemap(root, "Walls", layout.Walls, owned, _ => wallTile);
        HashSet<Vector2Int> raisedCells = new(layout.Raised);
        raisedCells.UnionWith(layout.Ramps);
        Tilemap raised = CreateTilemap(root, "Raised", raisedCells, owned, cell => layout.Ramps.Contains(cell) ? rampTile : raisedTile);

        NavigationGrid2D grid = root.AddComponent<NavigationGrid2D>();
        grid.ConfigureBlockingTilemaps(walls);
        grid.ConfigureRaisedTilemap(raised);
        root.SetActive(false);
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, raised);
        renderer.enabled = false;
        root.SetActive(true);
        return grid;
    }

    /// <summary>
    /// A tilemap under <paramref name="root"/> with a tile from <paramref name="tileOf"/> on each of
    /// <paramref name="cells"/> inside <paramref name="owned"/> (all of them when null).
    /// </summary>
    private static Tilemap CreateTilemap(GameObject root, string name, HashSet<Vector2Int> cells, RectInt? owned, Func<Vector2Int, TileBase> tileOf)
    {
        GameObject tilemapObject = new(name);
        tilemapObject.transform.SetParent(root.transform);
        Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
        List<Vector3Int> positions = new();
        List<TileBase> tiles = new();
        foreach (Vector2Int cell in cells)
        {
            if (owned == null || owned.Value.Contains(cell))
            {
                positions.Add(new Vector3Int(cell.x, cell.y, 0));
                tiles.Add(tileOf(cell));
            }
        }

        tilemap.SetTiles(positions.ToArray(), tiles.ToArray());
        return tilemap;
    }

    /// <summary>
    /// Fails, naming <paramref name="what"/> of <paramref name="cell"/>, when the world's value differs from the single
    /// grid's; formats nothing otherwise, since it runs for every cell.
    /// </summary>
    private static void ExpectSame<T>(T fromWorld, T fromGrid, string what, Vector3Int cell)
    {
        if (!EqualityComparer<T>.Default.Equals(fromWorld, fromGrid))
        {
            Assert.Fail($"{what} of {cell}: the world has {fromWorld}, the single grid {fromGrid}");
        }
    }

    private T Track<T>(T made) where T : Object
    {
        created.Add(made);
        return made;
    }
}
