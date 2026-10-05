using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using VContainer;

/// <summary>
/// PlayMode tests for the click-to-move controls in the booted starting area, driven by scripted commands: pathing,
/// attack orders, player overlap, the ability and status HUD, the dev status panel and death.
/// </summary>
public class PlayerControlsPlayModeTests
{
    private const float WalkTimeoutSeconds = 10f;

    private IObjectResolver gameplay;
    private PlayerController player;
    private NavigationGrid2D grid;
    private TerrainMovementProfile2D profile;
    private ScriptedCommands commands;

    /// <summary>
    /// After booting into the starting area, the active navigation grid is the one in the area scene.
    /// </summary>
    [UnityTest]
    public IEnumerator EnteringTheClearing_MakesItsGridTheActiveOne()
    {
        yield return Boot();

        Assert.That(grid, Is.Not.Null);
        Assert.That(grid.gameObject.scene, Is.EqualTo(SceneBootTestHelper.ResolveGameFlow().AreaScene));
    }

    /// <summary>
    /// A move click behind a runtime-built wall gives a path with several waypoints around it, and the player arrives
    /// at the goal.
    /// </summary>
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

    /// <summary>
    /// Clicking a mob behind a wall picks it as the target and gives an attack order that walls and distance do not
    /// cancel; the order kills the mob and ends with it.
    /// </summary>
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

    /// <summary>
    /// The player walks straight through a body on the Player layer standing on its path, without pushing it.
    /// </summary>
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

    /// <summary>
    /// The ability bar has one slot per ability labelled with its name and bound key and shows the cooldown fill and
    /// seconds after a cast; the pointer feedback layer has prewarmed move markers and the attack cursor hidden.
    /// </summary>
    [UnityTest]
    public IEnumerator Hud_ShowsTheLoadoutAndCooldowns_AndPointerFeedbackIsReady()
    {
        yield return Boot();
        AbilitySlotView[] slots = Object.FindAnyObjectByType<AbilityBarView>().GetComponentsInChildren<AbilitySlotView>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();

        Assert.That(slots.Length, Is.EqualTo(AbilitySlots.Count));
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.That(slots[i].NameText.text, Is.EqualTo(abilities.GetAbility(i).DisplayName), "Blanks have no icon, so their names show.");
            Assert.That(slots[i].KeyText.text, Is.Not.Empty, "Each slot is labelled with its bound key.");
        }

        Assert.That(abilities.TryCast(0, new CastAim(Vector2.zero, Vector2.right, default)), Is.EqualTo(CastOutcome.Started));
        abilities.CompleteCast(0);
        yield return null;
        yield return null;

        Assert.That(slots[0].CooldownFill.fillAmount, Is.GreaterThan(0.5f));
        Assert.That(slots[0].CooldownText.text, Is.EqualTo(Mathf.CeilToInt(abilities.RemainingCooldown(0)).ToString()));

        PointerFeedbackLayer feedback = Object.FindAnyObjectByType<PointerFeedbackLayer>();
        Assert.That(feedback, Is.Not.Null);
        Assert.That(feedback.PooledMarkerCount, Is.GreaterThan(0), "Move markers are prewarmed.");
        Assert.That(feedback.AttackCursorShown, Is.False);
    }

    /// <summary>
    /// The player's statuses show on the HUD bar with buffs first and a Burn visual that follows the player; a mob's
    /// debuff shows above its health bar; clearing the player's statuses hides the icons and pools the visual.
    /// </summary>
    [UnityTest]
    public IEnumerator Statuses_ShowOnTheHud_OnTheMobsBar_AndAsVisuals()
    {
        yield return Boot();
        StatusEffectService statuses = SceneBootTestHelper.ResolveFromGameplay<StatusEffectService>();
        StatusEffectDefinition burn = SceneBootTestHelper.FindStatus("Burn");
        StatusEffectDefinition fortify = SceneBootTestHelper.FindStatus("Fortify");
        StatusBarView bar = Object.FindAnyObjectByType<StatusBarView>();
        Assert.That(bar, Is.Not.Null);

        Assert.That(statuses.Apply(player.GetComponent<DamageReceiver>(), fortify, player.gameObject), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(statuses.Apply(player.GetComponent<DamageReceiver>(), burn), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return null;
        Assert.That(bar.Icon(0).gameObject.activeSelf && bar.Icon(0).Shown == fortify, Is.True, "Buffs come first on the HUD.");
        Assert.That(bar.Icon(1).gameObject.activeSelf && bar.Icon(1).Shown == burn, Is.True);
        Assert.That(bar.Icon(2).gameObject.activeSelf, Is.False);
        Assert.That(player.GetComponent<StatusVisuals>().ShownCount, Is.EqualTo(1), "Burn has a visual, Fortify does not.");
        StatusVisual aura = player.GetComponent<StatusVisuals>().GetShown(0);
        Assert.That(Vector2.Distance(aura.transform.position, player.transform.position), Is.LessThan(0.01f), "The visual follows the player.");

        MobController mob = Object.FindAnyObjectByType<MobController>();
        Assert.That(statuses.Apply(mob.GetComponent<DamageReceiver>(), burn, player.gameObject), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return null;
        Assert.That(mob.GetComponentInChildren<WorldStatusIcons>().ShownCount, Is.EqualTo(1), "The mob shows its debuff above its health bar.");

        statuses.ClearAll(player.GetComponent<DamageReceiver>());
        yield return null;
        Assert.That(bar.Icon(0).gameObject.activeSelf, Is.False);
        Assert.That(aura.gameObject.activeSelf, Is.False, "The visual returns to the pool.");
    }

    /// <summary>
    /// The hidden dev status panel applies statuses to the player and to the unit under a screen point (Invalid when
    /// there is none); Blank Smite deals 18 to a Vulnerable mob and stuns it, and Blank Mend heals 25 and fortifies.
    /// </summary>
    [UnityTest]
    public IEnumerator DevPanel_AppliesStatuses_AndSmiteAndMendHaveEffects()
    {
        yield return Boot();
        StatusDebugPanel panel = Object.FindAnyObjectByType<StatusDebugPanel>();
        Assert.That(panel, Is.Not.Null, "Every play session gets the dev status panel.");
        Assert.That(panel.IsOpen, Is.False, "It starts hidden.");

        StatusEffectDefinition chill = SceneBootTestHelper.FindStatus("Chill");
        Assert.That(panel.ApplyToSelf(chill), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(player.GetComponent<StatusEffects>().GetStacks(chill), Is.EqualTo(1));

        MobController mob = Object.FindAnyObjectByType<MobController>();
        mob.enabled = false;
        Collider2D mobCollider = mob.GetComponent<Collider2D>();
        Vector2 mobScreenPoint = gameplay.Resolve<Camera>().WorldToScreenPoint(mobCollider.bounds.center);
        Assert.That(panel.ApplyToUnitAt(mobScreenPoint, SceneBootTestHelper.FindStatus("Vulnerable")), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(panel.ApplyToUnitAt(mobScreenPoint + new Vector2(5000f, 0f), chill), Is.EqualTo(StatusApplyOutcome.Invalid), "Nothing under the cursor.");

        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        Health mobHealth = mob.GetComponent<Health>();
        CastAim atMob = new(mob.transform.position, Vector2.right, new UnitTarget(mobHealth, mobCollider, UnitTeam.Enemy));
        Assert.That(abilities.GetAbility(3).DisplayName, Is.EqualTo("Blank Smite"));
        int mobHealthBefore = mobHealth.CurrentHealth;
        Assert.That(abilities.TryCast(3, atMob), Is.EqualTo(CastOutcome.Started));
        abilities.CompleteCast(3);
        Assert.That(mobHealth.CurrentHealth, Is.EqualTo(mobHealthBefore - 18), "15 damage, 1.2x on a Vulnerable mob.");
        Assert.That(mob.IsStunned, Is.True, "Smite stuns.");

        Health playerHealth = player.GetComponent<Health>();
        int playerHealthBefore = playerHealth.CurrentHealth;
        gameplay.Resolve<DamageService>().ApplyDamage(player.GetComponent<DamageReceiver>(), 40);
        CastAim atSelf = new(player.transform.position, Vector2.zero, new UnitTarget(playerHealth, player.GetComponent<Collider2D>(), UnitTeam.Ally));
        Assert.That(abilities.GetAbility(2).DisplayName, Is.EqualTo("Blank Mend"));
        Assert.That(abilities.TryCast(2, atSelf), Is.EqualTo(CastOutcome.Started));
        abilities.CompleteCast(2);
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(playerHealthBefore - 15), "Mend heals 25.");
        Assert.That(player.GetComponent<StatusEffects>().GetStacks(SceneBootTestHelper.FindStatus("Fortify")), Is.EqualTo(1), "And fortifies.");
        yield return null;
    }

    /// <summary>
    /// A player that dies mid-move is left with no order, no path and no movement.
    /// </summary>
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

    /// <summary>
    /// Boots into the starting area, caches the gameplay container, local player, active grid and movement profile,
    /// and switches the player to scripted commands.
    /// </summary>
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

    /// <summary>
    /// Waits until the player has no order, failing the test after the walk timeout.
    /// </summary>
    private IEnumerator WaitUntilIdle()
    {
        float deadline = Time.realtimeSinceStartup + WalkTimeoutSeconds;
        while (player.CurrentOrder != PlayerOrderKind.Idle)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The move order should finish.");
            yield return null;
        }
    }

    /// <summary>
    /// Deactivates every mob so none attacks or blocks the player during the test.
    /// </summary>
    private static void DisableMobs()
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Builds a seven-cell collision wall two cells to the right of the player through the area's
    /// <see cref="AreaTerrain"/> (the Clearing has no walls of its own) and returns a point straight behind it. The
    /// grid picks the new tiles up by itself.
    /// </summary>
    private Vector2 BuildWallInFrontOfThePlayer()
    {
        Tilemap collision = grid.BlockingTilemaps.Count > 0 ? grid.BlockingTilemaps[0] : null;
        Assert.That(collision, Is.Not.Null, "The Clearing should have a collision tilemap.");
        AreaTerrain terrain = Object.FindAnyObjectByType<AreaTerrain>();
        Vector3Int start = grid.WorldToCell(player.transform.position);
        Vector3Int[] wall = new Vector3Int[7];
        for (int dy = -3; dy <= 3; dy++)
        {
            wall[dy + 3] = new Vector3Int(start.x + 2, start.y + dy, start.z);
        }

        Assert.That(terrain.TrySetCells(collision, wall, filled: true), Is.True);
        TilemapCollider2D wallCollider = collision.GetComponent<TilemapCollider2D>();
        wallCollider.ProcessTilemapChanges();
        Assert.That(wallCollider.shapeCount, Is.GreaterThanOrEqualTo(wall.Length), "Every wall cell gets a grid collider.");
        DualGridTilemapModule wallArt = collision.GetComponent<DualGridTilemapModule>();
        Assert.That(wallArt, Is.Not.Null, "The collision tilemap is a dual-grid wall tilemap.");
        Assert.That(wallArt.RenderTilemap.HasTile(DualGridUtils.GetRenderTilePosition(wall[0], 3)), Is.True, "Its render tilemap draws the new wall.");
        Assert.That(wallArt.RenderTilemap.gameObject.layer, Is.Not.EqualTo(collision.gameObject.layer), "The wall art is on a layer the camera draws.");
        Physics2D.SyncTransforms();

        Vector3Int goal = new(start.x + 4, start.y, start.z);
        Assert.That(grid.IsCellWalkable(goal, profile), Is.True);
        Assert.That(grid.TryGetLineCost(start, goal, profile, out _), Is.False, "The wall should block the straight line.");
        return grid.CellToWorldCenter(goal);
    }

    /// <summary>
    /// The center of the first cell <paramref name="distance"/> cells right, left, up or down of
    /// <paramref name="from"/> that has a walkable straight line with walkable cells on both flanks; fails the test
    /// when there is none.
    /// </summary>
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

    /// <summary>
    /// Whether the cells on both sides of the straight run from <paramref name="start"/> along
    /// <paramref name="direction"/> are walkable for its full <paramref name="distance"/>.
    /// </summary>
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

    /// <summary>
    /// <see cref="IPlayerCommandSource"/> that the test feeds one click at a time.
    /// </summary>
    private sealed class ScriptedCommands : IPlayerCommandSource
    {
        private PlayerCommand next;

        /// <summary>
        /// Queues a single move click at <paramref name="point"/> on <paramref name="target"/> for the next read.
        /// </summary>
        public void Click(Vector2 point, UnitTarget target)
        {
            next = new PlayerCommand(point, target, movePressed: true, moveHeld: false, stopPressed: false);
        }

        /// <summary>
        /// Returns the queued command and clears it, so each click is read once; an empty command otherwise.
        /// </summary>
        public PlayerCommand ReadCommand()
        {
            PlayerCommand command = next;
            next = default;
            return command;
        }
    }
}
