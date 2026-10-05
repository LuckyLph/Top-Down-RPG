using UnityEngine;

/// <summary>
/// <see cref="IUnitQueries"/> for one player body, measuring range with <see cref="Collider2D.Distance"/> and finding
/// enemies around it with a non-allocating overlap on the enemy layers.
/// </summary>
public sealed class PlayerUnitQueries : IUnitQueries
{
    private const int MaxHits = 16;

    private readonly Collider2D self;
    private readonly LayerMask enemyLayers;
    private readonly Collider2D[] hits = new Collider2D[MaxHits];
    private readonly EnemyHealthLookup enemies = new();

    /// <summary>
    /// Measures from <paramref name="self"/>, the player's own collider (null measures from the origin), and looks
    /// for enemies on <paramref name="enemyLayers"/> (none by default).
    /// </summary>
    public PlayerUnitQueries(Collider2D self, LayerMask enemyLayers = default)
    {
        this.self = self;
        this.enemyLayers = enemyLayers;
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
    /// Gap between the two enabled colliders, zero when they overlap; falls back to the distance between the
    /// player's and the target's positions when either collider is missing or disabled.
    /// </summary>
    public float DistanceTo(UnitTarget target)
    {
        Collider2D other = target.Collider;
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
