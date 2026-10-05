using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Random = System.Random;

/// <summary>
/// Dev-only seeded mob spawner for the stress test area. Instantiates mobs through the container on walkable cells
/// reachable from its own position, either in a band around the player or anywhere on the navigation grid, and
/// tracks them so they can be counted and cleared.
/// </summary>
public class StressTestSpawner : MonoBehaviour
{
    private const int AttemptsPerMob = 30;

    [SerializeField] private MobController mobPrefab;
    [SerializeField, Min(0)] private int initialNearSpawn = 20;
    [SerializeField, Min(0)] private int initialAcrossMap = 40;
    [Tooltip("Distance band around the player (or spawn point) for 'near' spawns. Detection radius is 6.")]
    [SerializeField] private Vector2 nearDistance = new(2.5f, 7f);
    [Tooltip("Mobs never spawn closer than this to the player.")]
    [SerializeField, Min(0f)] private float minPlayerDistance = 2f;
    [SerializeField] private int seed = 1234;

    private readonly List<MobController> mobs = new();
    private IObjectResolver resolver;
    private NavigationGrid2D navigationGrid;
    private LocalPlayerTracker localPlayer;
    private Random random;
    private Transform mobRoot;
    private int spawnedTotal;

    public int MobCount
    {
        get
        {
            PruneDestroyed();
            return mobs.Count;
        }
    }

    public IReadOnlyList<MobController> Mobs
    {
        get
        {
            PruneDestroyed();
            return mobs;
        }
    }

    public int InitialMobCount => initialNearSpawn + initialAcrossMap;

    /// <summary>
    /// Receives the area container used to instantiate mobs, the navigation grid and the local player tracker.
    /// </summary>
    [Inject]
    public void Construct(IObjectResolver objectResolver, NavigationGrid2D grid, LocalPlayerTracker localPlayerTracker)
    {
        resolver = objectResolver;
        navigationGrid = grid;
        localPlayer = localPlayerTracker;
    }

    /// <summary>
    /// Disables itself with an error when it was not injected or has no mob prefab; otherwise creates the mob root
    /// and spawns the initial mobs around itself and across the map.
    /// </summary>
    private void Start()
    {
        if (resolver == null || mobPrefab == null)
        {
            Debug.LogError($"{name} needs a mob prefab and must be listed in the AreaLifetimeScope's auto-inject objects.", this);
            enabled = false;
            return;
        }

        random = new Random(seed);
        mobRoot = new GameObject("StressMobs").transform;
        mobRoot.SetParent(transform, false);

        SpawnAround(transform.position, initialNearSpawn);
        SpawnAcrossMap(initialAcrossMap);
    }

    /// <summary>
    /// Spawns up to <paramref name="count"/> mobs in the near band around the local player (or this spawner when
    /// there is none). Returns how many were spawned.
    /// </summary>
    public int SpawnNearPlayer(int count)
    {
        return SpawnAround(PlayerPosition(), count);
    }

    /// <summary>
    /// Spawns up to <paramref name="count"/> mobs at random distances within the near band around
    /// <paramref name="center"/>. Returns how many were spawned.
    /// </summary>
    public int SpawnAround(Vector2 center, int count)
    {
        return Spawn(count, () =>
        {
            double angle = random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Lerp(nearDistance.x, nearDistance.y, Mathf.Sqrt((float)random.NextDouble()));
            return center + new Vector2(Mathf.Cos((float)angle), Mathf.Sin((float)angle)) * distance;
        });
    }

    /// <summary>
    /// Spawns up to <paramref name="count"/> mobs on random cells within the navigation grid's walkable bounds.
    /// Returns how many were spawned.
    /// </summary>
    public int SpawnAcrossMap(int count)
    {
        BoundsInt bounds = navigationGrid.WalkableBounds;
        return Spawn(count, () => navigationGrid.CellToWorldCenter(new Vector3Int(
            random.Next(bounds.xMin, bounds.xMax),
            random.Next(bounds.yMin, bounds.yMax),
            0)));
    }

    /// <summary>
    /// Destroys every tracked mob that still exists and forgets them all.
    /// </summary>
    public void ClearMobs()
    {
        foreach (MobController mob in mobs)
        {
            if (mob != null)
            {
                Destroy(mob.gameObject);
            }
        }

        mobs.Clear();
    }

    /// <summary>
    /// Tries up to <see cref="AttemptsPerMob"/> candidates per mob from <paramref name="sampleCandidate"/>, keeping
    /// walkable cells connected to the walkable cell nearest the spawner and not too close to the player, and
    /// instantiates a mob on each. Logs a warning when fewer than <paramref name="count"/> spawn, and returns 0
    /// before <see cref="Start"/> has run or when the spawner is not near any walkable cell.
    /// </summary>
    private int Spawn(int count, System.Func<Vector2> sampleCandidate)
    {
        if (mobRoot == null)
        {
            return 0;
        }

        TerrainMovementProfile2D profile = mobPrefab.Config != null ? mobPrefab.Config.MovementProfile : null;
        if (!navigationGrid.TryGetNearestWalkableCell(navigationGrid.WorldToCell(transform.position), profile, out Vector3Int anchorCell))
        {
            Debug.LogError($"{name} is not near any walkable cell, so no mobs can spawn.", this);
            return 0;
        }

        int spawned = 0;
        for (int attempt = 0; attempt < count * AttemptsPerMob && spawned < count; attempt++)
        {
            Vector3Int cell = navigationGrid.WorldToCell(sampleCandidate());
            if (!navigationGrid.IsCellWalkable(cell, profile) || !navigationGrid.AreCellsConnected(anchorCell, cell, profile))
            {
                continue;
            }

            Vector2 position = navigationGrid.CellToWorldCenter(cell);
            if (Vector2.Distance(position, PlayerPosition()) < minPlayerDistance)
            {
                continue;
            }

            MobController mob = resolver.Instantiate(mobPrefab, position, Quaternion.identity, mobRoot);
            mob.name = $"StressMob_{++spawnedTotal:000}";
            mobs.Add(mob);
            spawned++;
        }

        if (spawned < count)
        {
            Debug.LogWarning($"{name} spawned {spawned} of {count} mobs; not enough free reachable cells were found.", this);
        }

        return spawned;
    }

    /// <summary>
    /// The local player's position, or this spawner's when there is no local player.
    /// </summary>
    private Vector2 PlayerPosition()
    {
        LocalPlayer player = localPlayer != null ? localPlayer.Current : null;
        return player != null && player.Transform != null ? player.Transform.position : transform.position;
    }

    /// <summary>
    /// Drops tracked mobs that have been destroyed since they spawned.
    /// </summary>
    private void PruneDestroyed()
    {
        mobs.RemoveAll(mob => mob == null);
    }
}
