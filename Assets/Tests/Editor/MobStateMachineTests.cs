using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MobStateMachineTests
{
    private GameObject root;
    private MobConfig config;
    private Tile walkTile;
    private Tile blockTile;
    private MobController brain;
    private Transform player;
    private Health playerHealth;
    private NavigationGrid2D navGrid;
    private TerrainType2D groundTerrain;
    // Mobs and the player live outside root; left behind, they would be sensed by later tests' mobs.
    private readonly List<GameObject> sceneObjects = new();

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

    [Test]
    public void Reinjection_WhileRunning_ResumesFromIdleInsteadOfStalling()
    {
        SetupWorld();
        brain.ChangeState(MobStateId.Patrol);

        // A scope rebuild re-injects the brain after Start has already entered its first state.
        brain.Construct(navGrid, new PlayerLocator(player, playerHealth));

        player.position = new Vector3(3f, 0f, 0f);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
    }

    [Test]
    public void Initialization_PrewarmsNavigationRegions_ForTheMobsProfile()
    {
        SetupWorld();

        Assert.That(navGrid.AreRegionsLabeled(config.MovementProfile), Is.True);
    }

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

    [Test]
    public void ChaseState_SearchesLastKnownPosition_WhenTargetGoesOutOfSightWithinRange()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));

        BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.lineOfSightInterval);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search));
        Assert.That(brain.PathAgent.HasPath, Is.True, "The mob should head for where it last saw the target.");
    }

    [Test]
    public void SearchState_ResumesChase_WhenTargetComesBackIntoView()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        GameObject pillar = BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.lineOfSightInterval);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search));

        Object.DestroyImmediate(pillar);
        brain.TickStateMachine(config.lineOfSightInterval);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
    }

    [Test]
    public void SearchState_SpotsTargetWithinLoseDistance_BeyondNormalDetectionRadius()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        GameObject pillar = BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.lineOfSightInterval);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search));

        // Beyond detectionRadius (8) but within loseTargetDistance (10), in plain view.
        Object.DestroyImmediate(pillar);
        player.position = new Vector3(9f, 0f, 0f);
        brain.TickStateMachine(config.lineOfSightInterval);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Chase));
    }

    [Test]
    public void SearchState_ClearsAlert_WhenItEnds()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.lineOfSightInterval);
        Assert.That(brain.Perception.IsAlert, Is.True);

        brain.TickStateMachine(config.searchDuration + 0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));
        Assert.That(brain.Perception.IsAlert, Is.False);
    }

    [Test]
    public void SearchState_GivesUpAndReturns_AfterSearchDuration()
    {
        SetupWorld();
        player.position = new Vector3(3f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);
        BlockLineOfSightAt(new Vector2(1.5f, 0f));
        brain.TickStateMachine(config.lineOfSightInterval);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Search));

        brain.TickStateMachine(config.searchDuration + 0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));
    }

    [Test]
    public void ChaseState_TransitionsToReturn_WhenTargetHasNoReachablePath()
    {
        SetupWorld(addHorizontalBarrier: true);

        player.position = new Vector3(0f, 3f, 0f);
        brain.ChangeState(MobStateId.Chase);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Return));
    }

    [Test]
    public void ReturnState_GoesIdle_WhenDetectedTargetIsUnreachableButMobIsAlreadyAtSpawn()
    {
        SetupWorld(addHorizontalBarrier: true);

        player.position = new Vector3(0f, 3f, 0f);
        brain.ChangeState(MobStateId.Return);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    [Test]
    public void ReturnState_GoesIdle_WhenDetectedTargetIsOffGridButMobIsAlreadyAtSpawn()
    {
        SetupWorld();

        player.position = new Vector3(100f, 0f, 0f);
        brain.ChangeState(MobStateId.Return);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    [Test]
    public void IdleState_DoesNotEnterChase_WhenDetectedTargetIsUnreachable()
    {
        SetupWorld(addHorizontalBarrier: true);

        player.position = new Vector3(0f, 3f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

    [Test]
    public void AttackRangeState_DoesNotApplyDamageEveryTick_WhileOnCooldown()
    {
        SetupWorld();

        player.position = new Vector3(0.5f, 0f, 0f);
        brain.ChangeState(MobStateId.AttackRange);
        int healthAfterEnter = playerHealth.CurrentHealth;

        brain.TickStateMachine(0.1f);

        Assert.That(healthAfterEnter, Is.EqualTo(9));
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(healthAfterEnter));
    }

    [Test]
    public void AttackRangeState_DoesNotResetCooldown_WhenTargetLeavesAndReentersRange()
    {
        SetupWorld();

        player.position = new Vector3(0.5f, 0f, 0f);
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

    [Test]
    public void AttackRangeState_StopsAttacking_WhenTargetDies()
    {
        SetupWorld();

        player.position = new Vector3(0.5f, 0f, 0f);
        brain.ChangeState(MobStateId.AttackRange);
        playerHealth.ApplyDamage(playerHealth.MaxHealth);

        brain.TickStateMachine(0.1f);

        Assert.That(playerHealth.IsDead, Is.True);
        Assert.That(brain.Perception.HasDetectedTarget, Is.False);
        Assert.That(brain.CurrentStateId, Is.Not.EqualTo(MobStateId.AttackRange));
    }

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

    [Test]
    public void IdleState_DoesNotEnterChase_WhenDetectedTargetIsOffGrid()
    {
        SetupWorld();

        player.position = new Vector3(100f, 0f, 0f);
        brain.ChangeState(MobStateId.Idle);
        brain.TickStateMachine(0.1f);

        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.Idle));
    }

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

    [Test]
    public void PatrolState_OnlyRoamsToReachableCells_WhenRoamRadiusSpansAWall()
    {
        SetupWorld(addHorizontalBarrier: true);
        // The barrier row sits one cell north of spawn, so a large share of samples land on or past it.
        config.patrolRoamRadius = 4f;

        int patrols = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            Random.InitState(seed);
            brain.ChangeState(MobStateId.Idle);
            brain.ChangeState(MobStateId.Patrol);
            if (brain.CurrentStateId != MobStateId.Patrol)
            {
                continue;
            }

            patrols++;
            Assert.That(brain.PathAgent.IsPartialPath, Is.False, $"Seed {seed} picked a roam destination across the wall.");
        }

        Assert.That(patrols, Is.GreaterThan(0));
    }

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

    [Test]
    public void Separation_NeverPushesAMobIntoAWall()
    {
        // The barrier row y = 1 is unwalkable; the neighbor below pushes this mob straight at it.
        SetupWorld(new Vector3(0.5f, 0.85f, 0f), addHorizontalBarrier: true);
        CreateMob(new Vector3(0.5f, 0.55f, 0f));

        brain.FixedTickStateMachine();

        Assert.That(brain.GetComponent<Rigidbody2D>().linearVelocity.y, Is.LessThanOrEqualTo(0f));
    }

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

    // A pillar on the obstacle layer that perception linecasts against. Navigation is unaffected, so
    // the mob can still path to where it last saw the target.
    private GameObject BlockLineOfSightAt(Vector2 position)
    {
        GameObject pillar = new("Pillar") { layer = 8 };
        pillar.transform.SetParent(root.transform);
        pillar.transform.position = position;
        pillar.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 3f);
        Physics2D.SyncTransforms();
        return pillar;
    }

    private void TickBlockedPastStuckTimeout()
    {
        int fixedTicks = Mathf.CeilToInt(config.stuckTimeout / Time.fixedDeltaTime) + 1;
        for (int i = 0; i < fixedTicks; i++)
        {
            brain.FixedTickStateMachine();
        }

        brain.TickStateMachine(0.1f);
    }

    private void SetupWorld(Vector3? spawnPosition = null, bool addHorizontalBarrier = false, IReadOnlyList<Vector3Int> blockedCells = null)
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
        groundTerrain = ScriptableObject.CreateInstance<TerrainType2D>();
        groundTerrain.Configure("ground");
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

        terrainSource.Configure(dataTilemap, collisionTilemap, null, groundTerrain);
        navGrid = root.AddComponent<NavigationGrid2D>();
        navGrid.BuildGrid();

        GameObject playerObject = new("Player");
        sceneObjects.Add(playerObject);
        playerObject.tag = "Player";
        playerObject.transform.position = new Vector3(100f, 0f, 0f);
        player = playerObject.transform;
        playerHealth = playerObject.AddComponent<Health>();
        playerObject.AddComponent<DamageReceiver>();
        playerObject.AddComponent<DisableOnDeath>();

        config = ScriptableObject.CreateInstance<MobConfig>();
        config.idleDurationRange = new Vector2(10f, 10f);
        config.detectionRadius = 8f;
        config.loseTargetDistance = 10f;
        config.attackStopDistance = 1f;
        config.attackExitBuffer = 0.25f;
        config.attackDamage = 1;
        config.attackInterval = 0.75f;
        config.repathInterval = 0.1f;
        config.patrolRoamRadius = 2f;

        brain = CreateMob(spawnPosition ?? Vector3.zero);
    }

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
        mob.AddComponent<MeleeDamageDealer>();
        mob.AddComponent<MobMotor2D>();
        mob.AddComponent<MobPerception2D>();
        mob.AddComponent<MobPathAgent2D>();
        mob.AddComponent<MobPatrolAnchor>();
        MobController mobBrain = mob.AddComponent<MobController>();

        mobBrain.Configure(config, navGrid, new PlayerLocator(player, playerHealth));
        Physics2D.SyncTransforms();
        return mobBrain;
    }
}
