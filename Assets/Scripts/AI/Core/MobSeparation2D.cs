using UnityEngine;

/// <summary>
/// Crowd steering for one mob: each fixed tick it senses the mobs on its own layer and pushes away from those closer
/// than the separation radius, never toward an unwalkable cell. Also tells a chasing mob when an ally stands between
/// it and its target.
/// </summary>
public sealed class MobSeparation2D
{
    private const int MaxNeighbors = 32;
    public const float SenseRadiusFactor = 1.5f;
    private const float GoldenAngle = 2.39996323f;

    private readonly Collider2D[] neighbors = new Collider2D[MaxNeighbors];
    private readonly Collider2D self;
    private readonly MobConfig config;
    private readonly NavigationGrid2D navigationGrid;
    private readonly ContactFilter2D filter;
    private int neighborCount;
    private Vector2 position;
    private Vector2 velocity;

    /// <summary>
    /// Creates the separation for <paramref name="selfCollider"/>, sensing non-trigger colliders on its layer, with the
    /// crowd tuning of <paramref name="mobConfig"/> and walkability from <paramref name="grid"/> (which may be null).
    /// </summary>
    public MobSeparation2D(Collider2D selfCollider, MobConfig mobConfig, NavigationGrid2D grid)
    {
        self = selfCollider;
        config = mobConfig;
        navigationGrid = grid;
        filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = self != null ? 1 << self.gameObject.layer : 0,
            useTriggers = false
        };
    }

    public Vector2 Velocity => velocity;

    /// <summary>
    /// Collects the neighbours around <paramref name="mobPosition"/> and sets <see cref="Velocity"/> to the push away
    /// from those within the separation radius, weighted by overlap and scaled by the separation strength. Zero when
    /// separation is off.
    /// </summary>
    public void Sense(Vector2 mobPosition)
    {
        position = mobPosition;
        neighborCount = 0;
        velocity = Vector2.zero;

        float radius = config.SeparationRadius;
        if (self == null || radius <= 0f)
        {
            return;
        }

        neighborCount = Physics2D.OverlapCircle(position, radius * SenseRadiusFactor, filter, neighbors);
        Vector2 push = Vector2.zero;
        for (int i = 0; i < neighborCount; i++)
        {
            Collider2D other = neighbors[i];
            if (other == self)
            {
                continue;
            }

            Vector2 away = position - NeighborPosition(other);
            float distance = away.magnitude;
            if (distance >= radius)
            {
                continue;
            }

            push += (distance > 0.0001f ? away / distance : CoincidentDirection(other)) * (1f - distance / radius);
        }

        velocity = ClampToWalkable(push * config.SeparationStrength);
    }

    /// <summary>
    /// Whether a neighbour from the last <see cref="Sense"/> lies within <paramref name="maxDistance"/>, closer than
    /// <paramref name="target"/>, and less than 60 degrees off the direction to it.
    /// </summary>
    public bool HasNeighborToward(Vector2 target, float maxDistance)
    {
        Vector2 toTarget = target - position;
        float distanceToTarget = toTarget.magnitude;
        if (distanceToTarget <= 0.0001f)
        {
            return false;
        }

        Vector2 forward = toTarget / distanceToTarget;
        for (int i = 0; i < neighborCount; i++)
        {
            Collider2D other = neighbors[i];
            if (other == null || other == self)
            {
                continue;
            }

            Vector2 toOther = NeighborPosition(other) - position;
            float distance = toOther.magnitude;
            if (distance > 0.0001f && distance <= maxDistance && distance < distanceToTarget && Vector2.Dot(toOther / distance, forward) > 0.5f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A neighbour's rigidbody position, or its transform position when it has no rigidbody.
    /// </summary>
    private static Vector2 NeighborPosition(Collider2D other)
    {
        return other.attachedRigidbody != null ? other.attachedRigidbody.position : (Vector2)other.transform.position;
    }

    /// <summary>
    /// A deterministic push direction for a neighbour at exactly the mob's position: an axis derived from the lower
    /// instance id, pointing opposite ways for the two mobs.
    /// </summary>
    private Vector2 CoincidentDirection(Collider2D other)
    {
        int selfId = self.GetInstanceID();
        int otherId = other.GetInstanceID();
        float angle = Mathf.Repeat(Mathf.Min(selfId, otherId) * GoldenAngle, Mathf.PI * 2f);
        Vector2 axis = new(Mathf.Cos(angle), Mathf.Sin(angle));
        return selfId < otherId ? axis : -axis;
    }

    /// <summary>
    /// Drops each axis of <paramref name="push"/> that would lead into an unwalkable cell within half the separation
    /// radius; unchanged without a built grid.
    /// </summary>
    private Vector2 ClampToWalkable(Vector2 push)
    {
        if (navigationGrid == null || !navigationGrid.IsBuilt || push.sqrMagnitude <= 0f)
        {
            return push;
        }

        float lookAhead = config.SeparationRadius * 0.5f;
        Vector2 horizontal = new(push.x, 0f);
        Vector2 vertical = new(0f, push.y);
        return (IsWalkableToward(horizontal, lookAhead) ? horizontal : Vector2.zero)
            + (IsWalkableToward(vertical, lookAhead) ? vertical : Vector2.zero);
    }

    /// <summary>
    /// Whether the cell <paramref name="lookAhead"/> along <paramref name="direction"/> from the mob is walkable; true
    /// for a zero direction.
    /// </summary>
    private bool IsWalkableToward(Vector2 direction, float lookAhead)
    {
        if (direction.sqrMagnitude <= 0f)
        {
            return true;
        }

        Vector3Int cell = navigationGrid.WorldToCell(position + direction.normalized * lookAhead);
        return navigationGrid.IsCellWalkable(cell, config.MovementProfile);
    }
}
