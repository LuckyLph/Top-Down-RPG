using UnityEngine;

/// <summary>
/// <see cref="IUnitQueries"/> for one player body, measuring range with <see cref="Collider2D.Distance"/>, finding
/// enemies around it with a non-allocating overlap on the enemy layers, and line of sight with a cast on the obstacle
/// layers.
/// </summary>
public sealed class PlayerUnitQueries : IUnitQueries
{
    private const int MaxHits = 16;

    private readonly Collider2D self;
    private readonly LayerMask enemyLayers;
    private readonly LayerMask obstacleLayers;
    private readonly Collider2D[] hits = new Collider2D[MaxHits];
    private readonly EnemyHealthLookup enemies = new();

    /// <summary>
    /// Measures from <paramref name="self"/>, the player's own collider (null measures from the origin), looks for
    /// enemies on <paramref name="enemyLayers"/> and takes the colliders on <paramref name="obstacleLayers"/> as walls
    /// that block line of sight (none of either by default).
    /// </summary>
    public PlayerUnitQueries(Collider2D self, LayerMask enemyLayers = default, LayerMask obstacleLayers = default)
    {
        this.self = self;
        this.enemyLayers = enemyLayers;
        this.obstacleLayers = obstacleLayers;
    }

    /// <summary>
    /// Whether the target's health exists and is not dead.
    /// </summary>
    public bool IsAlive(UnitTarget target)
    {
        return target.Health != null && !target.Health.IsDead;
    }

    /// <summary>
    /// The position of the target's health object, or the origin when it is gone.
    /// </summary>
    public Vector2 PositionOf(UnitTarget target)
    {
        return target.Health != null ? (Vector2)target.Health.transform.position : Vector2.zero;
    }

    /// <summary>
    /// Gap between the two enabled colliders, zero when they overlap or when the target is the player itself (an ally
    /// cast on oneself); falls back to the distance between the player's and the target's positions when either
    /// collider is missing or disabled.
    /// </summary>
    public float DistanceTo(UnitTarget target)
    {
        Collider2D other = target.Collider;
        if (self != null && other == self)
        {
            return 0f;
        }

        if (self != null && other != null && self.enabled && other.enabled)
        {
            ColliderDistance2D distance = self.Distance(other);
            if (distance.isValid)
            {
                return Mathf.Max(0f, distance.distance);
            }
        }

        Vector2 origin = self != null ? (Vector2)self.transform.position : Vector2.zero;
        return Vector2.Distance(origin, PositionOf(target));
    }

    /// <summary>
    /// Casts a circle of <paramref name="radius"/> (a ray for 0) on the obstacle layers from the player's position, its
    /// feet, toward the middle of the target's collider, as a projectile flies, up to where the circle first touches
    /// the target's collider (its position without an enabled one). True when it meets nothing, when the target is the
    /// player itself or touches it already, and without obstacle layers.
    /// </summary>
    public bool InSight(UnitTarget target, float radius)
    {
        Collider2D other = target.Collider;
        if (obstacleLayers.value == 0 || (self != null && other == self))
        {
            return true;
        }

        Vector2 origin = self != null ? (Vector2)self.transform.position : Vector2.zero;
        bool hasCollider = other != null && other.enabled;
        Vector2 toMiddle = (hasCollider ? (Vector2)other.bounds.center : PositionOf(target)) - origin;
        float length = toMiddle.magnitude;
        float reach = (hasCollider ? Vector2.Distance(origin, other.ClosestPoint(origin)) : length) - Mathf.Max(0f, radius);
        if (length < 0.0001f || reach <= 0f)
        {
            return true;
        }

        Vector2 direction = toMiddle / length;
        RaycastHit2D hit = radius > 0f
            ? Physics2D.CircleCast(origin, radius, direction, reach, obstacleLayers)
            : Physics2D.Raycast(origin, direction, reach, obstacleLayers);
        return hit.collider == null;
    }

    /// <summary>
    /// Overlaps a circle around the player's collider wide enough to hold <paramref name="range"/> on the enemy layers
    /// (no triggers), keeps the living enemies whose colliders are within range, and returns the one whose position
    /// is closest to <paramref name="point"/>. Empty without a collider of its own or enemy layers.
    /// </summary>
    public UnitTarget NearestEnemyInRange(Vector2 point, float range)
    {
        if (self == null || enemyLayers.value == 0)
        {
            return default;
        }

        Bounds bounds = self.bounds;
        ContactFilter2D filter = new() { useTriggers = false };
        filter.SetLayerMask(enemyLayers);
        int count = Physics2D.OverlapCircle(bounds.center, range + ((Vector2)bounds.extents).magnitude, filter, hits);

        UnitTarget best = default;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = hits[i];
            hits[i] = null;
            Health health = enemies.Find(hit);
            if (health == null || health.IsDead)
            {
                continue;
            }

            UnitTarget candidate = new(health, hit, UnitTeam.Enemy);
            float distance = ((Vector2)health.transform.position - point).sqrMagnitude;
            if (distance < bestDistance && DistanceTo(candidate) <= range)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }
}
