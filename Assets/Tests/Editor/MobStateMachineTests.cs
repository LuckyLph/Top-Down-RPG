using NUnit.Framework;
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

    [TearDown]
    public void TearDown()
    {
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

    private void SetupWorld(Vector3? spawnPosition = null, bool addHorizontalBarrier = false)
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

        terrainSource.Configure(dataTilemap, collisionTilemap, null, groundTerrain);
        navGrid = root.AddComponent<NavigationGrid2D>();
        navGrid.BuildGrid();

        GameObject providerObject = new("MobTargetProvider");
        providerObject.transform.SetParent(root.transform);
        MobTargetProvider provider = providerObject.AddComponent<MobTargetProvider>();

        GameObject playerObject = new("Player");
        playerObject.tag = "Player";
        playerObject.transform.position = new Vector3(100f, 0f, 0f);
        player = playerObject.transform;
        playerHealth = playerObject.AddComponent<Health>();
        playerObject.AddComponent<DamageReceiver>();
        playerObject.AddComponent<DisableOnDeath>();
        provider.SetTarget(player);

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

        GameObject mob = new("Mob");
        mob.transform.position = spawnPosition ?? Vector3.zero;
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
        brain = mob.AddComponent<MobController>();

        brain.Configure(config, navGrid, provider);
    }
}
