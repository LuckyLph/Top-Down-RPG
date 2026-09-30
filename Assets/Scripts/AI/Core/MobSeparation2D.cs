using UnityEngine;

// Keeps mobs from stacking. Mob bodies are kinematic, so physics never pushes them apart; instead each
// mob senses nearby mobs on its own layer once per physics step and steers away from them, harder the
// more they overlap. A push that would carry the mob into a cell it cannot walk on is dropped per
// axis, so crowds slide along walls instead of clipping into them.
public sealed class MobSeparation2D
{
    private const int MaxNeighbors = 32;
    // Neighbors are sensed a bit beyond the push radius so HasNeighborToward can be asked about a
    // wider release distance (see ChaseState's waiting hysteresis).
    public const float SenseRadiusFactor = 1.5f;
    // Golden angle, used to give exactly coincident mobs distinct push directions.
    private const float GoldenAngle = 2.39996323f;

    private readonly Collider2D[] neighbors = new Collider2D[MaxNeighbors];
    private readonly Collider2D self;
    private readonly MobConfig config;
    private readonly NavigationGrid2D navigationGrid;
    private readonly ContactFilter2D filter;
    private int neighborCount;
    private Vector2 position;
    private Vector2 velocity;

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

    // Push away from neighbors from the last Sense call, in units per second.
    public Vector2 Velocity => velocity;

    public void Sense(Vector2 mobPosition)
    {
        position = mobPosition;
        neighborCount = 0;
        velocity = Vector2.zero;

        float radius = config.separationRadius;
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

        velocity = ClampToWalkable(push * config.separationStrength);
    }

    // Whether another mob stands directly between this one and the target, within maxDistance (at most
    // SenseRadiusFactor times the separation radius). Chasers use it to wait behind a crowd instead of
    // pushing into it.
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
            // Within about 60 degrees of straight ahead, and not beyond the target.
            if (distance > 0.0001f && distance <= maxDistance && distance < distanceToTarget && Vector2.Dot(toOther / distance, forward) > 0.5f)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2 NeighborPosition(Collider2D other)
    {
        return other.attachedRigidbody != null ? other.attachedRigidbody.position : (Vector2)other.transform.position;
    }

    // Two mobs on exactly the same spot need opposite pushes: both derive one axis from the pair and
    // take opposite ends of it.
    private Vector2 CoincidentDirection(Collider2D other)
    {
        int selfId = self.GetInstanceID();
        int otherId = other.GetInstanceID();
        float angle = Mathf.Repeat(Mathf.Min(selfId, otherId) * GoldenAngle, Mathf.PI * 2f);
        Vector2 axis = new(Mathf.Cos(angle), Mathf.Sin(angle));
        return selfId < otherId ? axis : -axis;
    }

    private Vector2 ClampToWalkable(Vector2 push)
    {
        if (navigationGrid == null || !navigationGrid.IsBuilt || push.sqrMagnitude <= 0f)
        {
            return push;
        }

        float lookAhead = config.separationRadius * 0.5f;
        Vector2 horizontal = new(push.x, 0f);
        Vector2 vertical = new(0f, push.y);
        return (IsWalkableToward(horizontal, lookAhead) ? horizontal : Vector2.zero)
            + (IsWalkableToward(vertical, lookAhead) ? vertical : Vector2.zero);
    }

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
