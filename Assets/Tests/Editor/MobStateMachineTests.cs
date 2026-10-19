using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="MobController"/>'s state machine on a test navigation grid: idle, patrol, chase, attack,
/// search and return transitions, perception target choice, crowd control, separation and waiting behind allies.
/// </summary>
public class MobStateMachineTests
{
    private GameObject root;
    private MobConfig config;
    private Tile walkTile;
    private Tile blockTile;
    private MobController brain;
    private Transform player;
    private Health playerHealth;
    private PlayerHandle playerHandle;
    private PlayerRegistry players;
    private readonly IRandom random = new SystemRandom(1234);
    private NavigationGrid2D navGrid;
    private TerrainType2D groundTerrain;
    // Mobs and the player live outside root; left behind, they would be sensed by later tests' mobs.
    private readonly List<GameObject> sceneObjects = new();
    private readonly List<StatusEffectDefinition> statusDefinitions = new();

    /// <summary>
    /// Destroys the mobs, players, status definitions, grid root, tiles, terrain and config the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (GameObject sceneObject in sceneObjects)
        {
            if (sceneObject != null)
            {
                Object.DestroyImmediate(sceneObject);
            }
        }

        sceneObjects.Clear();

        foreach (StatusEffectDefinition definition in statusDefinitions)
        {
            Object.DestroyImmediate(definition);
        }

        statusDefinitions.Clear();

        if (root != null)
        {
            Object.DestroyImmediate(root);
        }

        if (config != null)
        {
            Object.DestroyImmediate(config);
        }

        if (walkTile != null)
        {
            Object.DestroyImmediate(walkTile);
        }

        if (blockTile != null)
        {
            Object.DestroyImmediate(blockTile);
        }

        if (groundTerrain != null)
        {
            Object.DestroyImmediate(groundTerrain);
        }
    }

    /// <summary>
    /// An idle mob chases a detected player, attacks once in range, returns when the player is far away and goes
    /// idle again at its spawn.
    /// </summary>
    [Test]
    public void StateMachine_TransitionsAcrossChaseAttackReturnIdle()
    {
        SetupWorld();

        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);

        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));

        player.position = new Vector3(0.5f, 0f, 0f);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.AttackRange));

        player.position = new Vector3(50f, 0f, 0f);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));

        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// Re-injecting a mob whose state machine is already running restarts it from idle, so it still reacts to a
    /// player instead of stalling with no state.
    /// </summary>
    [Test]
    public void Reinjection_WhileRunning_ResumesFromIdleInsteadOfStalling()
    {
        SetupWorld();
        brain.ChangeState(MobStateId.Patrol);

        // A scope rebuild re-injects the brain after Start has already entered its first state.
        brain.Construct(navGrid, players, random);

        player.position = new Vector3(3f, 0f, 0f);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
    }

    /// <summary>
    /// Configuring a mob labels the navigation grid's connectivity regions for its movement profile up front.
    /// </summary>
    [Test]
    public void Initialization_PrewarmsNavigationRegions_ForTheMobsProfile()
    {
        SetupWorld();

        Assert.That(navGrid.AreRegionsLabeled(config.MovementProfile), Is.True);
    }

    /// <summary>
    /// A returning mob counts as home once it is in its spawn cell, even when it is not on the exact spawn point.
    /// </summary>
    [Test]
    public void ReturnState_TransitionsToIdle_WhenMobIsInSpawnCellButNotSpawnPoint()
    {
        SetupWorld(new Vector3(0.1f, 0.1f, 0f));

        Vector3Int spawnCell = navGrid.WorldToCell(brain.Patrol.SpawnPosition);
        brain.transform.position = navGrid.CellToWorldCenter(spawnCell);

        player.position = new Vector3(100f, 0f, 0f);
        brain.ChangeState(MobStateId.Return);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// Repathing toward a moving target keeps the mob heading for the target rather than back to the center of
    /// the cell it is standing in.
    /// </summary>
    [Test]
    public void ChaseState_DoesNotSteerBackToCurrentCellCenter_WhenRepathingToMovingTarget()
    {
        SetupWorld();

        Rigidbody2D rb = brain.GetComponent<Rigidbody2D>();
        rb.position = new Vector2(0.8f, 0.5f);
        brain.transform.position = rb.position;

        player.position = new Vector3(4.5f, 0.5f, 0f);
        brain.ChangeState(MobStateId.Chase);
        brain.TickStateMachine(0.1f);
        brain.FixedTickStateMachine();

        Assert.That(rb.linearVelocity.x, Is.GreaterThan(0f));

        rb.position = new Vector2(0.8f, 0.5f);
        brain.transform.position = rb.position;
        rb.linearVelocity = Vector2.zero;

        player.position = new Vector3(5.5f, 0.5f, 0f);
        brain.TickStateMachine(0.1f);
        brain.FixedTickStateMachine();

        Assert.That(rb.linearVelocity.x, Is.GreaterThan(0f));
    }

    /// <summary>
    /// A chasing mob shoved off its path to the far side of a wall repaths and heads for the target, even though
    /// the target has not changed cell.
    /// </summary>
    [Test]
    public void ChaseState_Repaths_WhenMobIsPushedOffPathWhileTargetStaysInSameCell()
    {
        // Wall at x = 2 running from the grid's south edge up to y = 3, so the initial route goes over its top.
        List<Vector3Int> wall = new();
        for (int y = -10; y <= 3; y++)
        {
            wall.Add(new Vector3Int(2, y, 0));
        }

        SetupWorld(new Vector3(0.5f, 0.5f, 0f), blockedCells: wall);

        player.position = new Vector3(4.5f, 0.5f, 0f);
        brain.ChangeState(MobStateId.Chase);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.PathAgent.HasPath, Is.True);

        // Shoved to the east side of the wall; the old next waypoint is now behind it.
        Rigidbody2D rb = brain.GetComponent<Rigidbody2D>();
        rb.position = new Vector2(3.5f, -3.5f);
        brain.transform.position = rb.position;
        rb.linearVelocity = Vector2.zero;

        brain.TickStateMachine(0.1f);
        brain.FixedTickStateMachine();

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
        Assert.That(rb.linearVelocity.x, Is.GreaterThan(0f), "Mob should head straight for the target instead of back into the wall.");
    }

    /// <summary>
    /// A chasing mob that loses line of sight to a target still within range switches to search and paths to
    /// where it last saw the target.
    /// </summary>
    [Test]
    public void ChaseState_SearchesLastKnownPosition_WhenTargetGoesOutOfSightWithinRange()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));

        BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.LineOfSightInterval);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search));
        Assert.That(brain.PathAgent.HasPath, Is.True, "The mob should head for where it last saw the target.");
    }

    /// <summary>
    /// A searching mob goes back to chasing as soon as the blocker is gone and the target is in view again.
    /// </summary>
    [Test]
    public void SearchState_ResumesChase_WhenTargetComesBackIntoView()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        GameObject pillar = BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.LineOfSightInterval);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search));

        Object.DestroyImmediate(pillar);
        brain.TickStateMachine(config.LineOfSightInterval);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
    }

    /// <summary>
    /// While searching, a mob only re-acquires a visible target inside its detection radius, not out to its
    /// lose-target distance.
    /// </summary>
    [Test]
    public void SearchState_SpotsTargetOnlyWithinDetectionRadius_LikeEveryOtherState()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        GameObject pillar = BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.LineOfSightInterval);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search));

        // In plain view, beyond detectionRadius (8) but within loseTargetDistance (10).
        Object.DestroyImmediate(pillar);
        player.position = new Vector3(9f, 0f, 0f);
        brain.TickStateMachine(config.LineOfSightInterval);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search), "Aggro range must not widen while searching.");
    }

    /// <summary>
    /// The search duration only starts counting once the mob reaches the last known position, then it returns
    /// home after the full duration.
    /// </summary>
    [Test]
    public void SearchState_SearchTimeStartsOnArrival_NotWhileWalkingThere()
    {
        EnterSearchWithTargetGone();

        // Still walking to the last known position (edit mode runs no physics, so it never gets there).
        brain.TickStateMachine(config.SearchDuration + 0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search), "Walking there must not use up the search time.");

        Rigidbody2D rb = brain.GetComponent<Rigidbody2D>();
        rb.position = new Vector2(3f, 0f);
        brain.transform.position = rb.position;
        brain.FixedTickStateMachine();
        Assert.That(brain.PathAgent.ReachedDestination, Is.True);

        brain.TickStateMachine(config.SearchDuration - 0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search), "It should wait the full search time at the spot.");

        brain.TickStateMachine(0.2f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));
    }

    /// <summary>
    /// A searching mob that gets stuck on the way to the last known position starts its search time there and
    /// returns home once it runs out.
    /// </summary>
    [Test]
    public void SearchState_StartsSearchTime_WhenBlockedOnTheWay()
    {
        EnterSearchWithTargetGone();

        TickBlockedPastStuckTimeout();
        brain.TickStateMachine(config.SearchDuration);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));
    }

    /// <summary>
    /// A chasing mob gives up and returns when the target is behind a barrier with no path to it.
    /// </summary>
    [Test]
    public void ChaseState_TransitionsToReturn_WhenTargetHasNoReachablePath()
    {
        SetupWorld(addHorizontalBarrier: true);

        player.position = new Vector3(0f, 3f, 0f);
        brain.ChangeState(MobStateId.Chase);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));
    }

    /// <summary>
    /// A returning mob already at its spawn goes idle instead of chasing a detected target it cannot reach.
    /// </summary>
    [Test]
    public void ReturnState_GoesIdle_WhenDetectedTargetIsUnreachableButMobIsAlreadyAtSpawn()
    {
        SetupWorld(addHorizontalBarrier: true);

        player.position = new Vector3(0f, 3f, 0f);
        brain.ChangeState(MobStateId.Return);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// A returning mob already at its spawn goes idle when the player is off the navigation grid.
    /// </summary>
    [Test]
    public void ReturnState_GoesIdle_WhenDetectedTargetIsOffGridButMobIsAlreadyAtSpawn()
    {
        SetupWorld();

        player.position = new Vector3(100f, 0f, 0f);
        brain.ChangeState(MobStateId.Return);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// An idle mob stays idle when the player it detects is behind a barrier it has no path through.
    /// </summary>
    [Test]
    public void IdleState_DoesNotEnterChase_WhenDetectedTargetIsUnreachable()
    {
        SetupWorld(addHorizontalBarrier: true);

        player.position = new Vector3(0f, 3f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// A stunned mob makes no state changes, attacks or moves, and reacts to the player again as soon as the stun
    /// is removed.
    /// </summary>
    [Test]
    public void AStunnedMob_NeitherDecidesNorMoves_AndResumesInTheSameState()
    {
        SetupWorld();
        player.position = new Vector3(0.5f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        StatusEffectDefinition stun = Status("Stun", controls: StatusControls.Stun);
        brain.GetComponent<StatusEffects>().Set.Apply(stun, null, 1f);

        for (int i = 0; i < 5; i++)
        {
            brain.TickStateMachine(0.1f);
            brain.FixedTickStateMachine();
        }

        Assert.That(brain.IsStunned, Is.True);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle), "A stunned mob makes no decisions.");
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(playerHealth.MaxHealth), "Nor attacks.");
        Assert.That(brain.Motor.DesiredVelocity, Is.EqualTo(Vector2.zero));

        brain.GetComponent<StatusEffects>().Set.Remove(stun);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.Not.EqualTo(MobStateId.Idle), "It reacts to the player as soon as the stun ends.");
    }

    /// <summary>
    /// A rooted mob still changes state and attacks a player in range, but its motor neither moves nor reports a
    /// move speed.
    /// </summary>
    [Test]
    public void ARootedMob_HoldsStillButStillAttacksInRange()
    {
        SetupWorld();
        player.position = new Vector3(4f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.GetComponent<StatusEffects>().Set.Apply(Status("Root", controls: StatusControls.Root), null, 1f);

        brain.TickStateMachine(0.1f);
        brain.FixedTickStateMachine();
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase), "A rooted mob still decides.");
        Assert.That(brain.Motor.DesiredVelocity, Is.EqualTo(Vector2.zero), "But cannot move.");
        Assert.That(brain.Motor.CurrentMoveSpeed, Is.Zero);

        player.position = new Vector3(0.5f, 0f, 0f);
        brain.Perception.Tick(0f);
        brain.ChangeState(MobStateId.AttackRange);
        Assert.That(playerHealth.CurrentHealth, Is.LessThan(playerHealth.MaxHealth));
    }

    /// <summary>
    /// A mob under a half-speed slow chases at half its configured move speed.
    /// </summary>
    [Test]
    public void ASlowedMob_ChasesAtTheScaledSpeed()
    {
        SetupWorld();
        player.position = new Vector3(4.5f, 0.5f, 0f);
        brain.GetComponent<StatusEffects>().Set.Apply(Status("Chill", moveSpeed: 0.5f), null, 1f);

        brain.ChangeState(MobStateId.Chase);
        brain.TickStateMachine(0.1f);
        brain.FixedTickStateMachine();

        Assert.That(brain.Motor.CurrentMoveSpeed, Is.EqualTo(brain.Motor.MoveSpeed * 0.5f).Within(0.0001f));
        Assert.That(brain.Motor.DesiredVelocity.magnitude, Is.GreaterThan(0f).And.LessThanOrEqualTo(brain.Motor.MoveSpeed * 0.5f + 0.0001f));
    }

    /// <summary>
    /// Entering attack range hits once, and later ticks inside the attack interval deal no further damage.
    /// </summary>
    [Test]
    public void AttackRangeState_DoesNotApplyDamageEveryTick_WhileOnCooldown()
    {
        SetupWorld();

        player.position = new Vector3(0.5f, 0f, 0f);
        brain.Perception.Tick(0f);
        brain.ChangeState(MobStateId.AttackRange);
        int healthAfterEnter = playerHealth.CurrentHealth;

        brain.TickStateMachine(0.1f);

        Assert.That(healthAfterEnter, Is.EqualTo(9));
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(healthAfterEnter));
    }

    /// <summary>
    /// A target stepping out of attack range and back in does not let the mob hit again before its attack
    /// interval has passed.
    /// </summary>
    [Test]
    public void AttackRangeState_DoesNotResetCooldown_WhenTargetLeavesAndReentersRange()
    {
        SetupWorld();

        player.position = new Vector3(0.5f, 0f, 0f);
        brain.Perception.Tick(0f);
        brain.ChangeState(MobStateId.AttackRange);
        int healthAfterFirstHit = playerHealth.CurrentHealth;
        Assert.That(healthAfterFirstHit, Is.EqualTo(9));

        player.position = new Vector3(3f, 0f, 0f);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));

        player.position = new Vector3(0.5f, 0f, 0f);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.AttackRange));
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(healthAfterFirstHit), "Re-entering range must not bypass the attack interval.");
    }

    /// <summary>
    /// With several players in view, perception picks the nearest one and the mob chases it.
    /// </summary>
    [Test]
    public void Perception_TargetsTheNearestVisiblePlayer()
    {
        SetupWorld();
        player.position = new Vector3(5f, 0f, 0f);
        PlayerHandle nearer = AddPlayer(new Vector3(0f, 3f, 0f));
        brain.ChangeState(MobStateId.Idle);

        brain.TickStateMachine(0.1f);

        Assert.That(brain.Perception.CurrentTargetPlayer, Is.SameAs(nearer));
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
    }

    /// <summary>
    /// Perception skips a closer player whose line of sight is blocked and targets the farther visible one.
    /// </summary>
    [Test]
    public void Perception_IgnoresACloserPlayerBehindAWall()
    {
        SetupWorld();
        player.position = new Vector3(0f, 5f, 0f);
        AddPlayer(new Vector3(3f, 0f, 0f));
        BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.ChangeState(MobStateId.Idle);

        brain.TickStateMachine(0.1f);

        Assert.That(brain.Perception.CurrentTargetPlayer, Is.SameAs(playerHandle));
    }

    /// <summary>
    /// Once a mob has a target it keeps it across sight checks, even when another player moves closer.
    /// </summary>
    [Test]
    public void Perception_KeepsItsTarget_WhenAnotherPlayerComesCloser()
    {
        SetupWorld();
        player.position = new Vector3(4f, 0f, 0f);
        PlayerHandle other = AddPlayer(new Vector3(100f, 0f, 0f));
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.Perception.CurrentTargetPlayer, Is.SameAs(playerHandle));

        other.Transform.position = new Vector3(0f, 2f, 0f);
        for (int i = 0; i < 5; i++)
        {
            brain.TickStateMachine(config.LineOfSightInterval);
            Assert.That(brain.Perception.CurrentTargetPlayer, Is.SameAs(playerHandle));
        }
    }

    /// <summary>
    /// When the current target dies, perception switches to another visible player and the mob keeps chasing.
    /// </summary>
    [Test]
    public void Perception_SwitchesToAnotherVisiblePlayer_WhenItsTargetDies()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        PlayerHandle other = AddPlayer(new Vector3(0f, 5f, 0f));
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.Perception.CurrentTargetPlayer, Is.SameAs(playerHandle));

        playerHealth.ApplyDamage(playerHealth.MaxHealth);
        brain.TickStateMachine(config.LineOfSightInterval);

        Assert.That(brain.Perception.CurrentTargetPlayer, Is.SameAs(other));
        Assert.That(brain.Perception.HasDetectedTarget, Is.True);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
    }

    /// <summary>
    /// When the current target goes out of sight while another player is visible, the mob switches to that
    /// player and keeps chasing instead of searching.
    /// </summary>
    [Test]
    public void Perception_SwitchesToAnotherVisiblePlayer_WhenItsTargetHides()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        PlayerHandle other = AddPlayer(new Vector3(0f, -5f, 0f));
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.Perception.CurrentTargetPlayer, Is.SameAs(playerHandle));

        BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.LineOfSightInterval);

        Assert.That(brain.Perception.CurrentTargetPlayer, Is.SameAs(other));
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase), "Another player in view means no search.");
    }

    /// <summary>
    /// A target removed from the player registry is dropped, and the chasing mob returns home.
    /// </summary>
    [Test]
    public void Perception_DropsATargetThatLeavesTheRegistry()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));

        players.Remove(playerHandle);
        brain.TickStateMachine(config.LineOfSightInterval);

        Assert.That(brain.Perception.CurrentTarget, Is.Null);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));
    }

    /// <summary>
    /// A mob in attack range loses its target and leaves the attack state once the target dies.
    /// </summary>
    [Test]
    public void AttackRangeState_StopsAttacking_WhenTargetDies()
    {
        SetupWorld();

        player.position = new Vector3(0.5f, 0f, 0f);
        brain.Perception.Tick(0f);
        brain.ChangeState(MobStateId.AttackRange);
        playerHealth.ApplyDamage(playerHealth.MaxHealth);

        brain.TickStateMachine(0.1f);

        Assert.That(playerHealth.IsDead, Is.True);
        Assert.That(brain.Perception.HasDetectedTarget, Is.False);
        Assert.That(brain.CurrentStateId, Is.Not.EqualTo(MobStateId.AttackRange));
    }

    /// <summary>
    /// An idle mob ignores a dead player within detection range.
    /// </summary>
    [Test]
    public void IdleState_DoesNotEnterChase_WhenTargetIsDead()
    {
        SetupWorld();

        player.position = new Vector3(3f, 0f, 0f);
        playerHealth.ApplyDamage(playerHealth.MaxHealth);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// An idle mob stays idle when the player is off the navigation grid.
    /// </summary>
    [Test]
    public void IdleState_DoesNotEnterChase_WhenDetectedTargetIsOffGrid()
    {
        SetupWorld();

        player.position = new Vector3(100f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// A patrolling mob that stays stuck past the stuck timeout gives up on its roam destination and goes idle.
    /// </summary>
    [Test]
    public void PatrolState_GoesIdle_WhenBlockedFromReachingDestination()
    {
        SetupWorld();

        brain.ChangeState(MobStateId.Patrol);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Patrol));

        // Edit mode runs no physics, so the mob stays put exactly as if something blocked it.
        TickBlockedPastStuckTimeout();

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// A returning mob that stays stuck past the stuck timeout goes idle where it is instead of trying forever.
    /// </summary>
    [Test]
    public void ReturnState_GoesIdle_WhenBlockedFromReachingSpawn()
    {
        SetupWorld();

        Rigidbody2D rb = brain.GetComponent<Rigidbody2D>();
        rb.position = new Vector2(5.5f, 0.5f);
        brain.transform.position = rb.position;

        brain.ChangeState(MobStateId.Return);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));

        TickBlockedPastStuckTimeout();

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    /// <summary>
    /// Across repeated patrols, a mob whose roam radius spans a wall only picks destinations it can fully reach.
    /// </summary>
    [Test]
    public void PatrolState_OnlyRoamsToReachableCells_WhenRoamRadiusSpansAWall()
    {
        SetupWorld(addHorizontalBarrier: true, patrolRoamRadius: 4f);

        int patrols = 0;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            brain.ChangeState(MobStateId.Idle);
            brain.ChangeState(MobStateId.Patrol);
            if (brain.CurrentStateId != MobStateId.Patrol)
            {
                continue;
            }

            patrols++;
            Assert.That(brain.PathAgent.IsPartialPath, Is.False, $"Attempt {attempt} picked a roam destination across the wall.");
        }

        Assert.That(patrols, Is.GreaterThan(0));
    }

    /// <summary>
    /// Two overlapping mobs get velocities pushing each away from the other.
    /// </summary>
    [Test]
    public void Separation_PushesOverlappingMobsApart()
    {
        SetupWorld(new Vector3(0.5f, 0.5f, 0f));
        MobController neighbor = CreateMob(new Vector3(0.7f, 0.5f, 0f));

        brain.FixedTickStateMachine();
        neighbor.FixedTickStateMachine();

        Assert.That(brain.GetComponent<Rigidbody2D>().linearVelocity.x, Is.LessThan(0f));
        Assert.That(neighbor.GetComponent<Rigidbody2D>().linearVelocity.x, Is.GreaterThan(0f));
    }

    /// <summary>
    /// Two mobs at exactly the same position are still pushed apart, in opposite directions.
    /// </summary>
    [Test]
    public void Separation_PushesExactlyCoincidentMobsInOppositeDirections()
    {
        SetupWorld(new Vector3(0.5f, 0.5f, 0f));
        MobController neighbor = CreateMob(new Vector3(0.5f, 0.5f, 0f));

        brain.FixedTickStateMachine();
        neighbor.FixedTickStateMachine();

        Vector2 first = brain.GetComponent<Rigidbody2D>().linearVelocity;
        Vector2 second = neighbor.GetComponent<Rigidbody2D>().linearVelocity;
        Assert.That(first.sqrMagnitude, Is.GreaterThan(0f));
        Assert.That(Vector2.Dot(first, second), Is.LessThan(0f));
    }

    /// <summary>
    /// Separation from a neighbor never pushes a mob toward an unwalkable cell.
    /// </summary>
    [Test]
    public void Separation_NeverPushesAMobIntoAWall()
    {
        // The barrier row y = 1 is unwalkable; the neighbor below pushes this mob straight at it.
        SetupWorld(new Vector3(0.5f, 0.85f, 0f), addHorizontalBarrier: true);
        CreateMob(new Vector3(0.5f, 0.55f, 0f));

        brain.FixedTickStateMachine();

        Assert.That(brain.GetComponent<Rigidbody2D>().linearVelocity.y, Is.LessThanOrEqualTo(0f));
    }

    /// <summary>
    /// Separation from a neighbor never pushes a mob across the cliff of raised ground: on the front row of ground raised
    /// from row 1 up, a neighbor above pushes it toward the level cell below, which is walkable but beyond the cliff.
    /// </summary>
    [Test]
    public void Separation_NeverPushesAMobOffTheCliff()
    {
        SetupWorld(new Vector3(0.5f, 1.15f, 0f));
        GameObject raisedObject = new("Raised");
        raisedObject.transform.SetParent(root.transform);
        Tilemap raisedMap = raisedObject.AddComponent<Tilemap>();
        RaisedGroundTile raised = RaisedGroundTile.Create(false);
        try
        {
            for (int x = -10; x <= 10; x++)
            {
                for (int y = 1; y <= 10; y++)
                {
                    raisedMap.SetTile(new Vector3Int(x, y, 0), raised);
                }
            }

            navGrid.ConfigureRaisedTilemap(raisedMap);
            navGrid.BuildGrid();
            CreateMob(new Vector3(0.5f, 1.45f, 0f));

            brain.FixedTickStateMachine();

            Assert.That(navGrid.IsCellWalkable(Vector3Int.zero), Is.True, "The cell below is walkable.");
            Assert.That(brain.GetComponent<Rigidbody2D>().linearVelocity.y, Is.GreaterThanOrEqualTo(0f));
        }
        finally
        {
            Object.DestroyImmediate(raised);
        }
    }

    /// <summary>
    /// A chasing mob with an ally between it and the nearby target holds back instead of pushing into the ally.
    /// </summary>
    [Test]
    public void ChaseState_WaitsBehindAnAllyNearTheTarget_InsteadOfPushingIntoIt()
    {
        SetupWorld(new Vector3(1.7f, 0.5f, 0f));
        CreateMob(new Vector3(2.2f, 0.5f, 0f));
        player.position = new Vector3(3f, 0.5f, 0f);

        brain.ChangeState(MobStateId.Chase);
        brain.TickStateMachine(0.1f);
        brain.FixedTickStateMachine();

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
        Assert.That(brain.GetComponent<Rigidbody2D>().linearVelocity.x, Is.LessThanOrEqualTo(0f),
            "The mob should hold (only separation moves it, away from the ally) rather than push toward the target.");
    }

    /// <summary>
    /// A waiting mob nudged just outside the separation radius keeps waiting rather than stepping straight back
    /// toward the ally.
    /// </summary>
    [Test]
    public void ChaseState_KeepsWaiting_WhenSeparationNudgesItJustPastTheAlly()
    {
        SetupWorld(new Vector3(1.7f, 0.5f, 0f));
        CreateMob(new Vector3(2.2f, 0.5f, 0f));
        player.position = new Vector3(3f, 0.5f, 0f);
        brain.ChangeState(MobStateId.Chase);
        brain.TickStateMachine(0.1f);
        brain.FixedTickStateMachine();

        // Pushed back to just beyond the separation radius (0.8) from the ally, but within 1.5x of it.
        Rigidbody2D rb = brain.GetComponent<Rigidbody2D>();
        rb.position = new Vector2(1.2f, 0.5f);
        rb.linearVelocity = Vector2.zero;
        brain.FixedTickStateMachine();

        Assert.That(rb.linearVelocity.x, Is.LessThanOrEqualTo(0f), "The mob should stay put instead of stepping straight back in.");
    }

    /// <summary>
    /// A waiting mob resumes moving toward the target once the ally blocking the way is gone.
    /// </summary>
    [Test]
    public void ChaseState_StopsWaiting_OnceTheWayAheadIsClearlyOpen()
    {
        SetupWorld(new Vector3(1.7f, 0.5f, 0f));
        MobController ally = CreateMob(new Vector3(2.2f, 0.5f, 0f));
        player.position = new Vector3(3f, 0.5f, 0f);
        brain.ChangeState(MobStateId.Chase);
        brain.TickStateMachine(0.1f);
        brain.FixedTickStateMachine();

        Object.DestroyImmediate(ally.gameObject);
        Physics2D.SyncTransforms();
        brain.FixedTickStateMachine();
        brain.TickStateMachine(config.RepathInterval);
        brain.FixedTickStateMachine();

        Assert.That(brain.GetComponent<Rigidbody2D>().linearVelocity.x, Is.GreaterThan(0f));
    }

    /// <summary>
    /// Adds another living, damageable player at <paramref name="position"/> to the registry the mobs sense.
    /// </summary>
    private PlayerHandle AddPlayer(Vector3 position)
    {
        GameObject playerObject = new("OtherPlayer");
        sceneObjects.Add(playerObject);
        playerObject.tag = "Player";
        playerObject.transform.position = position;
        PlayerHandle handle = new(playerObject.transform, playerObject.AddComponent<Health>());
        playerObject.AddComponent<DamageReceiver>();
        players.Add(handle);
        return handle;
    }

    /// <summary>
    /// Chases a target at (3, 0), loses sight of it behind a pillar so the mob starts searching, then takes the
    /// target far away so it cannot be spotted again during the search.
    /// </summary>
    private void EnterSearchWithTargetGone()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.LineOfSightInterval);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search));

        player.position = new Vector3(100f, 0f, 0f);
    }

    /// <summary>
    /// Places a pillar on the obstacle layer that perception linecasts against. Navigation is unaffected, so
    /// the mob can still path to where it last saw the target.
    /// </summary>
    private GameObject BlockLineOfSightAt(Vector2 position)
    {
        GameObject pillar = new("Pillar") { layer = 8 };
        pillar.transform.SetParent(root.transform);
        pillar.transform.position = position;
        pillar.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 3f);
        Physics2D.SyncTransforms();
        return pillar;
    }

    /// <summary>
    /// Runs enough fixed ticks without the mob moving to exceed its stuck timeout, then one state machine tick so
    /// the state can react.
    /// </summary>
    private void TickBlockedPastStuckTimeout()
    {
        int fixedTicks = Mathf.CeilToInt(config.StuckTimeout / Time.fixedDeltaTime) + 1;
        for (int i = 0; i < fixedTicks; i++)
        {
            brain.FixedTickStateMachine();
        }

        brain.TickStateMachine(0.1f);
    }

    /// <summary>
    /// Builds a 21x21 walkable navigation grid centered on the origin, with an optional wall along row y = 1 and
    /// extra <paramref name="blockedCells"/>, a registered player far out of range, the mob config and the mob
    /// under test at <paramref name="spawnPosition"/> (the origin by default).
    /// </summary>
    private void SetupWorld(Vector3? spawnPosition = null, bool addHorizontalBarrier = false, IReadOnlyList<Vector3Int> blockedCells = null, float patrolRoamRadius = 2f)
    {
        root = new GameObject("MobStateMachineTestRoot");
        Grid grid = root.AddComponent<Grid>();

        GameObject dataObject = new("DataTilemap");
        dataObject.transform.SetParent(root.transform);
        Tilemap dataTilemap = dataObject.AddComponent<Tilemap>();
        dataObject.AddComponent<TilemapRenderer>();
        NavigationTerrainSource2D terrainSource = dataObject.AddComponent<NavigationTerrainSource2D>();

        GameObject collisionObject = new("CollisionTilemap");
        collisionObject.transform.SetParent(root.transform);
        Tilemap collisionTilemap = collisionObject.AddComponent<Tilemap>();
        collisionObject.AddComponent<TilemapRenderer>();

        walkTile = ScriptableObject.CreateInstance<Tile>();
        blockTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = TerrainType2D.Create("ground");
        for (int x = -10; x <= 10; x++)
        {
            for (int y = -10; y <= 10; y++)
            {
                dataTilemap.SetTile(new Vector3Int(x, y, 0), walkTile);
            }
        }

        if (addHorizontalBarrier)
        {
            for (int x = -10; x <= 10; x++)
            {
                collisionTilemap.SetTile(new Vector3Int(x, 1, 0), blockTile);
            }
        }

        if (blockedCells != null)
        {
            foreach (Vector3Int cell in blockedCells)
            {
                collisionTilemap.SetTile(cell, blockTile);
            }
        }

        terrainSource.Configure(groundTerrain);
        navGrid = root.AddComponent<NavigationGrid2D>();
        navGrid.ConfigureBlockingTilemaps(collisionTilemap);
        navGrid.BuildGrid();

        GameObject playerObject = new("Player");
        sceneObjects.Add(playerObject);
        playerObject.tag = "Player";
        playerObject.transform.position = new Vector3(100f, 0f, 0f);
        player = playerObject.transform;
        playerHealth = playerObject.AddComponent<Health>();
        playerObject.AddComponent<DamageReceiver>();
        playerObject.AddComponent<DisableOnDeath>();
        playerHandle = new PlayerHandle(player, playerHealth);
        players = new PlayerRegistry();
        players.Add(playerHandle);

        config = MobConfig.Create(
            idleDurationRange: new Vector2(10f, 10f),
            detectionRadius: 8f,
            loseTargetDistance: 10f,
            attackStopDistance: 1f,
            attackExitBuffer: 0.25f,
            attackDamage: 1,
            attackInterval: 0.75f,
            repathInterval: 0.1f,
            patrolRoamRadius: patrolRoamRadius);

        brain = CreateMob(spawnPosition ?? Vector3.zero);
    }

    /// <summary>
    /// Creates a 10-second debuff with the given move speed multiplier and crowd control, destroyed on teardown.
    /// </summary>
    private StatusEffectDefinition Status(string name, float moveSpeed = 1f, StatusControls controls = StatusControls.None)
    {
        StatusEffectDefinition definition = StatusEffectDefinition.Create(name, StatusKind.Debuff, 10f, moveSpeedMultiplier: moveSpeed, controls: controls);
        statusDefinitions.Add(definition);
        return definition;
    }

    /// <summary>
    /// Creates a fully wired mob at <paramref name="position"/>, configured with the shared config, grid, player
    /// registry and random source, and an authoritative melee hit service.
    /// </summary>
    private MobController CreateMob(Vector3 position)
    {
        GameObject mob = new("Mob");
        sceneObjects.Add(mob);
        mob.transform.position = position;
        Rigidbody2D rb = mob.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        mob.AddComponent<BoxCollider2D>();
        mob.AddComponent<SpriteRenderer>();
        mob.AddComponent<Animator>();
        mob.AddComponent<Health>();
        mob.AddComponent<DamageReceiver>();
        mob.AddComponent<DisableOnDeath>();
        mob.AddComponent<StatusEffects>();
        mob.AddComponent<MeleeDamageDealer>();
        mob.AddComponent<MobMotor2D>();
        mob.AddComponent<MobPerception2D>();
        mob.AddComponent<MobPathAgent2D>();
        mob.AddComponent<MobPatrolAnchor>();
        MobController mobBrain = mob.AddComponent<MobController>();

        mob.GetComponent<MeleeDamageDealer>().Construct(UnityClock.Shared, TestCombat.CreateHitService(FixedGameAuthority.Authoritative));
        mobBrain.Configure(config, navGrid, players, random);
        Physics2D.SyncTransforms();
        return mobBrain;
    }
}
