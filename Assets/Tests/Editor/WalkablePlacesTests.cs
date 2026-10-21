using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for the rule that moves a saved place a player can no longer stand on (Docs/WorldGeneration.md: Runtime
/// changes), <see cref="WalkablePlaces.Find"/>, on a standalone navigation grid of 40 x 40 cells of ground with a lake
/// of 13 x 13 cells of water in it and a wall across the north-east, under a world at the origin whose entries are the
/// default <c>start</c> and the waystones <c>west</c> and <c>east</c>: a place on walkable ground stays; one on water or
/// in the wall moves to the centre of the walkable cell nearest it by Manhattan distance, within the radius; with none
/// within the radius it goes to the player's nearest discovered waystone, the default entry counting; with no entry at
/// all it stays.
/// </summary>
public class WalkablePlacesTests
{
    private const int Size = 40;
    private const int LakeMin = 10;
    private const int LakeMax = 22;
    private const int WallRow = 30;
    private static readonly Vector2 LakeMiddle = new(16.5f, 16.5f);

    private readonly List<Object> created = new();
    private NavigationGrid2D grid;
    private TerrainMovementProfile2D profile;
    private WorldDefinition world;

    /// <summary>
    /// Builds the grid (ground, the lake on a water layer above it, the wall on a blocking tilemap), a profile that
    /// walks the ground but not the water, and the world with its three entries.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        GameObject root = Track(new GameObject("WalkablePlacesGrid"));
        root.AddComponent<Grid>();
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        TerrainType2D ground = Track(TerrainType2D.Create("ground"));
        TerrainType2D water = Track(TerrainType2D.Create("water"));
        Tilemap groundMap = CreateTilemap(root, "Ground");
        Tilemap waterMap = CreateTilemap(root, "Water");
        Tilemap wallMap = CreateTilemap(root, "Walls");
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                groundMap.SetTile(new Vector3Int(x, y, 0), tile);
                if (x >= LakeMin && x <= LakeMax && y >= LakeMin && y <= LakeMax)
                {
                    waterMap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        for (int x = 25; x <= 35; x++)
        {
            wallMap.SetTile(new Vector3Int(x, WallRow, 0), tile);
        }

        groundMap.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(ground);
        waterMap.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(water, 1);
        grid = root.AddComponent<NavigationGrid2D>();
        grid.ConfigureBlockingTilemaps(wallMap);
        grid.BuildGrid();
        profile = Track(TerrainMovementProfile2D.Create(true, 10,
            new TerrainMovementProfile2D.TerrainRule(ground, true, 10),
            new TerrainMovementProfile2D.TerrainRule(water, false, 10)));
        world = Track(WorldDefinition.Create(
            "WalkablePlaces",
            Vector2Int.zero,
            Vector2Int.one,
            new[] { Vector2Int.zero },
            new WorldDefinition.Entry("start", new Vector2(35.5f, 5.5f), true),
            new WorldDefinition.Entry("west", new Vector2(4.5f, 16.5f), true),
            new WorldDefinition.Entry("east", new Vector2(36.5f, 20.5f), true)));
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
    /// A place on walkable ground stays where it is.
    /// </summary>
    [Test]
    public void APlaceOnWalkableGround_Stays()
    {
        Vector2 place = new(5.25f, 30.75f);

        Assert.That(Find(place, 16, null, out Vector2 moved, out WorldDefinition.Entry waystone), Is.EqualTo(WalkablePlaces.Outcome.Walkable));
        Assert.That(moved, Is.EqualTo(place));
        Assert.That(waystone, Is.Null);
    }

    /// <summary>
    /// A place in the middle of the lake moves to the centre of a walkable cell 7 cells away by Manhattan distance, the
    /// nearest: every cell nearer is water. A place in the wall moves to a cell beside it.
    /// </summary>
    [Test]
    public void APlaceOnWaterOrInAWall_MovesToTheNearestWalkableCell()
    {
        Assert.That(Find(LakeMiddle, 16, null, out Vector2 moved, out WorldDefinition.Entry waystone), Is.EqualTo(WalkablePlaces.Outcome.NearestCell));
        Assert.That(waystone, Is.Null);
        AssertNearestWalkable(LakeMiddle, moved, 7);

        Vector2 inTheWall = new(30.5f, WallRow + 0.5f);
        Assert.That(grid.IsCellWalkable(grid.WorldToCell(inTheWall), profile), Is.False, "the wall blocks its cells");
        Assert.That(Find(inTheWall, 16, null, out moved, out _), Is.EqualTo(WalkablePlaces.Outcome.NearestCell));
        AssertNearestWalkable(inTheWall, moved, 1);
    }

    /// <summary>
    /// With no walkable cell within the radius, a place in the lake goes to the nearest waystone the player discovered,
    /// in a straight line: the default entry when it discovered none, <c>west</c> once it discovered that one, and not
    /// for a discovery of another world's <c>west</c>.
    /// </summary>
    [Test]
    public void APlaceWithNoWalkableCellWithinTheRadius_GoesToTheNearestDiscoveredWaystone()
    {
        Assert.That(Find(LakeMiddle, 6, null, out Vector2 moved, out WorldDefinition.Entry waystone), Is.EqualTo(WalkablePlaces.Outcome.Waystone));
        Assert.That(waystone.Name, Is.EqualTo("start"), "the default entry counts as discovered");
        Assert.That(moved, Is.EqualTo(world.EntryPosition("start")));

        List<GameSaveData.Waystone> discovered = new() { new GameSaveData.Waystone("another-world", "west") };
        Assert.That(Find(LakeMiddle, 6, discovered, out _, out waystone), Is.EqualTo(WalkablePlaces.Outcome.Waystone));
        Assert.That(waystone.Name, Is.EqualTo("start"), "another world's discovery does not count");

        discovered.Add(new GameSaveData.Waystone(world.WorldId, "west"));
        Assert.That(Find(LakeMiddle, 6, discovered, out moved, out waystone), Is.EqualTo(WalkablePlaces.Outcome.Waystone));
        Assert.That(waystone.Name, Is.EqualTo("west"));
        Assert.That(moved, Is.EqualTo(world.EntryPosition("west")));
    }

    /// <summary>
    /// With no walkable cell within the radius and a world without entries, the place stays.
    /// </summary>
    [Test]
    public void APlaceWithNowhereToGo_Stays()
    {
        world = Track(WorldDefinition.Create("NoEntries", Vector2Int.zero, Vector2Int.one, new[] { Vector2Int.zero }));

        Assert.That(Find(LakeMiddle, 6, null, out Vector2 moved, out WorldDefinition.Entry waystone), Is.EqualTo(WalkablePlaces.Outcome.Nowhere));
        Assert.That(moved, Is.EqualTo(LakeMiddle));
        Assert.That(waystone, Is.Null);
    }

    /// <summary>
    /// Runs the rule on the test's grid, profile and world.
    /// </summary>
    private WalkablePlaces.Outcome Find(Vector2 place, int searchCells, IReadOnlyList<GameSaveData.Waystone> discovered, out Vector2 moved, out WorldDefinition.Entry waystone)
    {
        return WalkablePlaces.Find(grid, profile, searchCells, world, place, discovered ?? new List<GameSaveData.Waystone>(), out moved, out waystone);
    }

    /// <summary>
    /// Asserts <paramref name="moved"/> is the centre of a walkable cell <paramref name="distance"/> cells from the
    /// cell of <paramref name="place"/> by Manhattan distance, and that no cell nearer is walkable.
    /// </summary>
    private void AssertNearestWalkable(Vector2 place, Vector2 moved, int distance)
    {
        Vector3Int from = grid.WorldToCell(place);
        Vector3Int to = grid.WorldToCell(moved);
        Assert.That(moved, Is.EqualTo((Vector2)grid.CellToWorldCenter(to)), "the centre of its cell");
        Assert.That(grid.IsCellWalkable(to, profile), Is.True, "a walkable cell");
        Assert.That(Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y), Is.EqualTo(distance), "its Manhattan distance");
        for (int dx = -distance; dx <= distance; dx++)
        {
            for (int dy = -distance; dy <= distance; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) < distance)
                {
                    Assert.That(grid.IsCellWalkable(from + new Vector3Int(dx, dy, 0), profile), Is.False, $"no nearer walkable cell ({dx}, {dy})");
                }
            }
        }
    }

    /// <summary>
    /// Creates a tilemap named <paramref name="name"/> under <paramref name="root"/>.
    /// </summary>
    private static Tilemap CreateTilemap(GameObject root, string name)
    {
        GameObject tilemapObject = new(name);
        tilemapObject.transform.SetParent(root.transform);
        Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
        tilemapObject.AddComponent<TilemapRenderer>();
        return tilemap;
    }

    /// <summary>
    /// Keeps <paramref name="item"/> for the tear-down and returns it.
    /// </summary>
    private T Track<T>(T item) where T : Object
    {
        created.Add(item);
        return item;
    }
}
