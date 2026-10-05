using UnityEngine;

/// <summary>
/// <see cref="IUnitQueries"/> for one player body, measuring range with <see cref="Collider2D.Distance"/>.
/// </summary>
public sealed class PlayerUnitQueries : IUnitQueries
{
    private readonly Collider2D self;

    /// <summary>
    /// Measures from <paramref name="self"/>, the player's own collider (null measures from the origin).
    /// </summary>
    public PlayerUnitQueries(Collider2D self)
    {
        this.self = self;
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
}
