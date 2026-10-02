using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using VContainer;

public class PlayerControlsPlayModeTests
{
    private const float WalkTimeoutSeconds = 10f;

    private IObjectResolver gameplay;
    private PlayerController player;
    private NavigationGrid2D grid;
    private TerrainMovementProfile2D profile;
    private ScriptedCommands commands;
    private Tile wallTile;

    [TearDown]
    public void TearDown()
    {
        if (wallTile != null)
        {
            Object.Destroy(wallTile);
        }
    }

    [UnityTest]
    public IEnumerator EnteringTheClearing_MakesItsGridTheActiveOne()
    {
        yield return Boot();

        Assert.That(grid, Is.Not.Null);
        Assert.That(grid.gameObject.scene, Is.EqualTo(SceneBootTestHelper.ResolveGameFlow().AreaScene));
    }

    [UnityTest]
    public IEnumerator RightClickBehindAWall_PathsAroundIt()
    {
        yield return Boot();
        DisableMobs();
        Vector2 goal = BuildWallInFrontOfThePlayer();

        commands.Click(goal, default);
        yield return null;

        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(player.GetComponent<PlayerMotor2D>().Follower.Waypoints.Count, Is.GreaterThan(1), "The path should turn around the wall.");
        yield return WaitUntilIdle();

        Assert.That(Vector2.Distance(player.transform.position, goal), Is.LessThan(0.2f), "The player should arrive behind the wall.");
    }

    [UnityTest]
    public IEnumerator RightClickOnAMobBehindAWall_PathsAroundAndKillsIt()
    {
        yield return Boot();
        MobController mob = Object.FindAnyObjectByType<MobController>();
        Assert.That(mob, Is.Not.Null);
        Health mobHealth = mob.GetComponent<Health>();
        Vector2 hidingSpot = BuildWallInFrontOfThePlayer();
        Rigidbody2D mobBody = mob.GetComponent<Rigidbody2D>();
        mobBody.position = hidingSpot;
        mob.transform.position = hidingSpot;
        Physics2D.SyncTransforms();

        UnitTarget target = gameplay.Resolve<PointerTargetPicker>().Pick(hidingSpot + Vector2.up * 0.15f);
        Assert.That(target.Health, Is.SameAs(mobHealth), "Clicking the mob should pick it.");
        commands.Click(hidingSpot, target);
        yield return null;
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Attack));

        float deadline = Time.realtimeSinceStartup + 20f;
        while (mobHealth != null && !mobHealth.IsDead)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The attack order should kill the mob.");
            Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Attack), "Walls and distance must not cancel the order.");
            yield return null;
        }

        yield return null;
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Idle), "The order ends with its target.");
    }

    [UnityTest]
    public IEnumerator Players_WalkThroughEachOther()
    {
        yield return Boot();
        DisableMobs();
        Vector2 start = player.transform.position;
        Vector2 goal = FindClearGoal(start, 3);
        Vector2 middle = Vector2.Lerp(start, goal, 0.5f);
        GameObject teammate = new("TeammateBody") { layer = LayerMask.NameToLayer("Player") };
        teammate.transform.position = middle;
        teammate.AddComponent<BoxCollider2D>().size = new Vector2(0.35f, 0.15f);
        Rigidbody2D teammateBody = teammate.AddComponent<Rigidbody2D>();
        teammateBody.gravityScale = 0f;
        teammateBody.freezeRotation = true;

        try
        {
            commands.Click(goal, default);
            yield return null;
            Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));
            yield return WaitUntilIdle();

            Assert.That(Vector2.Distance(player.transform.position, goal), Is.LessThan(0.2f), "The player should walk straight through its teammate.");
            Assert.That(Vector2.Distance(teammateBody.position, middle), Is.LessThan(0.01f), "The teammate should not be pushed.");
        }
        finally
        {
            Object.Destroy(teammate);
        }
    }

    [UnityTest]
    public IEnumerator Death_ClearsTheOrders()
    {
        yield return Boot();
        DisableMobs();
        commands.Click((Vector2)player.transform.position + Vector2.right * 50f, default);
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));

        Health health = player.GetComponent<Health>();
        health.ApplyDamage(health.MaxHealth);
        yield return null;

        PlayerMotor2D motor = player.GetComponent<PlayerMotor2D>();
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(motor.HasPath, Is.False);
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.zero));
    }

    private IEnumerator Boot()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
        yield return null;

        gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        player = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        grid = gameplay.Resolve<ActiveNavigationGrid>().Current;
        profile = player.ControlSettings.MovementProfile;
        commands = new ScriptedCommands();
        player.SetCommandSource(commands);
    }

    private IEnumerator WaitUntilIdle()
    {
        float deadline = Time.realtimeSinceStartup + WalkTimeoutSeconds;
        while (player.CurrentOrder != PlayerOrderKind.Idle)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The move order should finish.");
            yield return null;
        }
    }

    private static void DisableMobs()
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Builds a seven-cell collision wall two cells to the right of the player (the Clearing has no walls of its own),
    /// rebuilds the grid, and returns a point straight behind it.
    /// </summary>
    private Vector2 BuildWallInFrontOfThePlayer()
    {
        Tilemap collision = null;
        foreach (NavigationTerrainSource2D source in grid.GetComponentsInChildren<NavigationTerrainSource2D>())
        {
            if (source.CollisionTilemap != null)
            {
                collision = source.CollisionTilemap;
                break;
            }
        }

        Assert.That(collision, Is.Not.Null, "The Clearing should have a collision tilemap.");
        wallTile = ScriptableObject.CreateInstance<Tile>();
        wallTile.colliderType = Tile.ColliderType.Grid;
        Vector3Int start = grid.WorldToCell(player.transform.position);
        for (int dy = -3; dy <= 3; dy++)
        {
            collision.SetTile(new Vector3Int(start.x + 2, start.y + dy, start.z), wallTile);
        }

        collision.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
        grid.BuildGrid();
        Physics2D.SyncTransforms();

        Vector3Int goal = new(start.x + 4, start.y, start.z);
        Assert.That(grid.IsCellWalkable(goal, profile), Is.True);
        Assert.That(grid.TryGetLineCost(start, goal, profile, out _), Is.False, "The wall should block the straight line.");
        return grid.CellToWorldCenter(goal);
    }

    private Vector2 FindClearGoal(Vector2 from, int distance)
    {
        Vector3Int start = grid.WorldToCell(from);
        Vector3Int[] directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
        foreach (Vector3Int direction in directions)
        {
            Vector3Int cell = start + direction * distance;
            if (grid.TryGetLineCost(start, cell, profile, out _) && ClearOfWalls(start, direction, distance))
            {
                return grid.CellToWorldCenter(cell);
            }
        }

        Assert.Fail("The spawn point should have open ground on one side.");
        return from;
    }

    private bool ClearOfWalls(Vector3Int start, Vector3Int direction, int distance)
    {
        Vector3Int side = new(direction.y, direction.x, 0);
        for (int step = 0; step <= distance; step++)
        {
            Vector3Int cell = start + direction * step;
            if (!grid.IsCellWalkable(cell + side, profile) || !grid.IsCellWalkable(cell - side, profile))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ScriptedCommands : IPlayerCommandSource
    {
        private PlayerCommand next;

        public void Click(Vector2 point, UnitTarget target)
        {
            next = new PlayerCommand(point, target, movePressed: true, moveHeld: false, stopPressed: false);
        }

        public PlayerCommand ReadCommand()
        {
            PlayerCommand command = next;
            next = default;
            return command;
        }
    }
}
