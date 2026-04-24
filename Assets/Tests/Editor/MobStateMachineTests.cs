using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MobStateMachineTests
{
    private GameObject root;
    private MobConfig config;
    private Tile walkTile;
    private MobBrain brain;
    private Transform player;
    private NavigationGrid2D navGrid;

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

    private void SetupWorld(Vector3? spawnPosition = null)
    {
        root = new GameObject("MobStateMachineTestRoot");
        Grid grid = root.AddComponent<Grid>();

        GameObject dataObject = new("DataTilemap");
        dataObject.transform.SetParent(root.transform);
        Tilemap dataTilemap = dataObject.AddComponent<Tilemap>();
        dataObject.AddComponent<TilemapRenderer>();

        walkTile = ScriptableObject.CreateInstance<Tile>();
        for (int x = -10; x <= 10; x++)
        {
            for (int y = -10; y <= 10; y++)
            {
                dataTilemap.SetTile(new Vector3Int(x, y, 0), walkTile);
            }
        }

        navGrid = root.AddComponent<NavigationGrid2D>();
        navGrid.Configure(dataTilemap, null);
        navGrid.BuildGrid();

        GameObject providerObject = new("MobTargetProvider");
        providerObject.transform.SetParent(root.transform);
        MobTargetProvider provider = providerObject.AddComponent<MobTargetProvider>();

        GameObject playerObject = new("Player");
        playerObject.tag = "Player";
        playerObject.transform.position = new Vector3(100f, 0f, 0f);
        player = playerObject.transform;
        provider.SetTarget(player);

        config = ScriptableObject.CreateInstance<MobConfig>();
        config.idleDurationRange = new Vector2(10f, 10f);
        config.detectionRadius = 8f;
        config.loseTargetDistance = 10f;
        config.attackStopDistance = 1f;
        config.attackExitBuffer = 0.25f;
        config.repathInterval = 0.1f;
        config.patrolRoamRadius = 2f;

        GameObject mob = new("Mob");
        mob.transform.position = spawnPosition ?? Vector3.zero;
        Rigidbody2D rb = mob.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        mob.AddComponent<BoxCollider2D>();
        mob.AddComponent<SpriteRenderer>();
        mob.AddComponent<Animator>();
        mob.AddComponent<MobMotor2D>();
        mob.AddComponent<MobPerception2D>();
        mob.AddComponent<MobPathAgent2D>();
        mob.AddComponent<MobPatrolRoam>();
        brain = mob.AddComponent<MobBrain>();

        brain.Configure(config, navGrid, provider);
    }
}
