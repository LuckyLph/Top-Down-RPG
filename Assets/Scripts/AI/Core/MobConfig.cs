using UnityEngine;

[CreateAssetMenu(menuName = "AI/Mob Config", fileName = "MobConfig")]
public class MobConfig : ScriptableObject
{
    [Header("Movement")]
    [Min(0f)] public float moveSpeed = 2.5f;
    [Min(0f)] public float acceleration = 20f;
    [Min(0.01f)] public float waypointReachDistance = 0.05f;
    [Min(0.01f)] public float arrivalDistance = 0.15f;

    [Header("Perception")]
    [Min(0f)] public float detectionRadius = 6f;
    [Min(0f)] public float loseTargetDistance = 8f;
    [Min(0.01f)] public float lineOfSightInterval = 0.2f;
    public LayerMask obstacleLayerMask = 1 << 8;

    [Header("Combat")]
    [Min(0f)] public float attackStopDistance = 1.25f;
    [Min(0f)] public float attackExitBuffer = 0.25f;
    [Min(0)] public int attackDamage = 1;
    [Min(0f)] public float attackInterval = 0.75f;

    [Header("Patrol")]
    [Min(0f)] public float patrolRoamRadius = 4f;
    [Min(1)] public int patrolSampleAttempts = 12;
    public Vector2 idleDurationRange = new(0.5f, 1.5f);

    [Header("Navigation")]
    [Min(0.01f)] public float repathInterval = 0.25f;
    [Min(1)] public int nearestCellSearchRadius = 8;
    // Patrol and return give up after following a path this long without making headway.
    [Min(0.1f)] public float stuckTimeout = 1f;
    public TerrainMovementProfile2D movementProfile;

    public TerrainMovementProfile2D MovementProfile => movementProfile;

    public float NextIdleDuration()
    {
        float min = Mathf.Min(idleDurationRange.x, idleDurationRange.y);
        float max = Mathf.Max(idleDurationRange.x, idleDurationRange.y);
        return Random.Range(min, max);
    }
}
