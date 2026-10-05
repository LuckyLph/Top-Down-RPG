using System;
using UnityEngine;

/// <summary>
/// Remembers where a mob spawned and supplies its patrol: idle durations and reachable roam destinations around the
/// spawn point, drawn from the injected <see cref="IRandom"/>.
/// </summary>
public class MobPatrolAnchor : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawPatrolGizmos = true;
    [SerializeField] private Color patrolRadiusColor = new(0.6f, 0.4f, 1f, 0.75f);
    [SerializeField] private Color spawnPointColor = new(0.9f, 0.9f, 1f, 0.85f);

    private NavigationGrid2D navigationGrid;
    private MobConfig config;
    private IRandom random;
    private Vector2 spawnPosition;

    public Vector2 SpawnPosition => spawnPosition;

    /// <summary>
    /// Records the spawn position.
    /// </summary>
    private void Awake()
    {
        spawnPosition = transform.position;
    }

    /// <summary>
    /// Takes the grid, config and random source, and records the current position as the spawn position again.
    /// </summary>
    public void Initialize(NavigationGrid2D navGrid, MobConfig mobConfig, IRandom randomSource)
    {
        navigationGrid = navGrid;
        config = mobConfig;
        random = randomSource;
        spawnPosition = transform.position;
    }

    /// <summary>
    /// A random idle duration from the config, or 1 second without a config or random source.
    /// </summary>
    public float GetIdleDuration()
    {
        return config != null && random != null ? config.NextIdleDuration(random) : 1f;
    }

    /// <summary>
    /// Samples up to <see cref="MobConfig.PatrolSampleAttempts"/> points within the roam radius of the spawn, snaps
    /// each to the center of its cell (or of the nearest walkable cell when it is not walkable), and returns the first
    /// that <paramref name="isReachable"/> accepts (any, when it is null). False without a grid, config or random
    /// source, or when no sample qualifies.
    /// </summary>
    public bool TryGetRoamDestination(Func<Vector2, bool> isReachable, out Vector2 destination)
    {
        destination = spawnPosition;

        if (navigationGrid == null || config == null || random == null)
        {
            return false;
        }

        int attempts = Mathf.Max(1, config.PatrolSampleAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = random.InsideUnitCircle() * config.PatrolRoamRadius;
            Vector2 candidateWorld = spawnPosition + offset;
            Vector3Int candidateCell = navigationGrid.WorldToCell(candidateWorld);

            if (!navigationGrid.IsCellWalkable(candidateCell, config.MovementProfile)
                && !navigationGrid.TryGetNearestWalkableCell(candidateCell, config.MovementProfile, out candidateCell, config.NearestCellSearchRadius))
            {
                continue;
            }

            Vector2 candidate = navigationGrid.CellToWorldCenter(candidateCell);
            if (isReachable == null || isReachable(candidate))
            {
                destination = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Draws the roam radius and the spawn point, falling back to the object's position outside Play Mode while no
    /// spawn position is recorded.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawPatrolGizmos)
        {
            return;
        }

        Vector3 center = spawnPosition;
        if (!Application.isPlaying && center == Vector3.zero)
        {
            center = transform.position;
        }

        Gizmos.color = patrolRadiusColor;
        float radius = config != null ? config.PatrolRoamRadius : 0f;
        if (radius > 0f)
        {
            Gizmos.DrawWireSphere(center, radius);
        }

        Gizmos.color = spawnPointColor;
        Gizmos.DrawWireSphere(center, 0.08f);
    }
}
