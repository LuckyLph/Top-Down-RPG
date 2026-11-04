using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Smoke tests of Testlands, the hand-painted test world (Docs/WorldStreaming.md's Phase 5) that keeps the margins the
/// PlayMode tests booting into it rely on (Docs/SceneGeneration.md): booting into it puts the player at its default entry on
/// open ground, every cell within <see cref="SpawnClearRadius"/> of it walkable, with five clear cells in a straight
/// line on one side, and every mob at least <see cref="MobDistanceFromSpawn"/> cells away; a seven-cell wall built two
/// cells right of the player blocks the straight line, and the player walks around it on the world's navigation.
/// </summary>
public class TestlandsPlayModeTests
{
    private const int SpawnClearRadius = 6;
    private const int MobDistanceFromSpawn = 14;
    private const int ClearLine = 5;

    /// <summary>
    /// The player stands at the default entry on open ground with the Clearing's margins around it, but for the one cell
    /// its own waystone's stone blocks, two cells north of it (Docs/KnownGaps.md, Phase 3).
    /// </summary>
    [UnityTest]
    public IEnumerator Boot_PutsThePlayerOnOpenGround_WithTheClearingsMargins()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();

        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Assert.That(world, Is.Not.Null);
        Assert.That(world.name, Is.EqualTo("Testlands"));
        SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded);
        PlayerController player = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        Assert.That((Vector2)player.transform.position, Is.EqualTo(world.ToWorld(world.DefaultEntry.Position)));
        Assert.That(world.DefaultEntry.Waystone, Is.True);

        INavigation2D navigation = loaded.Navigation;
        TerrainMovementProfile2D profile = player.ControlSettings.MovementProfile;
        Vector3Int spawn = navigation.WorldToCell(player.transform.position);
        Vector3Int stone = StoneCellOf(loaded, navigation, world.DefaultEntry.Name);
        Assert.That(stone, Is.EqualTo(spawn + new Vector3Int(0, 2, 0)), "The entry's stone stands two cells north of it.");
        Assert.That(navigation.IsCellWalkable(stone, profile), Is.False, "The stone blocks its cell.");
        List<Vector3Int> blocked = new();
        for (int dy = -SpawnClearRadius; dy <= SpawnClearRadius; dy++)
        {
            for (int dx = -SpawnClearRadius; dx <= SpawnClearRadius; dx++)
            {
                Vector3Int cell = spawn + new Vector3Int(dx, dy, 0);
                if (dx * dx + dy * dy <= SpawnClearRadius * SpawnClearRadius && cell != stone && !navigation.IsCellWalkable(cell, profile))
                {
                    blocked.Add(cell);
                }
            }
        }

        Assert.That(blocked, Is.Empty, $"Every cell within {SpawnClearRadius} of the spawn but its stone's is open ground.");
        Assert.That(HasClearLine(navigation, profile, spawn), Is.True, $"The spawn has {ClearLine} clear cells in a straight line on one side.");

        int mobs = 0;
        foreach (KeyValuePair<Vector2Int, Scene> chunk in loaded.Chunks)
        {
            foreach (MobSpawnPoint point in SceneQuery.FindAll<MobSpawnPoint>(chunk.Value))
            {
                mobs++;
                Assert.That(Vector2.Distance(point.transform.position, player.transform.position), Is.GreaterThanOrEqualTo(MobDistanceFromSpawn), $"{point.name} of chunk {chunk.Key}");
            }
        }

        Assert.That(mobs, Is.GreaterThan(0), "The chunks around the spawn have mobs.");
        Vector2Int spawnCell = ChunkCoords.CellOf(world.ToLocal(player.transform.position));
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            if (!loaded.Chunks.ContainsKey(chunk.Coords))
            {
                Assert.That(ChunkCoords.SquaredDistance(spawnCell, chunk.Coords), Is.GreaterThanOrEqualTo(MobDistanceFromSpawn * MobDistanceFromSpawn),
                    $"Chunk {chunk.Coords}, not loaded, lies too far for its mobs to come within {MobDistanceFromSpawn} cells.");
            }
        }
    }

    /// <summary>
    /// A seven-cell wall built two cells right of the player, as the controls tests build one, blocks the straight
    /// line to a point behind it, and the player paths around it there.
    /// </summary>
    [UnityTest]
    public IEnumerator AWallBuiltBesideTheSpawn_IsWalkedAround()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();

        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded);
        PlayerController player = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        INavigation2D navigation = loaded.Navigation;
        TerrainMovementProfile2D profile = player.ControlSettings.MovementProfile;
        Vector3Int start = navigation.WorldToCell(player.transform.position);
        Vector2Int chunk = ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(player.transform.position)));
        DualGridTilemapModule walls = SceneQuery.FindFirst<DualGridTilemapModule>(loaded.Chunks[chunk]);
        Assert.That(walls, Is.Not.Null, "The spawn's chunk has walls to build with.");

        List<Vector3Int> wall = new();
        for (int dy = -3; dy <= 3; dy++)
        {
            Vector3Int cell = new(start.x + 2, start.y + dy, 0);
            Assert.That(walls.DataTilemap.HasTile(cell), Is.False);
            wall.Add(cell);
        }

        walls.SetDataTiles(wall);
        yield return null;
        Physics2D.SyncTransforms();

        Vector3Int goal = new(start.x + 4, start.y, 0);
        Assert.That(navigation.IsCellWalkable(goal, profile), Is.True);
        Assert.That(navigation.IsCellWalkable(wall[3], profile), Is.False, "The wall blocks its cells.");
        Assert.That(navigation.TryGetLineCost(start, goal, profile, out _), Is.False, "The wall blocks the straight line.");

        PlayerMotor2D motor = player.GetComponent<PlayerMotor2D>();
        Assert.That(motor.MoveTo(navigation.CellToWorldCenter(goal)), Is.True);
        Assert.That(motor.Follower.Waypoints.Count, Is.GreaterThan(1), "The path turns around the wall.");
        yield return SceneBootTestHelper.WaitUntil(() => motor.ReachedDestination, "the player to walk around the wall");

        Assert.That(Vector2.Distance(player.transform.position, navigation.CellToWorldCenter(goal)), Is.LessThan(0.25f));
    }

    /// <summary>
    /// The cell the stone of <paramref name="world"/>'s waystone <paramref name="entryName"/> stands on, in its loaded
    /// chunks; fails without one.
    /// </summary>
    private static Vector3Int StoneCellOf(LoadedWorld world, INavigation2D navigation, string entryName)
    {
        foreach (KeyValuePair<Vector2Int, Scene> chunk in world.Chunks)
        {
            foreach (WaystoneMarker marker in SceneQuery.FindAll<WaystoneMarker>(chunk.Value))
            {
                if (marker.EntryName == entryName)
                {
                    return navigation.WorldToCell(marker.transform.position);
                }
            }
        }

        Assert.Fail($"The waystone '{entryName}' has no stone in the loaded chunks.");
        return default;
    }

    /// <summary>
    /// Whether <paramref name="start"/> has <see cref="ClearLine"/> walkable cells in a straight line right, left, up or
    /// down, with walkable cells on both flanks, as the Clearing's tests look for one.
    /// </summary>
    private static bool HasClearLine(INavigation2D navigation, TerrainMovementProfile2D profile, Vector3Int start)
    {
        Vector3Int[] directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
        foreach (Vector3Int direction in directions)
        {
            Vector3Int side = new(direction.y, direction.x, 0);
            bool clear = navigation.TryGetLineCost(start, start + direction * ClearLine, profile, out _);
            for (int step = 0; clear && step <= ClearLine; step++)
            {
                Vector3Int cell = start + direction * step;
                clear = navigation.IsCellWalkable(cell + side, profile) && navigation.IsCellWalkable(cell - side, profile);
            }

            if (clear)
            {
                return true;
            }
        }

        return false;
    }
}
