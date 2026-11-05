using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers where a mob spawned and supplies its patrol: idle durations and reachable roam destinations around the
/// spawn point, drawn from the injected <see cref="IRandom"/>. A mob of a world also has a home, its home chunk's area:
/// it patrols only inside it and its leash (<see cref="MobConfig.LeashCells"/>) reaches that many cells beyond it. A
/// mob of an area has no home and no leash.
/// </summary>
public class MobPatrolAnchor : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawPatrolGizmos = true;
    [SerializeField] private Color patrolRadiusColor = new(0.6f, 0.4f, 1f, 0.75f);
    [SerializeField] private Color spawnPointColor = new(0.9f, 0.9f, 1f, 0.85f);

    private INavigation2D navigation;
    private MobConfig config;
    private IRandom random;
    private Vector2 spawnPosition;
    private Rect homeArea;
    private bool hasHome;
    private bool isLeashed;

    public Vector2 SpawnPosition => spawnPosition;
    public bool HasHome => hasHome;
    public Rect HomeArea => homeArea;
    public bool IsLeashed => isLeashed;

    /// <summary>
    /// Records the spawn position.
    /// </summary>
    private void Awake()
    {
        spawnPosition = transform.position;
    }

    /// <summary>
    /// Takes the navigation, config and random source, records the current position as the spawn position again and
    /// lets go of the leash.
    /// </summary>
    public void Initialize(INavigation2D mobNavigation, MobConfig mobConfig, IRandom randomSource)
    {
        navigation = mobNavigation;
        config = mobConfig;
        random = randomSource;
        spawnPosition = transform.position;
        isLeashed = false;
    }

    /// <summary>
    /// Records whether the mob gave up on a target at its leash and is going home: while it is, it spots no target, so
    /// a target it cannot reach without leaving the leash does not pull it back and forth.
    /// </summary>
    public void SetLeashed(bool leashed)
    {
        isLeashed = leashed;
    }

    /// <summary>
    /// Makes <paramref name="area"/> (world units, a cell being one unit) the mob's home: its home chunk's cells. Kept
    /// across <see cref="Initialize"/>.
    /// </summary>
    public void SetHome(Rect area)
    {
        homeArea = area;
        hasHome = true;
    }

    /// <summary>
    /// Whether <paramref name="position"/> lies inside the home area or within the leash beyond its edge; always true
    /// for a mob without a home or a config.
    /// </summary>
    public bool IsWithinLeash(Vector2 position)
    {
        if (!hasHome || config == null)
        {
            return true;
        }

        float leash = config.LeashCells;
        return position.x >= homeArea.xMin - leash && position.x <= homeArea.xMax + leash
            && position.y >= homeArea.yMin - leash && position.y <= homeArea.yMax + leash;
    }

    /// <summary>
    /// Whether every one of <paramref name="waypoints"/> lies within the leash.
    /// </summary>
    public bool IsPathWithinLeash(IReadOnlyList<Vector2> waypoints)
    {
        if (!hasHome || waypoints == null)
        {
            return true;
        }

        for (int i = 0; i < waypoints.Count; i++)
        {
            if (!IsWithinLeash(waypoints[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="position"/> lies inside the home area; always true for a mob without a home.
    /// </summary>
    public bool IsInHome(Vector2 position)
    {
        return !hasHome || homeArea.Contains(position);
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
    /// inside the home area that <paramref name="isReachable"/> accepts (any, when it is null). False without a grid,
    /// config or random source, or when no sample qualifies.
    /// </summary>
    public bool TryGetRoamDestination(Func<Vector2, bool> isReachable, out Vector2 destination)
    {
        destination = spawnPosition;

        if (navigation == null || config == null || random == null)
        {
            return false;
        }

        int attempts = Mathf.Max(1, config.PatrolSampleAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = random.InsideUnitCircle() * config.PatrolRoamRadius;
            Vector2 candidateWorld = spawnPosition + offset;
            Vector3Int candidateCell = navigation.WorldToCell(candidateWorld);

            if (!navigation.IsCellWalkable(candidateCell, config.MovementProfile)
                && !navigation.TryGetNearestWalkableCell(candidateCell, config.MovementProfile, out candidateCell))
            {
                continue;
            }

            Vector2 candidate = navigation.CellToWorldCenter(candidateCell);
            if (!IsInHome(candidate))
            {
                continue;
            }

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
