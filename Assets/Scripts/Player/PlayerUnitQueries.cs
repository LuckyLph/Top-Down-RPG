using UnityEngine;

/// <summary>
/// <see cref="IUnitQueries"/> for one player body, measuring range with <see cref="Collider2D.Distance"/>.
/// </summary>
public sealed class PlayerUnitQueries : IUnitQueries
{
    private readonly Collider2D self;

    public PlayerUnitQueries(Collider2D self)
    {
        this.self = self;
    }

    public bool IsAlive(UnitTarget target)
    {
        return target.Health != null && !target.Health.IsDead;
    }

    public Vector2 PositionOf(UnitTarget target)
    {
        return target.Health != null ? (Vector2)target.Health.transform.position : Vector2.zero;
    }

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
